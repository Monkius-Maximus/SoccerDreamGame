using System.Text.Json.Nodes;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The generator's contract (ROADMAP.md Sprint 5). Determinism first — it is what the repository
/// requires of all randomness — then the shape of what comes out, and finally the safety property
/// that makes generating people acceptable at all.
/// </summary>
public sealed class SquadGeneratorTests
{
    private const long MasterSeed = 20260814;

    private static readonly WorldCalibration Calibration = WorldFixture.BuildCalibration();
    private static readonly GenerationProfiles Profiles = LoadProfiles();

    /// <summary>The real measured profiles, read the same way the importer reads them.</summary>
    private static GenerationProfiles LoadProfiles()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "gen_profiles.json");
        return GenerationProfilesReader.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// The sample club's country, with the mix measured from the real batch. A country profile is
    /// now required: the generator refuses to invent where a country's players come from.
    /// </summary>
    private static readonly CountryProfile Brazil = new(
        "BRA",
        "BRL",
        EurToLocal: 6.195,
        WageFloorMonthly: 15000,
        SquadGenerator.BrazilianMix
            .Select(entry => new NationalityShare(entry.Code, entry.Weight / SquadGenerator.BrazilianMix.Sum(e => e.Weight)))
            .ToList(),
        NationalityMixSource: null);

    private static ClubIdentity Club(double strength = 0.8, int squadSize = 34)
    {
        ClubIdentity club = WorldSamples.Club();
        return club with { World = club.World with { ClubStrength = strength, SquadSize = squadSize } };
    }

    private static IReadOnlyList<CharacterRecord> Generate(
        ClubIdentity? club = null,
        int squadSize = 34,
        Formation formation = Formation.F433,
        int target = 72,
        AgeProfile ageProfile = AgeProfile.Balanced,
        long seed = 42) =>
        SquadGenerator.Generate(
            club ?? Club(squadSize: squadSize),
            new SquadGenerationOptions(squadSize, formation, target, ageProfile, seed),
            Profiles,
            Calibration,
            MasterSeed,
            Brazil);

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void TheSameSeedProducesTheSameSquad()
    {
        // The contract the whole repository holds randomness to: reproducible, not merely
        // random-looking. Regenerating with a seed the user wrote down has to give back exactly
        // the squad they saw.
        IReadOnlyList<CharacterRecord> first = Generate(seed: 20260814);
        IReadOnlyList<CharacterRecord> second = Generate(seed: 20260814);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            // Compare field by field, with the attribute dictionary aligned: records compare
            // collections by reference.
            Assert.Equal(first[i].Attrs.OrderBy(pair => pair.Key), second[i].Attrs.OrderBy(pair => pair.Key));
            Assert.Equal(first[i].SecondaryPositions, second[i].SecondaryPositions);
            Assert.Equal(
                first[i] with { Attrs = second[i].Attrs, SecondaryPositions = second[i].SecondaryPositions },
                second[i]);
        }
    }

    [Fact]
    public void ADifferentSeedProducesADifferentSquad()
    {
        IReadOnlyList<CharacterRecord> first = Generate(seed: 1);
        IReadOnlyList<CharacterRecord> second = Generate(seed: 2);

        Assert.NotEqual(
            first.Select(player => $"{player.FirstName} {player.LastName} {player.Overall}"),
            second.Select(player => $"{player.FirstName} {player.LastName} {player.Overall}"));
    }

    [Fact]
    public void TwoClubsOnTheSameSeedGetDifferentSquads()
    {
        // Streams are keyed by club as well as seed, so regenerating one club cannot reproduce
        // another's players.
        IReadOnlyList<CharacterRecord> first = Generate(Club() with { ClubId = "clb_alpha" }, seed: 7);
        IReadOnlyList<CharacterRecord> second = Generate(Club() with { ClubId = "clb_beta" }, seed: 7);

        Assert.NotEqual(
            first.Select(player => player.LastName),
            second.Select(player => player.LastName));
    }

    // ------------------------------------------------------------------ composition

    [Theory]
    [InlineData(28)]
    [InlineData(34)]
    [InlineData(40)]
    public void TheSquadIsTheRequestedSize(int size)
    {
        Assert.Equal(size, Generate(squadSize: size).Count);
    }

    [Theory]
    [InlineData(10)]    // below the minimum
    [InlineData(99)]    // above the maximum
    public void SizeIsClampedToTheAllowedRange(int requested)
    {
        int count = Generate(squadSize: requested).Count;

        Assert.InRange(count, SquadShape.MinSquadSize, SquadShape.MaxSquadSize);
    }

    [Fact]
    public void CompositionRespectsTheMinimumsAndCaps()
    {
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 40);
        Dictionary<Position, int> counts = squad
            .GroupBy(player => player.PrimaryPosition)
            .ToDictionary(group => group.Key, group => group.Count());

        IReadOnlyDictionary<Position, int> expected = SquadShape.CompositionFor(40, Formation.F433);
        foreach ((Position position, int count) in expected)
            Assert.Equal(count, counts.GetValueOrDefault(position));
    }

    [Theory]
    [InlineData(Formation.F433)]
    [InlineData(Formation.F4231)]
    [InlineData(Formation.F442)]
    [InlineData(Formation.F352)]
    public void EveryFormationCanBeFielded(Formation formation)
    {
        // A squad that cannot field its own shape is not a squad. 3-5-2 is the test that matters:
        // it wants three centre-backs where the minimum provides three, and one full-back.
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 28, formation: formation);
        Dictionary<Position, int> counts = squad
            .GroupBy(player => player.PrimaryPosition)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach ((Position position, int needed) in SquadShape.Starters(formation))
            Assert.True(counts.GetValueOrDefault(position) >= needed, $"{formation} needs {needed} {position}");
    }

    [Fact]
    public void ElevenPlayersAreStarters()
    {
        IReadOnlyList<CharacterRecord> squad = Generate();

        Assert.Equal(11, squad.Count(player => player.SquadRole == SquadRole.Titular));
    }

    // ------------------------------------------------------------------ strength

    [Theory]
    [InlineData(60)]
    [InlineData(72)]
    [InlineData(85)]
    public void TheStartingElevenLandsNearTheTarget(int target)
    {
        IReadOnlyList<CharacterRecord> squad = Generate(target: target);

        // The target is an input to the attribute sampling, not an output that gets forced: the
        // overall comes back out of the position weights. ±3 is the tolerance the roadmap sets.
        double bestEleven = squad.OrderByDescending(player => player.Overall).Take(11).Average(player => player.Overall);

        Assert.InRange(bestEleven, target - 3, target + 3);
    }

    [Fact]
    public void StrongerClubsGetStrongerSquads()
    {
        int weak = SquadMetrics.For(Generate(Club(strength: 0.35), target: SquadShape.TargetOverallFor(0.35))).Overall;
        int strong = SquadMetrics.For(Generate(Club(strength: 1.15), target: SquadShape.TargetOverallFor(1.15))).Overall;

        Assert.True(strong > weak, $"strength 1.15 gave {strong}, strength 0.35 gave {weak}");
    }

    [Fact]
    public void AttributesFollowThePositionsShape()
    {
        // The measured profiles are what make a keeper a keeper: sampled around his own overall,
        // his reflexes sit above it and his finishing far below.
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 40);
        List<CharacterRecord> keepers = squad.Where(player => player.PrimaryPosition == Position.GK).ToList();

        Assert.NotEmpty(keepers);
        Assert.True(
            keepers.Average(k => k.Attrs[Attr.Reflexes]) > keepers.Average(k => k.Attrs[Attr.Finishing]) + 30,
            "a generated goalkeeper must be far better at reflexes than at finishing");
    }

    [Fact]
    public void EveryAttributeStaysInRange()
    {
        foreach (CharacterRecord player in Generate(squadSize: 40, target: 88))
        {
            Assert.Equal(12, player.Attrs.Count);
            Assert.All(player.Attrs.Values, value => Assert.InRange(value, 1, 99));
        }
    }

    [Fact]
    public void TheEconomyIsDerived_NotInvented()
    {
        foreach (CharacterRecord player in Generate())
        {
            Assert.Equal(player.LastName.ToUpperInvariant(), player.ShirtName);
            Assert.True(player.MarketValueEur > 0);
            Assert.True(player.SalaryMonthlyBrl > 0);
            Assert.Equal(Math.Max(player.Overall, player.Overall + player.PotentialGap), player.PotentialOverall);
        }
    }

    // ------------------------------------------------------------------ uniqueness

    [Fact]
    public void ShirtNumbersAreUniqueAndLegal()
    {
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 40);

        Assert.Equal(squad.Count, squad.Select(player => player.ShirtNumber).Distinct().Count());
        Assert.All(squad, player => Assert.InRange(player.ShirtNumber, 1, 99));
    }

    [Fact]
    public void NoTwoPlayersShareAName()
    {
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 40);

        Assert.Equal(
            squad.Count,
            squad.Select(player => $"{player.FirstName} {player.LastName}").Distinct().Count());
    }

    [Fact]
    public void PlayerIdsAreUniqueAndMarkedAsGenerated()
    {
        IReadOnlyList<CharacterRecord> squad = Generate();

        Assert.Equal(squad.Count, squad.Select(player => player.PlayerId).Distinct().Count());
        Assert.All(squad, player => Assert.StartsWith("plr_gen_", player.PlayerId));
    }

    // ------------------------------------------------------------------ ages

    [Fact]
    public void ProspectsAreYoungAndPhaseMatchesAge()
    {
        IReadOnlyList<CharacterRecord> squad = Generate(squadSize: 40);

        Assert.All(squad, player => Assert.InRange(player.Age, 17, 40));
        Assert.All(
            squad.Where(player => player.SquadRole == SquadRole.Promessa),
            player => Assert.InRange(player.Age, 17, 20));

        // Phase is derived from age, so the two can never disagree.
        Assert.All(squad, player => Assert.Equal(ExpectedPhase(player.Age), player.Phase));
    }

    private static Phase ExpectedPhase(int age) => age switch
    {
        <= 20 => Phase.Prospect,
        <= 26 => Phase.Breakthrough,
        <= 32 => Phase.Prime,
        <= 38 => Phase.Veteran,
        _ => Phase.Twilight,
    };

    [Fact]
    public void TheAgeProfileShiftsTheSquad()
    {
        double young = Generate(ageProfile: AgeProfile.Young).Average(player => player.Age);
        double balanced = Generate(ageProfile: AgeProfile.Balanced).Average(player => player.Age);
        double experienced = Generate(ageProfile: AgeProfile.Experienced).Average(player => player.Age);

        Assert.True(young < balanced, $"young {young:0.0} should be below balanced {balanced:0.0}");
        Assert.True(experienced > balanced, $"experienced {experienced:0.0} should be above balanced {balanced:0.0}");
    }

    [Fact]
    public void BirthDatesAgreeWithAgesAtTheReferenceDate()
    {
        // 2026-01-28 is the contract's reference date; a generated birthday must be consistent
        // with the age beside it, unlike parts of the imported batch.
        var reference = new DateOnly(2026, 1, 28);

        foreach (CharacterRecord player in Generate(squadSize: 40))
        {
            int age = reference.Year - player.DateOfBirth.Year;
            if (player.DateOfBirth > reference.AddYears(-age))
                age--;

            Assert.Equal(player.Age, age);
        }
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public void EveryGeneratedPlayerIsRegenWithNoAnchor()
    {
        // THIS IS A SAFETY TEST, NOT A STYLE TEST (ALGORITHMS.md §6.10). A generated player has no
        // anchor, so they assert nothing about any real person. That is the property that makes it
        // acceptable for the tool to invent people at all — if it ever fails, generation is
        // fabricating claims about somebody real.
        foreach (CharacterRecord player in Generate(squadSize: 40))
        {
            Assert.Equal(Provenance.Regen, player.Provenance);
            Assert.Null(player.Audit.AnchorPlayerName);
            Assert.Null(player.Audit.AnchorNationality);
            Assert.Null(player.Audit.DeviationFromSurname);
            Assert.Null(player.Audit.PhoneticSimilarity);
            Assert.False(player.Audit.AnchorFactsVerified);
        }
    }

    [Fact]
    public void GenerationRefusesToRunWithoutProfiles()
    {
        var empty = new GenerationProfiles(
            new Dictionary<Position, IReadOnlyDictionary<Attr, AttributeProfile>>(), [], []);

        var exception = Assert.Throws<InvalidOperationException>(() => SquadGenerator.Generate(
            Club(),
            new SquadGenerationOptions(34, Formation.F433, 72, AgeProfile.Balanced, 1),
            empty,
            Calibration,
            MasterSeed,
            Brazil));

        // The profiles are measured data. Falling back to invented defaults would produce players
        // shaped like nothing that was ever observed, and say nothing about it.
        Assert.Contains("import-profiles", exception.Message);
    }
}

