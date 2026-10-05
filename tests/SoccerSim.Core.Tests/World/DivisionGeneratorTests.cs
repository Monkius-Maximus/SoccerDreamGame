using System.Text.Json;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The division generator's contract (ADR-0011 §6, Sprint 11): the same request makes the same
/// batch; the batch fills exactly the seats asked for, as a ladder of strengths; nothing in it
/// collides with the world or with itself; every club and squad passes the checks an authored one
/// must; and a request the pyramid cannot hold is refused before anything is drawn.
/// </summary>
public sealed class DivisionGeneratorTests
{
    private const long MasterSeed = 20260814;
    private const string SerieA = "cmp_bra_tier1";
    private const string SerieB = "bra_t2";

    private static readonly Lazy<WorldSnapshot> Pilot =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static readonly Lazy<ClubProfiles> Clubs = new(() => ClubProfilesReader.Read(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "club_profiles.json"))));

    private static readonly Lazy<GenerationProfiles> Players = new(() => GenerationProfilesReader.Read(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "gen_profiles.json"))));

    private static readonly CountryProfile Brazil = new(
        "BRA",
        "BRL",
        EurToLocal: 6.195,
        WageFloorMonthly: 15000,
        SquadGenerator.BrazilianMix
            .Select(entry => new NationalityShare(entry.Code, entry.Weight / SquadGenerator.BrazilianMix.Sum(e => e.Weight)))
            .ToList(),
        NationalityMixSource: null);

    private static WorldSnapshot World => Pilot.Value;

    /// <summary>The pilot league at level 1 with its twenty clubs, and an empty twenty-seat Série B
    /// below it exchanging four.</summary>
    private static LeaguePyramid Pyramid() =>
        PyramidEditor.AddLevel(
            LeaguePyramid.Of("BRA", World.Meta.CurrentSeason, World.Competitions, World.Seasons),
            World, SerieB, "Série B", "geo_bra", legs: 2, clubCount: 20, exchange: 4);

    private static DivisionGenerationRequest Request(
        int clubCount = 20,
        string competitionId = SerieB,
        double min = 0.62,
        double max = 0.80,
        long seed = 2026) =>
        new("BRA", competitionId, clubCount, PrestigeBand.B4, min, max, seed);

    private static DivisionGenerationResult Generate(DivisionGenerationRequest? request = null, LeaguePyramid? pyramid = null) =>
        DivisionGenerator.Generate(
            request ?? Request(),
            pyramid ?? Pyramid(),
            Clubs.Value,
            Players.Value,
            Brazil,
            World.GeoNodes,
            World.Calibration,
            World.Clubs,
            MasterSeed);

    private static readonly Lazy<DivisionGenerationResult> SerieBBatch = new(() => Generate());

    private static DivisionGenerationResult Batch => SerieBBatch.Value;

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void TheSameRequest_MakesTheSameDivision()
    {
        DivisionGenerationResult again = Generate();

        Assert.Equal(JsonSerializer.Serialize(Batch.Clubs), JsonSerializer.Serialize(again.Clubs));
        Assert.Equal(JsonSerializer.Serialize(Batch.Characters), JsonSerializer.Serialize(again.Characters));
    }

    [Fact]
    public void AnotherSeed_MakesAnotherDivision()
    {
        DivisionGenerationResult other = Generate(Request(seed: 2027));

        Assert.NotEqual(
            Batch.Clubs.Select(club => club.Identity.OfficialName),
            other.Clubs.Select(club => club.Identity.OfficialName));
    }

    // ------------------------------------------------------------------ what comes out

    [Fact]
    public void TheBatch_FillsTheRequestedSeats_AndOnlyThose()
    {
        PyramidLevel serieB = Batch.Pyramid.Find(SerieB)!;
        PyramidLevel serieA = Batch.Pyramid.Find(SerieA)!;

        Assert.Equal(20, Batch.Clubs.Count);
        Assert.Equal(Batch.Clubs.Select(club => club.ClubId), serieB.Season.ParticipantClubIds);
        Assert.Equal(World.Seasons.Single().ParticipantClubIds, serieA.Season.ParticipantClubIds);
    }

    [Fact]
    public void EveryClub_HasItsWholeSquad_AndNoOneElses()
    {
        Assert.All(Batch.Clubs, club =>
            Assert.Equal(club.World.SquadSize, Batch.Characters.Count(player => player.ClubId == club.ClubId)));
        Assert.Equal(Batch.Clubs.Sum(club => club.World.SquadSize), Batch.Characters.Count);
    }

    [Fact]
    public void TheStrengths_AreALadderFromMaxToMin()
    {
        Assert.Equal(
            DivisionGenerator.Strengths(Request()),
            Batch.Clubs.Select(club => club.World.ClubStrength));
        Assert.Equal(0.80, Batch.Clubs[0].World.ClubStrength);
        Assert.Equal(0.62, Batch.Clubs[^1].World.ClubStrength);
        Assert.All(Batch.Clubs, club => Assert.Equal(PrestigeBand.B4, club.World.PrestigeBand));
    }

    [Fact]
    public void ASingleClub_IsDrawnAtTheMiddleOfTheRange()
    {
        Assert.Equal(new[] { 0.71 }, DivisionGenerator.Strengths(Request(clubCount: 1)));
    }

    [Fact]
    public void NothingInTheBatch_CollidesWithTheWorldOrItself()
    {
        List<ClubIdentity> clubs = [.. World.Clubs, .. Batch.Clubs];
        List<CharacterRecord> players = [.. World.Characters, .. Batch.Characters];

        Assert.Equal(clubs.Count, clubs.Select(club => club.ClubId).Distinct().Count());
        Assert.Equal(clubs.Count, clubs.Select(club => club.DisplayCode).Distinct().Count());
        Assert.Equal(clubs.Count, clubs.Select(club => club.Identity.ShortName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(players.Count, players.Select(player => player.PlayerId).Distinct().Count());
    }

    [Fact]
    public void EveryClub_PassesEveryClubCheck()
    {
        Assert.All(Batch.Clubs, club =>
            Assert.All(ClubInvariants.Check(club, World.Calibration), finding =>
                Assert.True(finding.Level == FindingLevel.Ok, $"{club.ClubId}: {finding.Code} — {finding.Detail}")));
    }

    [Fact]
    public void TheGrownWorld_PassesTheBatchAudit_ForEveryNewClub()
    {
        WorldSnapshot grown = WorldDerivations.Recalculate(World with
        {
            Clubs = [.. World.Clubs, .. Batch.Clubs],
            Characters = [.. World.Characters, .. Batch.Characters],
        });

        var newIds = Batch.Clubs.Select(club => club.ClubId).ToHashSet();
        BatchAuditReport report = BatchAudit.Run(grown);

        Assert.DoesNotContain(report.Findings, finding => newIds.Contains(finding.EntityId) && finding.Level == FindingLevel.Error);
    }

    [Fact]
    public void ThePyramid_HasNoClubInTwoLeagues()
    {
        IReadOnlyList<Competition> competitions = [.. Batch.Pyramid.Levels.Select(level => level.Competition)];

        Assert.DoesNotContain(PyramidRules.Check(Batch.Pyramid, competitions), finding => finding.Code == "CLUB_TWO_LEAGUES");
    }

    [Fact]
    public void ClubsInALongNamedCity_GetDistinctPlayerIds()
    {
        // Two clubs in São Gonçalo used to share the truncated slug "brasaogoncal" and therefore
        // the same player ids. One city, two seats, nothing else open.
        ClubProfiles oneCity = Clubs.Value with
        {
            Cities = [Clubs.Value.Cities.Single(city => city.GeoNodeId == "geo_city_saogoncalo")],
        };

        DivisionGenerationResult two = DivisionGenerator.Generate(
            Request(clubCount: 2), Pyramid(), oneCity, Players.Value, Brazil,
            World.GeoNodes, World.Calibration, World.Clubs, MasterSeed);

        Assert.All(two.Clubs, club => Assert.Equal("geo_city_saogoncalo", club.Geography.GeoNodeId));
        Assert.Equal(two.Characters.Count, two.Characters.Select(player => player.PlayerId).Distinct().Count());
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void ADivisionThePyramidDoesNotHave_IsRefusedByName()
    {
        var ex = Assert.Throws<ArgumentException>(() => Generate(Request(competitionId: "bra_t9")));

        Assert.Contains("bra_t9", ex.Message);
    }

    [Fact]
    public void MoreClubsThanFreeSeats_IsRefused()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Generate(Request(clubCount: 21)));

        Assert.Contains("20 free seat(s)", ex.Message);
    }

    [Fact]
    public void AFullDivision_TakesNoMoreClubs()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Generate(Request(competitionId: SerieA, clubCount: 1)));

        Assert.Contains("0 free seat(s)", ex.Message);
    }

    [Theory]
    [InlineData(0.0, 0.7)]
    [InlineData(0.6, 1.1)]
    [InlineData(0.8, 0.6)]
    public void AStrengthRangeOutsideTheScale_IsRefused(double min, double max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate(Request(min: min, max: max)));
    }

    [Fact]
    public void AnotherCountrysPyramid_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => Generate(pyramid: new LeaguePyramid("ARG", 2026, [])));
    }
}
