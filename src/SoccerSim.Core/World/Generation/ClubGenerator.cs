using System.Globalization;
using System.Text;
using SoccerSim.Core.Random;
using SoccerSim.Core.World.Color;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// What the owner decides about a club that does not exist yet: where (the country), how big
/// (the prestige band, authored per D-38) and how strong. Everything else is drawn from the
/// country's <see cref="ClubProfiles"/>. <see cref="Seed"/> is the determinism contract: the
/// same request against the same profiles and the same world produces the same club.
/// </summary>
public sealed record ClubGenerationRequest(
    string CountryId,
    PrestigeBand Band,
    double ClubStrength,
    long Seed);

/// <summary>
/// What already exists and must not be reused: ids, display codes, short names, and how many
/// clubs each city already holds. Built from the world; a batch folds each new club back in with
/// <see cref="With"/> so two clubs of the same batch cannot collide either.
/// </summary>
public sealed record ClubGenerationContext(
    IReadOnlySet<string> ClubIds,
    IReadOnlySet<string> DisplayCodes,
    IReadOnlySet<string> ShortNames,
    IReadOnlyDictionary<string, int> ClubsPerCity)
{
    public static ClubGenerationContext From(IEnumerable<ClubIdentity> clubs)
    {
        List<ClubIdentity> list = clubs.ToList();
        return new ClubGenerationContext(
            list.Select(club => club.ClubId).ToHashSet(StringComparer.Ordinal),
            list.Select(club => club.DisplayCode).ToHashSet(StringComparer.Ordinal),
            list.Select(club => club.Identity.ShortName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            list.GroupBy(club => club.Geography.GeoNodeId).ToDictionary(group => group.Key, group => group.Count()));
    }

    public ClubGenerationContext With(ClubIdentity club)
    {
        var perCity = ClubsPerCity.ToDictionary(entry => entry.Key, entry => entry.Value);
        perCity[club.Geography.GeoNodeId] = perCity.GetValueOrDefault(club.Geography.GeoNodeId) + 1;
        return new ClubGenerationContext(
            ClubIds.Append(club.ClubId).ToHashSet(StringComparer.Ordinal),
            DisplayCodes.Append(club.DisplayCode).ToHashSet(StringComparer.Ordinal),
            ShortNames.Append(club.Identity.ShortName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            perCity);
    }
}

/// <summary>
/// Builds a complete club from nothing (ADR-0011 §3, Sprint 10). Pure: no database, no clock, no
/// ambient state — the same arguments always produce the same club.
///
/// <para><b>On safety.</b> The club is <see cref="Provenance.Regen"/>: it has no anchor, so its
/// <see cref="ClubIdentity.Audit"/> is null and it asserts nothing about any real club. That is
/// what lets generation invent names, crests and founding years freely — the same argument
/// <see cref="SquadGenerator"/> makes for players.</para>
///
/// <para><b>On failure.</b> Missing data is an error with its reason, never a default: a country
/// without a stadium profile, a city the geo tree does not know, every city full, every name or
/// code taken, a palette that cannot reach the ΔE threshold.</para>
/// </summary>
public static class ClubGenerator
{
    /// <summary>How many palettes to draw before concluding the colour families cannot produce
    /// two kits far enough apart. Bounded so a bad profile fails instead of looping.</summary>
    private const int PaletteAttempts = 16;

    /// <summary>The latest founding year: the season before the contract's reference date
    /// (DATA_CONTRACT.md §4, 2026-01-28).</summary>
    private const int LatestFoundingYear = 2025;

    private const string CityPrefix = "geo_city_";

    public static ClubIdentity Generate(
        ClubGenerationRequest request,
        ClubProfiles profiles,
        IReadOnlyList<GeoNode> geoNodes,
        WorldCalibration calibration,
        ClubGenerationContext taken,
        long masterSeed)
    {
        Validate(request, profiles, geoNodes, calibration);

        IDeterministicRandom rng = DeterministicRng.CreateStream(
            (ulong)masterSeed,
            StableHash.Of("club"),
            StableHash.Of(request.CountryId),
            unchecked((ulong)request.Seed));

        double threshold = calibration.Constant("deltaEThreshold");
        Dictionary<string, GeoNode> nodes = geoNodes.ToDictionary(node => node.GeoNodeId);

        CityProfile city = DrawCity(rng, profiles, taken);
        string cityName = nodes[city.GeoNodeId].DisplayName;
        DistrictArchetype district = rng.Weighted(city.Districts);

        string prefix = rng.Weighted(profiles.ClubPrefixes);
        (string shortName, string officialName) = DrawName(rng, profiles, taken, city, cityName, district, prefix);
        string displayCode = DrawDisplayCode(shortName, cityName, taken);
        string clubId = NextClubId(request.CountryId, city.GeoNodeId, taken);

        (ColorFamily primary, ColorFamily secondary, ColorFamily tertiary) = DrawPalette(rng, profiles, threshold);
        var palette = new ClubPalette(primary.Hex, secondary.Hex, tertiary.Hex, rng.Weighted(profiles.TypographyStyles));

        string homeSocks = rng.Weighted(profiles.HomeSocks) switch
        {
            PaletteSlot.Primary => palette.Primary,
            PaletteSlot.Secondary => palette.Secondary,
            _ => palette.Tertiary,
        };
        (string awayShirt, string awayShorts, string awaySocks) = KitDerivation.AwayColours(palette, palette.Primary);

        int decade = rng.Weighted(profiles.FoundingDecades);
        int foundingYear = Math.Min(decade + rng.NextInt(0, 10), LatestFoundingYear);

        var club = new ClubIdentity(
            clubId,
            displayCode,
            new ClubIdentityInfo(officialName, shortName, Nickname(profiles, primary, secondary), foundingYear),
            new ClubGeography(cityName, city.Uf, request.CountryId, city.GeoNodeId, district),
            new ClubWorldProfile(
                request.Band,
                request.ClubStrength,
                profiles.SquadSizeByBand[request.Band],
                // A generated name is built from the place, so the rule that describes it is the
                // place-name one. There is no anchor whose name it deviates from.
                NamingRule.Toponymic),
            new ClubCrest(
                rng.Weighted(profiles.ShieldShapes),
                rng.Pick(profiles.CentralCharges),
                rng.Pick(profiles.Mottos),
                Colors: []),            // derived from the palette below
            palette,
            new ClubKits(
                rng.Weighted(profiles.CollarStyles),
                rng.Weighted(profiles.FitStyles),
                new HomeKit(rng.Weighted(profiles.HomePatterns), palette.Primary, palette.Secondary, homeSocks, Luminance: 0),
                new AwayKit(rng.Weighted(profiles.AwayPatterns), awayShirt, awayShorts, awaySocks),
                DeltaE: 0,              // derived
                DeltaEThreshold: threshold,
                PolarityRule: string.Empty),
            new ClubStadium(
                StadiumName(rng, profiles),
                Capacity(rng, profiles, calibration, request),
                rng.Weighted(profiles.Atmospheres),
                rng.Weighted(profiles.PitchSurfaces)),
            new ClubAiProfile(
                rng.Weighted(profiles.TacticalStyles),
                TacticalStyleProvenance.Sampled,
                HomeAdvantageModifier: 0, // derived from the atmosphere
                DerbyRivalClubId: null),
            Audit: null);

        return WorldDerivations.Recalculate(club, calibration);
    }

    private static void Validate(
        ClubGenerationRequest request,
        ClubProfiles profiles,
        IReadOnlyList<GeoNode> geoNodes,
        WorldCalibration calibration)
    {
        if (profiles.CountryId != request.CountryId)
        {
            throw new ArgumentException(
                $"The club profiles describe {profiles.CountryId}, but the club is requested in {request.CountryId}.",
                nameof(profiles));
        }

        if (request.ClubStrength is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                $"clubStrength {request.ClubStrength} is outside (0, 1].");
        }

        if (!calibration.StadiumProfile.ContainsKey(request.CountryId))
        {
            throw new InvalidOperationException(
                $"{request.CountryId} has no stadium profile in the calibration, so a stadium cannot be sized.");
        }

        var nodes = geoNodes.ToDictionary(node => node.GeoNodeId);
        foreach (CityProfile city in profiles.Cities)
        {
            if (!nodes.TryGetValue(city.GeoNodeId, out GeoNode? node) || node.Kind != GeoNodeKind.City)
            {
                throw new InvalidOperationException(
                    $"The club profiles place clubs in {city.GeoNodeId}, which is not a City in the geo tree. "
                    + "Add the city to the tree before generating clubs there.");
            }

            if (!city.GeoNodeId.StartsWith(CityPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{city.GeoNodeId} does not follow the '{CityPrefix}<slug>' id convention the club id is built from.");
            }
        }
    }

    private static CityProfile DrawCity(IDeterministicRandom rng, ClubProfiles profiles, ClubGenerationContext taken)
    {
        var open = profiles.Cities
            .Where(city => taken.ClubsPerCity.GetValueOrDefault(city.GeoNodeId) < city.MaxClubs)
            .Select(city => (city, city.Weight))
            .ToList();

        if (open.Count == 0)
        {
            throw new InvalidOperationException(
                $"Every city in the {profiles.CountryId} club profiles already holds its maximum number of clubs. "
                + "Add cities (or raise maxClubs) in the profiles.");
        }

        return rng.Weighted(open);
    }

    /// <summary>
    /// The first free short name among: the city itself, then the district's qualifiers in a
    /// seeded order ("Operário", "Ferroviário", …), each alone, with the state and with the city.
    /// Free means no club in the world has it and no
    /// real club is known by it (<see cref="ClubProfiles.ReservedNames"/>). The official name wraps it in the club-type
    /// prefix: "Esporte Clube Recife", or "Esporte Clube Operário do Recife".
    /// </summary>
    private static (string ShortName, string OfficialName) DrawName(
        IDeterministicRandom rng,
        ClubProfiles profiles,
        ClubGenerationContext taken,
        CityProfile city,
        string cityName,
        DistrictArchetype district,
        string prefix)
    {
        var reserved = profiles.ReservedNames.Select(Letters).ToHashSet(StringComparer.Ordinal);
        bool Free(string name) => !taken.ShortNames.Contains(name) && !reserved.Contains(Letters(name));

        if (Free(cityName))
            return (cityName, $"{prefix} {cityName}");

        // A qualifier alone ("Operário"), then with the state the way Brazilian football tells
        // namesakes apart ("Operário-PR"), then with the city, which no other city can share.
        List<string> qualifiers = Shuffled(rng, profiles.Qualifiers[district]);
        IEnumerable<string> shortNames = qualifiers
            .Concat(qualifiers.Select(qualifier => $"{qualifier}-{city.Uf}"))
            .Concat(qualifiers.Select(qualifier => $"{qualifier} {cityName}"));
        var byShortName = qualifiers
            .SelectMany(qualifier => new[] { qualifier, $"{qualifier}-{city.Uf}", $"{qualifier} {cityName}" }
                .Select(shortName => (shortName, qualifier)))
            .ToDictionary(entry => entry.shortName, entry => entry.qualifier, StringComparer.Ordinal);

        foreach (string shortName in shortNames)
        {
            if (Free(shortName))
                return (shortName, $"{prefix} {byShortName[shortName]} {city.Preposition} {cityName}");
        }

        throw new InvalidOperationException(
            $"Every name for a {district} club in {cityName} is taken or reserved ({cityName}, "
            + $"{string.Join(", ", profiles.Qualifiers[district])}). Add qualifiers to the profiles.");
    }

    private static List<string> Shuffled(IDeterministicRandom rng, IReadOnlyList<string> items)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>
    /// Three capital letters no other club uses. Tried in order: the first three letters of the
    /// short name, the initials of the short name and city, then every in-order triple of their
    /// letters that starts with the first one — the same letters, so the code still reads as the
    /// club's.
    /// </summary>
    private static string DrawDisplayCode(string shortName, string cityName, ClubGenerationContext taken)
    {
        string[] words = $"{shortName} {cityName}"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Letters)
            .Where(word => word.Length > 0)
            .ToArray();
        string letters = string.Concat(words);

        var candidates = new List<string>();
        if (letters.Length >= 3)
            candidates.Add(letters[..3]);
        if (words.Length >= 3)
            candidates.Add(string.Concat(words.Take(3).Select(word => word[0])));
        for (int j = 1; j < letters.Length; j++)
        {
            for (int k = j + 1; k < letters.Length; k++)
                candidates.Add($"{letters[0]}{letters[j]}{letters[k]}");
        }

        return candidates.FirstOrDefault(code => !taken.DisplayCodes.Contains(code))
            ?? throw new InvalidOperationException(
                $"No free three-letter display code can be built from '{shortName}' / '{cityName}'.");
    }

    /// <summary>Upper-case A–Z only: accents folded away ("São" → "SAO"), everything else dropped.</summary>
    private static string Letters(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            char upper = char.ToUpperInvariant(c);
            if (upper is >= 'A' and <= 'Z')
                builder.Append(upper);
        }

        return builder.ToString();
    }

    /// <summary><c>clb_{country}_{city slug}_{NNN}</c>, the pilot league's convention
    /// (<c>clb_bra_rio_001</c>), with the lowest free number.</summary>
    private static string NextClubId(string countryId, string geoNodeId, ClubGenerationContext taken)
    {
        string stem = $"clb_{countryId.ToLowerInvariant()}_{geoNodeId[CityPrefix.Length..]}_";
        for (int n = 1; n <= 999; n++)
        {
            string id = $"{stem}{n:000}";
            if (!taken.ClubIds.Contains(id))
                return id;
        }

        throw new InvalidOperationException($"All 999 club ids under {stem} are taken.");
    }

    /// <summary>
    /// Primary, secondary and tertiary, each drawn by its own slot weight, each at least the ΔE
    /// threshold away from the ones before it — and the kits the palette produces must clear the
    /// same threshold (ClubInvariants #3). A palette that cannot is redrawn from the same stream.
    /// </summary>
    private static (ColorFamily Primary, ColorFamily Secondary, ColorFamily Tertiary) DrawPalette(
        IDeterministicRandom rng,
        ClubProfiles profiles,
        double threshold)
    {
        for (int attempt = 0; attempt < PaletteAttempts; attempt++)
        {
            ColorFamily primary = rng.Weighted(Slot(profiles, family => family.PrimaryWeight, [], threshold));

            var secondaryOptions = Slot(profiles, family => family.SecondaryWeight, [primary], threshold);
            if (secondaryOptions.Count == 0)
                continue;
            ColorFamily secondary = rng.Weighted(secondaryOptions);

            var tertiaryOptions = Slot(profiles, family => family.TertiaryWeight, [primary, secondary], threshold);
            if (tertiaryOptions.Count == 0)
                continue;
            ColorFamily tertiary = rng.Weighted(tertiaryOptions);

            var palette = new ClubPalette(primary.Hex, secondary.Hex, tertiary.Hex, TypographyStyle.ModernSans);
            (string awayShirt, _, _) = KitDerivation.AwayColours(palette, primary.Hex);
            if (ColorMath.DeltaE76(primary.Hex, awayShirt) >= threshold)
                return (primary, secondary, tertiary);
        }

        throw new InvalidOperationException(
            $"{PaletteAttempts} palettes drawn from the {profiles.CountryId} colour families and none gives a "
            + $"home and away kit at least ΔE {threshold} apart. Check the families' weights.");
    }

    /// <summary>The families a slot can take: positive weight, and at least the ΔE threshold
    /// away from every colour already chosen.</summary>
    private static List<(ColorFamily Item, double Weight)> Slot(
        ClubProfiles profiles,
        Func<ColorFamily, double> weight,
        IReadOnlyList<ColorFamily> chosen,
        double threshold) =>
        profiles.ColorFamilies
            .Where(family => weight(family) > 0)
            .Where(family => chosen.All(other => ColorMath.DeltaE76(family.Hex, other.Hex) >= threshold))
            .Select(family => (family, weight(family)))
            .ToList();

    private static string Nickname(ClubProfiles profiles, ColorFamily primary, ColorFamily secondary)
    {
        NicknameRule? pair = profiles.Nicknames.FirstOrDefault(rule =>
            rule.Families.Count == 2
            && rule.Families.Contains(primary.Id)
            && rule.Families.Contains(secondary.Id));
        if (pair is not null)
            return pair.Text;

        // The reader guarantees every family that can be a primary has one of these.
        return profiles.Nicknames.First(rule => rule.Families.Count == 1 && rule.Families[0] == primary.Id).Text;
    }

    private static string StadiumName(IDeterministicRandom rng, ClubProfiles profiles) =>
        rng.Weighted(profiles.StadiumPatterns).Replace("{place}", rng.Pick(profiles.StadiumPlaces));

    /// <summary>
    /// Drawn from the band's measured capacity distribution (CalibrationPrestigeBands) and kept
    /// inside the country's stadium profile (ClubInvariants #5), on the profile's step.
    /// </summary>
    private static int Capacity(IDeterministicRandom rng, ClubProfiles profiles, WorldCalibration calibration, ClubGenerationRequest request)
    {
        PrestigeBandCalibration band = calibration.Bands[request.Band];
        StadiumProfileEntry country = calibration.StadiumProfile[request.CountryId];
        int step = profiles.CapacityStep;

        int low = (int)Math.Ceiling(country.Min / step) * step;
        int high = (int)Math.Floor(country.Max / step) * step;
        if (low > high)
        {
            throw new InvalidOperationException(
                $"No multiple of {step} fits the {request.CountryId} stadium profile {country.Min}–{country.Max}.");
        }

        double drawn = band.CapMean + (rng.NextGaussian() * band.CapSd);
        int rounded = (int)Math.Round(drawn / step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(rounded, low, high);
    }
}