/// <summary>The reader is strict for the same reason the world reader is: a profile document that
/// is subtly wrong produces players shaped like nothing real, silently.</summary>
public sealed class GenerationProfilesReaderTests
{
    private static string Json() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "gen_profiles.json"));

    [Fact]
    public void TheRealDocumentParses()
    {
        GenerationProfiles profiles = GenerationProfilesReader.Read(Json());

        Assert.Equal(8, profiles.Attributes.Count);
        Assert.All(profiles.Attributes.Values, shapes => Assert.Equal(12, shapes.Count));
        Assert.Equal(108, profiles.FirstNames.Count);
        Assert.Equal(338, profiles.LastNames.Count);

        // The keeper's shape, as measured: finishing far below his own overall, reflexes above.
        Assert.True(profiles.Attributes[Position.GK][Attr.Finishing].OffsetMean < -50);
        Assert.True(profiles.Attributes[Position.GK][Attr.Reflexes].OffsetMean > 0);
    }

    [Fact]
    public void AMissingPositionIsRejected()
    {
        var document = (JsonObject)JsonNode.Parse(Json())!;
        document["profiles"]!.AsObject().Remove("ST");

        var exception = Assert.Throws<WorldImportException>(() => GenerationProfilesReader.Read(document.ToJsonString()));

        Assert.Contains("ST", Assert.Single(exception.Errors));
    }

    [Fact]
    public void AMalformedPairIsRejected()
    {
        var document = (JsonObject)JsonNode.Parse(Json())!;
        document["profiles"]!["GK"]!["Reflexes"] = 8.7;   // a number where a pair belongs

        var exception = Assert.Throws<WorldImportException>(() => GenerationProfilesReader.Read(document.ToJsonString()));

        Assert.Contains("Reflexes", Assert.Single(exception.Errors));
    }

    [Fact]
    public void AnEmptyNamePoolIsRejected()
    {
        var document = (JsonObject)JsonNode.Parse(Json())!;
        document["lastNames"] = new JsonArray();

        var exception = Assert.Throws<WorldImportException>(() => GenerationProfilesReader.Read(document.ToJsonString()));

        Assert.Contains("lastNames", Assert.Single(exception.Errors));
    }
}
