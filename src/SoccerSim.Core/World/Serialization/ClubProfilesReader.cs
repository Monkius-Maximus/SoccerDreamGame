using System.Text.Json.Nodes;
using SoccerSim.Core.World.Generation;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// Reads <c>club_profiles.json</c>: one country's pools and weights for generating clubs from
/// nothing (ADR-0011 §3). Same rules as the other readers: strict, all-or-nothing, and one
/// message per problem.
///
/// <para>Every section is an object carrying a <c>source</c> list — keys into the top-level
/// <c>sources</c> table — next to its data. A section without a known source is
/// rejected: "no number without a source" applies to generated clubs too.</para>
///
/// <para>Weight tables are objects keyed by value — enum names, decades, prefixes — and a value
/// the schema does not know is an error, never a default. Values left out simply have weight
/// zero.</para>
/// </summary>
public static class ClubProfilesReader
{
    public static ClubProfiles Read(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new WorldImportException(["document root: expected a JSON object"]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new WorldImportException([$"document root: not valid JSON ({ex.Message})"]);
        }

        var errors = new List<string>();
        var cursor = new JsonCursor(root, "$");
        var sectionSources = new Dictionary<string, IReadOnlyList<string>>();

        string countryId = Collect(errors, () => cursor.String("countryId"), string.Empty);
        IReadOnlyDictionary<string, string> sources = Collect(errors, () => ReadSources(cursor), new Dictionary<string, string>());

        // Reads one section: its data, and the source keys it declares.
        T Section<T>(string name, Func<JsonCursor, T> read, T empty) => Collect(errors, () =>
        {
            JsonCursor section = cursor.Object(name);
            sectionSources[name] = ReadSourceKeys(section, sources);
            return read(section);
        }, empty);

        var cities = Section("cities", section => ReadCities(section), []);
        var prefixes = Section("clubPrefixes", section => Weights(section, key => key), []);
        var qualifiers = Section("qualifiers", ReadQualifiers, new Dictionary<DistrictArchetype, IReadOnlyList<string>>());
        var reserved = Section("reservedNames", section => section.Strings("items"), []);
        var families = Section("colorFamilies", ReadColorFamilies, []);
        var nicknames = Section("nicknames", ReadNicknames, []);
        var decades = Section("foundingDecades", section => Weights(section, key => ParseDecade(section, key)), []);
        var shields = Section("shieldShapes", section => Weights(section, key => ParseEnum<ShieldShape>(section, key)), []);
        var typography = Section("typographyStyles", section => Weights(section, key => ParseEnum<TypographyStyle>(section, key)), []);
        var collars = Section("collarStyles", section => Weights(section, key => ParseEnum<CollarStyle>(section, key)), []);
        var fits = Section("fitStyles", section => Weights(section, key => ParseEnum<FitStyle>(section, key)), []);
        var homePatterns = Section("homePatterns", section => Weights(section, key => ParseEnum<FabricPattern>(section, key)), []);
        var awayPatterns = Section("awayPatterns", section => Weights(section, key => ParseEnum<FabricPattern>(section, key)), []);
        var homeSocks = Section("homeSocks", section => Weights(section, key => ParseEnum<PaletteSlot>(section, key)), []);
        var atmospheres = Section("atmospheres", section => Weights(section, key => ParseEnum<AtmosphereArchetype>(section, key)), []);
        var surfaces = Section("pitchSurfaces", section => Weights(section, key => ParseEnum<PitchSurface>(section, key)), []);
        var tactics = Section("tacticalStyles", section => Weights(section, key => ParseEnum<TacticalStyle>(section, key)), []);
        var charges = Section("centralCharges", section => section.Strings("items"), []);
        var mottos = Section("mottos", section => section.Strings("items"), []);
        var stadiumPatterns = Section("stadiumPatterns", ReadStadiumPatterns, []);
        var stadiumPlaces = Section("stadiumPlaces", section => section.Strings("items"), []);
        int capacityStep = Section("capacityStep", section => Positive(section, "value"), 0);
        var squadSizes = Section("squadSizeByBand", ReadSquadSizes, new Dictionary<PrestigeBand, int>());

        // Cross-section rules: every colour that can be a primary must have a nickname of its
        // own, so the generator never meets a palette it cannot name.
        var familyIds = families.Select(family => family.Id).ToHashSet();
        foreach (NicknameRule rule in nicknames)
        {
            foreach (string family in rule.Families.Where(family => !familyIds.Contains(family)))
                errors.Add($"$.nicknames: '{rule.Text}' names unknown colour family '{family}'");
        }

        foreach (ColorFamily family in families.Where(family => family.PrimaryWeight > 0))
        {
            if (!nicknames.Any(rule => rule.Families.Count == 1 && rule.Families[0] == family.Id))
                errors.Add($"$.nicknames: colour family '{family.Id}' can be a primary but has no single-family nickname");
        }

        foreach (CityProfile city in cities)
        {
            foreach ((DistrictArchetype district, _) in city.Districts.Where(entry => !qualifiers.ContainsKey(entry.Item)))
                errors.Add($"$.qualifiers: city {city.GeoNodeId} can hold a {district} club, which has no qualifiers");
        }

        if (errors.Count > 0)
            throw new WorldImportException(errors);

        return new ClubProfiles(
            countryId,
            sources,
            sectionSources,
            cities,
            prefixes,
            qualifiers,
            reserved,
            families,
            nicknames,
            decades,
            shields,
            typography,
            collars,
            fits,
            homePatterns,
            awayPatterns,
            homeSocks,
            atmospheres,
            surfaces,
            tactics,
            charges,
            mottos,
            stadiumPatterns,
            stadiumPlaces,
            capacityStep,
            squadSizes);
    }

