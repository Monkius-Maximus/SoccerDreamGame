using System.Text.Json;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Color;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The club generator's contract (ADR-0011, Sprint 10): the same request makes the same club; a
/// generated club is Regen and passes every check an authored one must; nothing it produces
/// collides with the world it is born into; and missing data is an error, never a default.
/// </summary>
public sealed class ClubGeneratorTests
{
    private const long MasterSeed = 20260814;

    /// <summary>The pilot league as the database holds it (derived fields recalculated).</summary>
    private static readonly Lazy<WorldSnapshot> Pilot =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static readonly Lazy<ClubProfiles> Brazil = new(() => ClubProfilesReader.Read(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "club_profiles.json"))));

    private static WorldSnapshot World => Pilot.Value;

    private static ClubProfiles Profiles => Brazil.Value;

    private static ClubIdentity Generate(
        long seed = 42,
        PrestigeBand band = PrestigeBand.B4,
        double strength = 0.72,
        ClubProfiles? profiles = null,
        ClubGenerationContext? taken = null) =>
        ClubGenerator.Generate(
            new ClubGenerationRequest("BRA", band, strength, seed),
            profiles ?? Profiles,
            World.GeoNodes,
            World.Calibration,
            taken ?? ClubGenerationContext.From(World.Clubs),
            MasterSeed);

    /// <summary>Records hold lists, and record equality compares lists by reference; the
    /// serialized form compares every value.</summary>
    private static string Snapshot(ClubIdentity club) => JsonSerializer.Serialize(club);

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void TheSameRequest_MakesTheSameClub()
    {
        Assert.Equal(Snapshot(Generate(seed: 7)), Snapshot(Generate(seed: 7)));
    }

    [Fact]
    public void DifferentSeeds_MakeDifferentClubs()
    {
        var clubs = Enumerable.Range(1, 10).Select(seed => Snapshot(Generate(seed: seed))).ToList();

        Assert.Equal(10, clubs.Distinct().Count());
    }

    // ------------------------------------------------------------------ what comes out

    [Fact]
    public void AGeneratedClub_IsRegen_AndCarriesNoAudit()
    {
        ClubIdentity club = Generate();

        Assert.Equal(Provenance.Regen, club.Provenance);
        Assert.Null(club.Audit);
        Assert.Equal(NamingRule.Toponymic, club.World.NamingRule);
        Assert.Equal(TacticalStyleProvenance.Sampled, club.AiProfile.TacticalStyleProvenance);
        Assert.Null(club.AiProfile.DerbyRivalClubId);
    }

    [Fact]
    public void TheRequest_DecidesCountryBandStrengthAndSquadSize()
    {
        ClubIdentity club = Generate(band: PrestigeBand.B3, strength: 0.81);

        Assert.Equal("BRA", club.Geography.CountryId);
        Assert.Equal(PrestigeBand.B3, club.World.PrestigeBand);
        Assert.Equal(0.81, club.World.ClubStrength);
        Assert.Equal(Profiles.SquadSizeByBand[PrestigeBand.B3], club.World.SquadSize);
    }

    [Fact]
    public void EveryGeneratedClub_PassesEveryClubCheck()
    {
        foreach (long seed in Enumerable.Range(1, 200))
        {
            ClubIdentity club = Generate(seed: seed);
            foreach (Finding finding in ClubInvariants.Check(club, World.Calibration))
                Assert.True(finding.Level == FindingLevel.Ok, $"seed {seed}: {finding.Code} — {finding.Detail}");
        }
    }

    [Fact]
    public void TheAwayKit_IsThePolarityInversionOfThePalette()
    {
        foreach (long seed in Enumerable.Range(1, 50))
        {
            ClubIdentity club = Generate(seed: seed);
            (string shirt, string shorts, string socks) = KitDerivation.AwayColours(club.Palette, club.Kits.Home.Shirt);

            Assert.Equal(club.Palette.Primary, club.Kits.Home.Shirt);
            Assert.Equal(club.Palette.Secondary, club.Kits.Home.Shorts);
            Assert.Equal((shirt, shorts, socks), (club.Kits.Away.Shirt, club.Kits.Away.Shorts, club.Kits.Away.Socks));
            Assert.True(ColorMath.DeltaE76(club.Kits.Home.Shirt, club.Kits.Away.Shirt) >= club.Kits.DeltaEThreshold);
        }
    }

    [Fact]
    public void TheStadium_FitsTheCountryProfile_OnTheProfilesStep()
    {
        StadiumProfileEntry profile = World.Calibration.StadiumProfile["BRA"];

        foreach (long seed in Enumerable.Range(1, 100))
        {
            int capacity = Generate(seed: seed, band: PrestigeBand.B5).Stadium.Capacity;
            Assert.InRange(capacity, (int)profile.Min, (int)profile.Max);
            Assert.Equal(0, capacity % Profiles.CapacityStep);
        }
    }

    [Fact]
    public void TheFoundingYear_ComesFromAProfileDecade_AndPrecedesTheReferenceDate()
    {
        var decades = Profiles.FoundingDecades.Where(entry => entry.Weight > 0).Select(entry => entry.Item).ToHashSet();

        foreach (long seed in Enumerable.Range(1, 100))
        {
            int year = Generate(seed: seed).Identity.FoundingYear;
            Assert.Contains(year / 10 * 10, decades);
            Assert.True(year <= 2025, $"{year} is after the reference season");
        }
    }

    [Fact]
    public void TheCity_IsAProfileCity_WithItsGeoName()
    {
        ClubIdentity club = Generate();
        CityProfile city = Assert.Single(Profiles.Cities, entry => entry.GeoNodeId == club.Geography.GeoNodeId);
        GeoNode node = Assert.Single(World.GeoNodes, entry => entry.GeoNodeId == club.Geography.GeoNodeId);

        Assert.Equal(node.DisplayName, club.Geography.CityName);
        Assert.Equal(city.Uf, club.Geography.Uf);
        Assert.Contains(club.Geography.DistrictArchetype, city.Districts.Select(entry => entry.Item));
    }

    // ------------------------------------------------------------------ collisions

    [Fact]
    public void ABatch_NeverReusesAnIdCodeOrShortName_AndRespectsCityCaps()
    {
        ClubGenerationContext taken = ClubGenerationContext.From(World.Clubs);
        var batch = new List<ClubIdentity>();

        foreach (long seed in Enumerable.Range(1, 20))
        {
            ClubIdentity club = Generate(seed: seed, taken: taken);
            batch.Add(club);
            taken = taken.With(club);
        }

        List<ClubIdentity> all = [.. World.Clubs, .. batch];
        Assert.Equal(all.Count, all.Select(club => club.ClubId).Distinct().Count());
        Assert.Equal(all.Count, all.Select(club => club.DisplayCode).Distinct().Count());
        Assert.Equal(all.Count, all.Select(club => club.Identity.ShortName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(batch, club => Assert.Matches("^[A-Z]{3}$", club.DisplayCode));
        Assert.All(batch, club => Assert.Matches("^clb_bra_[a-z]+_[0-9]{3}$", club.ClubId));

        foreach (CityProfile city in Profiles.Cities)
            Assert.True(all.Count(club => club.Geography.GeoNodeId == city.GeoNodeId) <= city.MaxClubs, city.GeoNodeId);
    }

    [Fact]
    public void ACityThatAlreadyHasAClubOfItsName_GivesTheNewClubAQualifier()
    {
        // Every pilot short name is taken, and the pilot has no club simply called "Rio de
        // Janeiro"; after one is generated, the next club there must take a qualifier.
        ClubProfiles rioOnly = Profiles with { Cities = [Profiles.Cities.Single(city => city.GeoNodeId == "geo_city_rio")] };
        ClubGenerationContext taken = ClubGenerationContext.From(World.Clubs);

        ClubIdentity first = Generate(seed: 1, profiles: rioOnly, taken: taken);
        ClubIdentity second = Generate(seed: 2, profiles: rioOnly, taken: taken.With(first));

        Assert.Equal("Rio de Janeiro", first.Identity.ShortName);
        string qualifier = Assert.Single(Profiles.Qualifiers[second.Geography.DistrictArchetype], q => second.Identity.ShortName.StartsWith(q));
        Assert.EndsWith($" {qualifier} do Rio de Janeiro", second.Identity.OfficialName);
    }

    [Fact]
    public void ARealClubsName_IsNeverTaken_EvenWhenItIsTheCitysName()
    {
        // "Santos" is a city in the geo tree and a real club; accents and case do not matter.
        ClubProfiles santosOnly = Profiles with
        {
            Cities = [Profiles.Cities.Single(city => city.GeoNodeId == "geo_city_san")],
            ReservedNames = ["SANTOS"],
        };

        ClubIdentity club = Generate(profiles: santosOnly);

        Assert.NotEqual("Santos", club.Identity.ShortName);
        Assert.Contains(Profiles.Qualifiers[club.Geography.DistrictArchetype], q => club.Identity.ShortName.StartsWith(q));
        Assert.EndsWith(" de Santos", club.Identity.OfficialName);
    }

    [Fact]
    public void NoGeneratedClub_TakesAReservedName()
    {
        var reserved = Profiles.ReservedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ClubGenerationContext taken = ClubGenerationContext.From(World.Clubs);

        // 25 is close to what the eleven profile cities still hold after the pilot's twenty.
        foreach (long seed in Enumerable.Range(1, 25))
        {
            ClubIdentity club = Generate(seed: seed, taken: taken);
            Assert.DoesNotContain(club.Identity.ShortName, reserved);
            taken = taken.With(club);
        }
    }

    [Fact]
    public void WhenAQualifierIsTaken_TheStateThenTheCityTellTheNamesakesApart()
    {
        ClubProfiles oneQualifier = Profiles with
        {
            Cities = [Profiles.Cities.Single(city => city.GeoNodeId == "geo_city_cwb") with { Districts = [(DistrictArchetype.Affluent, 1)], MaxClubs = 10 }],
            Qualifiers = Profiles.Qualifiers.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)["Recreativo"]),
        };
        ClubGenerationContext taken = ClubGenerationContext.From(World.Clubs);
        var names = new List<string>();

        foreach (long seed in Enumerable.Range(1, 4))
        {
            ClubIdentity club = Generate(seed: seed, profiles: oneQualifier, taken: taken);
            names.Add(club.Identity.ShortName);
            taken = taken.With(club);
        }

        Assert.Equal(new[] { "Curitiba", "Recreativo", "Recreativo-PR", "Recreativo Curitiba" }, names);
        Assert.Throws<InvalidOperationException>(() => Generate(seed: 5, profiles: oneQualifier, taken: taken));
    }

    // ------------------------------------------------------------------ end to end

    [Fact]
    public void AGeneratedClub_WithAGeneratedSquad_PassesTheBatchAudit()
    {
        ClubIdentity club = Generate(seed: 3);
        var brazil = new CountryProfile(
            "BRA",
            "BRL",
            EurToLocal: 6.195,
            WageFloorMonthly: 15000,
            SquadGenerator.BrazilianMix
                .Select(entry => new NationalityShare(entry.Code, entry.Weight / SquadGenerator.BrazilianMix.Sum(e => e.Weight)))
                .ToList(),
            NationalityMixSource: null);
        GenerationProfiles players = GenerationProfilesReader.Read(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "gen_profiles.json")));

        IReadOnlyList<CharacterRecord> squad = SquadGenerator.Generate(
            club, SquadGenerationOptions.For(club, seed: 3), players, World.Calibration, MasterSeed, brazil);

        WorldSnapshot grown = WorldDerivations.Recalculate(World with
        {
            Clubs = [.. World.Clubs, club],
            Characters = [.. World.Characters, .. squad],
        });

        BatchAuditReport report = BatchAudit.Run(grown);
        Assert.DoesNotContain(report.Findings, finding => finding.EntityId == club.ClubId && finding.Level == FindingLevel.Error);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void ProfilesForAnotherCountry_AreRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => ClubGenerator.Generate(
            new ClubGenerationRequest("ARG", PrestigeBand.B4, 0.7, 1),
            Profiles, World.GeoNodes, World.Calibration, ClubGenerationContext.From(World.Clubs), MasterSeed));

        Assert.Contains("BRA", ex.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void AStrengthOutsideTheScale_IsRefused(double strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(strength: strength));
    }

    [Fact]
    public void ACityTheGeoTreeDoesNotKnow_IsRefusedByName()
    {
        CityProfile ghost = Profiles.Cities[0] with { GeoNodeId = "geo_city_atlantida" };
        ClubProfiles profiles = Profiles with { Cities = [.. Profiles.Cities, ghost] };

        var ex = Assert.Throws<InvalidOperationException>(() => Generate(profiles: profiles));

        Assert.Contains("geo_city_atlantida", ex.Message);
    }

    [Fact]
    public void WhenEveryCityIsFull_GenerationStopsAndSaysSo()
    {
        ClubProfiles full = Profiles with { Cities = [.. Profiles.Cities.Select(city => city with { MaxClubs = 1 })] };

        var ex = Assert.Throws<InvalidOperationException>(() => Generate(profiles: full));

        Assert.Contains("maximum number of clubs", ex.Message);
    }

    [Fact]
    public void ACountryWithoutAStadiumProfile_IsRefused()
    {
        WorldCalibration calibration = World.Calibration with
        {
            StadiumProfile = new Dictionary<string, StadiumProfileEntry>(),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ClubGenerator.Generate(
            new ClubGenerationRequest("BRA", PrestigeBand.B4, 0.7, 1),
            Profiles, World.GeoNodes, calibration, ClubGenerationContext.From(World.Clubs), MasterSeed));

        Assert.Contains("stadium profile", ex.Message);
    }

    [Fact]
    public void ColoursThatCannotMakeTwoDistinctKits_FailInsteadOfLooping()
    {
        // Two near-identical reds: no secondary can sit ΔE 25 away from the primary.
        ClubProfiles monochrome = Profiles with
        {
            ColorFamilies =
            [
                new ColorFamily("vermelho", "#C8102E", 1, 1, 1),
                new ColorFamily("rubro", "#C8142F", 1, 1, 1),
            ],
            Nicknames = [new NicknameRule(["vermelho"], "Os Colorados"), new NicknameRule(["rubro"], "Os Rubros")],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => Generate(profiles: monochrome));

        Assert.Contains("palettes", ex.Message);
    }
}