    private static T Collect<T>(List<string> errors, Func<T> read, T empty)
    {
        try
        {
            return read();
        }
        catch (WorldFieldException ex)
        {
            errors.Add(ex.Message);
            return empty;
        }
    }

    private static IReadOnlyDictionary<string, string> ReadSources(JsonCursor cursor)
    {
        JsonCursor sources = cursor.Object("sources");
        var table = new Dictionary<string, string>();
        foreach ((string key, JsonCursor entry) in cursor.ObjectEntries("sources"))
            table[key] = entry.String("description");
        if (table.Count == 0)
            throw new WorldFieldException(sources.Path, "must declare at least one source");
        return table;
    }

    private static IReadOnlyList<string> ReadSourceKeys(JsonCursor section, IReadOnlyDictionary<string, string> sources)
    {
        IReadOnlyList<string> keys = section.Strings("source");
        foreach (string key in keys.Where(key => !sources.ContainsKey(key)))
            throw new WorldFieldException($"{section.Path}.source", $"'{key}' is not declared in $.sources");
        return keys;
    }

    /// <summary>A weight table (by default under <c>weights</c>). At least one weight must be
    /// positive, and none may be negative.</summary>
    private static IReadOnlyList<(T Item, double Weight)> Weights<T>(JsonCursor section, Func<string, T> parse, string name = "weights")
    {
        var table = new List<(T, double)>();
        foreach ((string key, double weight) in section.NumberEntries(name))
        {
            if (weight < 0)
                throw new WorldFieldException($"{section.Path}.{name}.{key}", "must not be negative");
            table.Add((parse(key), weight));
        }

        if (!table.Any(entry => entry.Item2 > 0))
            throw new WorldFieldException($"{section.Path}.{name}", "needs at least one positive weight");
        return table;
    }

    private static TEnum ParseEnum<TEnum>(JsonCursor section, string key) where TEnum : struct, Enum
    {
        if (Enum.TryParse(key, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new WorldFieldException($"{section.Path}.{key}",
            $"'{key}' is not a valid {typeof(TEnum).Name} (allowed: {string.Join(", ", Enum.GetNames<TEnum>())})");
    }

    private static int ParseDecade(JsonCursor section, string key)
    {
        if (int.TryParse(key, out int decade) && decade % 10 == 0 && decade is >= 1800 and <= 2020)
            return decade;
        throw new WorldFieldException($"{section.Path}.weights.{key}", "expected a decade such as 1900");
    }

    private static int Positive(JsonCursor section, string name)
    {
        int value = section.Int(name);
        if (value <= 0)
            throw new WorldFieldException($"{section.Path}.{name}", "must be positive");
        return value;
    }

    private static IReadOnlyList<CityProfile> ReadCities(JsonCursor section)
    {
        JsonArray items = section.Array("items");
        if (items.Count == 0)
            throw new WorldFieldException($"{section.Path}.items", "must not be empty");

        var cities = new List<CityProfile>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            JsonCursor city = JsonCursor.ForObject(items[i], $"{section.Path}.items[{i}]");
            double weight = city.Double("weight");
            if (weight <= 0)
                throw new WorldFieldException($"{city.Path}.weight", "must be positive");

            cities.Add(new CityProfile(
                city.String("geoNodeId"),
                city.String("uf"),
                city.String("preposition"),
                weight,
                Positive(city, "maxClubs"),
                Weights(city, key => ParseEnum<DistrictArchetype>(city, key), "districts")));
        }

        var duplicate = cities.GroupBy(city => city.GeoNodeId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new WorldFieldException($"{section.Path}.items", $"{duplicate.Key} is listed twice");
        return cities;
    }

    private static IReadOnlyDictionary<DistrictArchetype, IReadOnlyList<string>> ReadQualifiers(JsonCursor section)
    {
        var table = new Dictionary<DistrictArchetype, IReadOnlyList<string>>();
        foreach (DistrictArchetype district in Enum.GetValues<DistrictArchetype>())
        {
            JsonCursor values = section.Object("values");
            table[district] = values.Strings(district.ToString());
        }

        return table;
    }

    private static IReadOnlyList<ColorFamily> ReadColorFamilies(JsonCursor section)
    {
        JsonArray items = section.Array("items");
        var families = new List<ColorFamily>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            JsonCursor family = JsonCursor.ForObject(items[i], $"{section.Path}.items[{i}]");
            string hex = family.String("hex");
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$"))
                throw new WorldFieldException($"{family.Path}.hex", $"'{hex}' is not a colour in #RRGGBB form");

            double primary = family.Double("primaryWeight");
            double secondary = family.Double("secondaryWeight");
            double tertiary = family.Double("tertiaryWeight");
            if (primary < 0 || secondary < 0 || tertiary < 0)
                throw new WorldFieldException(family.Path, "weights must not be negative");

            families.Add(new ColorFamily(family.String("id"), hex.ToUpperInvariant(), primary, secondary, tertiary));
        }

        if (!families.Any(family => family.PrimaryWeight > 0))
            throw new WorldFieldException($"{section.Path}.items", "no family can be a primary");
        if (families.Select(family => family.Id).Distinct().Count() != families.Count)
            throw new WorldFieldException($"{section.Path}.items", "family ids must be unique");
        return families;
    }

    private static IReadOnlyList<NicknameRule> ReadNicknames(JsonCursor section)
    {
        JsonArray items = section.Array("items");
        var rules = new List<NicknameRule>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            JsonCursor rule = JsonCursor.ForObject(items[i], $"{section.Path}.items[{i}]");
            IReadOnlyList<string> families = rule.Strings("families");
            if (families.Count > 2)
                throw new WorldFieldException($"{rule.Path}.families", "a nickname matches one or two colour families");
            rules.Add(new NicknameRule(families, rule.String("text")));
        }

        return rules;
    }

    private static IReadOnlyList<(string Item, double Weight)> ReadStadiumPatterns(JsonCursor section)
    {
        var patterns = Weights(section, key => key);
        foreach ((string pattern, _) in patterns.Where(entry => !entry.Item.Contains("{place}")))
            throw new WorldFieldException($"{section.Path}.weights.{pattern}", "a stadium pattern must contain {place}");
        return patterns;
    }

    private static IReadOnlyDictionary<PrestigeBand, int> ReadSquadSizes(JsonCursor section)
    {
        var table = new Dictionary<PrestigeBand, int>();
        JsonCursor values = section.Object("values");
        foreach (PrestigeBand band in Enum.GetValues<PrestigeBand>())
        {
            int size = values.Int(band.ToString());
            // The same range the field editor enforces on world.squadSize.
            if (size is < 28 or > 40)
                throw new WorldFieldException($"{values.Path}.{band}", $"{size} is outside 28–40");
            table[band] = size;
        }

        return table;
    }
}
