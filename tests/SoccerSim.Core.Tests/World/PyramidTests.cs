using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The pyramid as a view over competitions with a level (ADR-0012 §5–§6): the rules that only
/// matter once a second level exists, and the editor that writes promotion and relegation in pairs
/// so a pyramid built on the screen is balanced by construction.
/// </summary>
public sealed class PyramidTests
{
    private const int Year = 2026;

    private static readonly Lazy<WorldSnapshot> Pilot = new(() => WorldJsonReader.Read(WorldFixture.Json));

    /// <summary>A level of the BRA pyramid with a full season of made-up clubs.</summary>
    private static PyramidLevel Level(
        int level,
        int clubs = 20,
        IReadOnlyList<TransitionRule>? rules = null,
        IReadOnlyList<CompetitionStage>? stages = null,
        IReadOnlyList<string>? participants = null)
    {
        string id = $"div_bra_{level}";
        return new PyramidLevel(
            new Competition(id, $"{level}ª Divisão", CompetitionScope.National, "geo_bra", "BRA", level, clubs,
                stages ?? CompetitionStages.League(2), rules ?? []),
            new CompetitionSeason($"edt_{id}_{Year}", id, Year,
                participants ?? Enumerable.Range(1, clubs).Select(n => $"clb_{level}_{n:D3}").ToList()));
    }

    /// <summary>Level <paramref name="upper"/> sends its bottom <paramref name="down"/> to the level
    /// below, which sends its top <paramref name="up"/> back.</summary>
    private static TransitionRule Down(int upper, int down, int clubs = 20) =>
        new(clubs - down + 1, clubs, $"div_bra_{upper + 1}");

    private static TransitionRule Up(int lower, int up) => new(1, up, $"div_bra_{lower - 1}");

    private static LeaguePyramid Pyramid(params PyramidLevel[] levels) => new("BRA", Year, levels);

    private static IReadOnlyList<Finding> Check(LeaguePyramid pyramid) =>
        PyramidRules.Check(pyramid, pyramid.Levels.Select(level => level.Competition).ToList());

    private static IReadOnlyList<string> Codes(LeaguePyramid pyramid) => Check(pyramid).Select(finding => finding.Code).ToList();

    /// <summary>Three levels swapping four each way.</summary>
    private static LeaguePyramid Balanced() => Pyramid(
        Level(1, rules: [Down(1, 4)]),
        Level(2, rules: [Up(2, 4), Down(2, 4)]),
        Level(3, rules: [Up(3, 4)]));

    [Fact]
    public void ABalancedPyramid_PassesEveryCheck() => Assert.Empty(Check(Balanced()));

    // --------------------------------------------------------------- the flow

    /// <summary>The balance ADR-0007 §3 names, counted from the rules: clubs arriving in a league
    /// equal clubs leaving it.</summary>
    [Fact]
    public void ALevelThatTakesMoreThanItGivesBack_ChangesSizeAndIsRefused()
    {
        // Level 1 relegates 4 but level 2 only promotes 3: the second division grows by one a year.
        LeaguePyramid pyramid = Pyramid(
            Level(1, rules: [Down(1, 4)]),
            Level(2, rules: [Up(2, 3)]));

        Finding finding = Assert.Single(Check(pyramid), f => f.Code == "PYRAMID_FLOW" && f.Detail.StartsWith("2ª Divisão"));
        Assert.Contains("recebe 4 e perde 3", finding.Detail);
    }

    [Fact]
    public void ARuleIntoNowhere_IsRefusedAsATarget()
    {
        LeaguePyramid pyramid = Pyramid(Level(1, rules: [Down(1, 4)]));

        Assert.Contains("TRANSITION_TARGET", Codes(pyramid));
    }

    [Fact]
    public void ARuleIntoItself_IsRefusedAsATarget()
    {
        LeaguePyramid pyramid = Pyramid(Level(1, rules: [new TransitionRule(17, 20, "div_bra_1")]));

        Assert.Contains("TRANSITION_TARGET", Codes(pyramid));
    }

    [Fact]
    public void RanksTheLeagueDoesNotHave_AreRefused()
    {
        LeaguePyramid pyramid = Pyramid(
            Level(1, rules: [new TransitionRule(17, 22, "div_bra_2")]),
            Level(2, rules: [Up(2, 6)]));

        Assert.Contains("TRANSITION_RANGE", Codes(pyramid));
    }

    [Fact]
    public void TwoRulesClaimingTheSameRank_AreRefused()
    {
        LeaguePyramid pyramid = Pyramid(
            Level(1, rules: [Down(1, 4)]),
            Level(2, rules: [new TransitionRule(1, 4, "div_bra_1"), new TransitionRule(3, 6, "div_bra_3")]),
            Level(3, rules: [Up(3, 4), new TransitionRule(5, 6, "div_bra_2")]));

        Finding finding = Assert.Single(Check(pyramid), f => f.Code == "TRANSITION_OVERLAP");
        Assert.Contains("3º, 4º", finding.Detail);
    }

    // -------------------------------------------------------------- the levels

    [Fact]
    public void ADuplicateLevel_IsRefused()
    {
        PyramidLevel twin = Level(1);
        LeaguePyramid pyramid = Pyramid(
            Level(1),
            twin with
            {
                Competition = twin.Competition with { CompetitionId = "div_bra_1b" },
                Season = twin.Season with { CompetitionId = "div_bra_1b", ParticipantClubIds = [] },
            });

        Assert.Contains("LEVEL_DUP", Codes(pyramid));
    }

    [Fact]
    public void AGapBetweenLevels_IsRefused()
    {
        LeaguePyramid pyramid = Pyramid(Level(1), Level(3), Level(4));

        Finding finding = Assert.Single(Check(pyramid), f => f.Code == "LEVEL_GAP");
        Assert.Contains("nível 2", finding.Detail);
    }

    [Fact]
    public void APyramidThatDoesNotStartAtOne_IsRefused()
    {
        Finding finding = Assert.Single(Check(Pyramid(Level(2))), f => f.Code == "LEVEL_GAP");
        Assert.Contains("começa em 1", finding.Detail);
    }

    [Fact]
    public void AnEmptyPyramid_IsRefused() =>
        Assert.Contains("PYRAMID_EMPTY", Codes(new LeaguePyramid("ARG", Year, [])));

    // -------------------------------------------------------------- the stages

    [Fact]
    public void ACompetitionIsPlayedInExactlyOneStage()
    {
        LeaguePyramid pyramid = Pyramid(Level(1, stages:
            [new CompetitionStage(1, StageKind.League, 1), new CompetitionStage(2, StageKind.League, 1)]));

        Assert.Contains("STAGE_COUNT", Codes(pyramid));
        Assert.Null(pyramid.Levels[0].Shape);
    }

    [Fact]
    public void AStageItsFieldCannotPlay_IsRefused()
    {
        LeaguePyramid pyramid = Pyramid(Level(1, stages: [new CompetitionStage(1, StageKind.League, 3)]));

        Finding finding = Assert.Single(Check(pyramid), f => f.Code == "STAGE_UNPLAYABLE");
        Assert.Contains("um ou dois turnos", finding.Detail);
    }

    // ---------------------------------------------------------- the participants

    [Fact]
    public void AClubInTwoLevelsOfTheSameCountry_IsAnError()
    {
        var shared = Enumerable.Range(1, 20).Select(n => $"clb_bra_{n:D3}").ToList();

        LeaguePyramid pyramid = Pyramid(
            Level(1, rules: [Down(1, 4)], participants: shared),
            Level(2, rules: [Up(2, 4)], participants: shared));

        var findings = Check(pyramid).Where(f => f.Code == "CLUB_TWO_LEAGUES").ToList();

        Assert.Equal(20, findings.Count);
        Assert.All(findings, finding => Assert.Equal(FindingLevel.Error, finding.Level));
    }

    [Fact]
    public void ASeasonSmallerThanTheDeclaredField_IsAWarning()
    {
        LeaguePyramid pyramid = Pyramid(Level(1, participants: ["clb_a", "clb_b"]));

        Finding finding = Assert.Single(Check(pyramid), f => f.Code == "SEASON_UNFILLED");
        Assert.Equal(FindingLevel.Warning, finding.Level);
        Assert.Contains("declara 20 clubes e tem 2", finding.Detail);
    }

    // -------------------------------------------------------------- the view

    [Fact]
    public void ThePyramid_IsDrawnFromTheCompetitionsWithALevel()
    {
        WorldSnapshot world = Pilot.Value;

        LeaguePyramid pyramid = LeaguePyramid.Of("BRA", Year, world.Competitions, world.Seasons);

        PyramidLevel top = Assert.Single(pyramid.Levels);
        Assert.Equal("cmp_bra_tier1", top.Competition.CompetitionId);
        Assert.Equal(1, top.Level);
        Assert.Equal(20, top.Season.ParticipantClubIds.Count);
        Assert.Empty(LeaguePyramid.Of("ARG", Year, world.Competitions, world.Seasons).Levels);
    }

    [Fact]
    public void ALevelWithoutTheYearsSeason_IsCorruptionAndThrows()
    {
        WorldSnapshot world = Pilot.Value;

        Assert.Throws<InvalidOperationException>(() => LeaguePyramid.Of("BRA", Year, world.Competitions, []));
    }

    [Fact]
    public void Moves_SayWhichWayARuleGoes()
    {
        LeaguePyramid pyramid = Balanced();

        Assert.Equal((0, 4), pyramid.Moves(pyramid.Levels[0]));
        Assert.Equal((4, 4), pyramid.Moves(pyramid.Levels[1]));
        Assert.Equal((4, 0), pyramid.Moves(pyramid.Levels[2]));
    }

    // -------------------------------------------------------------- the editor

    private static LeaguePyramid Empty() => new("BRA", Year, []);

    private static LeaguePyramid Add(LeaguePyramid pyramid, int level, int exchange, int clubs = 20, int legs = 2) =>
        PyramidEditor.AddLevel(pyramid, Pilot.Value, $"div_bra_{level}", $"{level}ª Divisão", "geo_bra", legs, clubs, exchange);

    [Fact]
    public void AddingALevel_WritesBothRulesFromOneNumber()
    {
        LeaguePyramid pyramid = Add(Add(Empty(), 1, exchange: 0), 2, exchange: 4);

        PyramidLevel top = pyramid.Find("div_bra_1")!;
        PyramidLevel second = pyramid.Find("div_bra_2")!;

        Assert.Equal([new TransitionRule(17, 20, "div_bra_2")], top.Competition.Transitions);
        Assert.Equal([new TransitionRule(1, 4, "div_bra_1")], second.Competition.Transitions);
        Assert.Equal((4, 4), PyramidEditor.Exchange(top, second));
        Assert.Equal(2, second.Level);
        Assert.Equal(("BRA", CompetitionScope.National, "geo_bra"), (second.Competition.CountryId, second.Competition.Scope, second.Competition.AnchorGeoNodeId));
        Assert.Equal(new CompetitionSeason("edt_div_bra_2_2026", "div_bra_2", Year, []), second.Season with { ParticipantClubIds = [] });
        Assert.Empty(second.Season.ParticipantClubIds);
    }

    [Fact]
    public void PairWrittenRules_KeepTheFlowBalancedAtEveryStep()
    {
        LeaguePyramid pyramid = Empty();
        int[] exchanges = [0, 4, 3, 2, 2];

        for (int level = 1; level <= exchanges.Length; level++)
        {
            pyramid = Add(pyramid, level, exchanges[level - 1]);
            Assert.DoesNotContain("PYRAMID_FLOW", Codes(pyramid));
        }

        pyramid = PyramidEditor.Rewrite(pyramid, "div_bra_2", "Série B", legs: 2, clubCount: 18, exchangeBelow: 6);
        Assert.DoesNotContain("PYRAMID_FLOW", Codes(pyramid));
        Assert.DoesNotContain("TRANSITION_RANGE", Codes(pyramid));

        pyramid = PyramidEditor.RemoveLevel(pyramid, "div_bra_4");
        Assert.DoesNotContain("PYRAMID_FLOW", Codes(pyramid));
    }

    [Fact]
    public void Rewriting_MovesTheBottomRanksWithTheField_AndReplacesThePair()
    {
        LeaguePyramid pyramid = Add(Add(Add(Empty(), 1, 0), 2, 4), 3, 3);

        pyramid = PyramidEditor.Rewrite(pyramid, "div_bra_2", "Série B", legs: 1, clubCount: 16, exchangeBelow: 2);

        PyramidLevel second = pyramid.Find("div_bra_2")!;
        Assert.Equal(16, second.Competition.ClubCount);
        Assert.Equal([new CompetitionStage(1, StageKind.League, 1)], second.Competition.Stages);
        Assert.Equal(
            [new TransitionRule(1, 4, "div_bra_1"), new TransitionRule(15, 16, "div_bra_3")],
            second.Competition.Transitions);
        Assert.Equal([new TransitionRule(1, 2, "div_bra_2")], pyramid.Find("div_bra_3")!.Competition.Transitions);
    }

    [Fact]
    public void AnExchangeTheFieldCannotHold_IsRefused()
    {
        LeaguePyramid pyramid = Add(Add(Empty(), 1, 0), 2, 4, clubs: 6);

        var ex = Assert.Throws<PyramidException>(() => Add(pyramid, 3, exchange: 3, clubs: 6));
        Assert.Contains("já troca 4", ex.Message);
    }

    [Fact]
    public void TheFirstLevel_HasNobodyToExchangeWith()
    {
        Assert.Throws<PyramidException>(() => Add(Empty(), 1, exchange: 2));
        Assert.Throws<PyramidException>(() =>
            PyramidEditor.Rewrite(Add(Empty(), 1, 0), "div_bra_1", "Série A", 2, 20, exchangeBelow: 2));
    }

    [Fact]
    public void ALevelIsAnchoredToACountryNode_ThatExists()
    {
        var notACountry = Assert.Throws<PyramidException>(() =>
            PyramidEditor.AddLevel(Empty(), Pilot.Value, "div_bra_1", "Série A", "geo_city_rio", 2, 20, 0));
        Assert.Contains("é City", notACountry.Message);

        Assert.Throws<PyramidException>(() =>
            PyramidEditor.AddLevel(Empty(), Pilot.Value, "div_bra_1", "Série A", "geo_nowhere", 2, 20, 0));
    }

    [Fact]
    public void ALevelCannotReuseACompetitionId()
    {
        var ex = Assert.Throws<PyramidException>(() =>
            PyramidEditor.AddLevel(Empty(), Pilot.Value, "cmp_bra_tier1", "Série A", "geo_bra", 2, 20, 0));
        Assert.Contains("cmp_bra_tier1", ex.Message);
    }

    [Fact]
    public void RemovingALevel_TakesTheRulesThatPointedAtIt_AndClosesTheGap()
    {
        LeaguePyramid pyramid = Add(Add(Add(Empty(), 1, 0), 2, 4), 3, 3);

        pyramid = PyramidEditor.RemoveLevel(pyramid, "div_bra_2");

        Assert.Equal([1, 2], pyramid.Levels.Select(level => level.Level));
        Assert.All(pyramid.Levels, level => Assert.Empty(level.Competition.Transitions));
        Assert.DoesNotContain(Codes(pyramid), code => code != "SEASON_UNFILLED");
    }

    [Fact]
    public void ALevelWithParticipants_CannotBeRemoved()
    {
        LeaguePyramid pyramid = PyramidEditor.Enrol(Add(Empty(), 1, 0), "div_bra_1", "clb_bra_rio_001");

        Assert.Throws<PyramidException>(() => PyramidEditor.RemoveLevel(pyramid, "div_bra_1"));
    }

    [Fact]
    public void Enrolling_MovesAClubBetweenLevels()
    {
        LeaguePyramid pyramid = Add(Add(Empty(), 1, 0), 2, 4);

        pyramid = PyramidEditor.Enrol(pyramid, "div_bra_1", "clb_x");
        pyramid = PyramidEditor.Enrol(pyramid, "div_bra_2", "clb_x");

        Assert.Empty(pyramid.Find("div_bra_1")!.Season.ParticipantClubIds);
        Assert.Equal(["clb_x"], pyramid.Find("div_bra_2")!.Season.ParticipantClubIds);

        pyramid = PyramidEditor.Withdraw(pyramid, "div_bra_2", "clb_x");
        Assert.Empty(pyramid.Find("div_bra_2")!.Season.ParticipantClubIds);
        Assert.Throws<PyramidException>(() => PyramidEditor.Withdraw(pyramid, "div_bra_2", "clb_x"));
    }

    /// <summary>
    /// The design claim the editor is built on: because a level only ever enters at the bottom and
    /// removing one closes the gap behind it, the structural rules cannot be broken from the
    /// screen at all. Every intermediate state is checked, not just the ends.
    /// </summary>
    [Fact]
    public void NoSequenceOfAddsAndRemovals_CanProduceADuplicateOrMissingLevel()
    {
        LeaguePyramid pyramid = Empty();
        var structural = new[] { "LEVEL_DUP", "LEVEL_GAP", "PYRAMID_FLOW" };

        for (int level = 1; level <= 6; level++)
        {
            pyramid = Add(pyramid, level, exchange: level == 1 ? 0 : 3);

            Assert.Equal(Enumerable.Range(1, level), pyramid.Levels.Select(l => l.Level));
            Assert.DoesNotContain(Codes(pyramid), code => structural.Contains(code));
        }

        // Out from the middle each time, which is the order that would leave a hole.
        foreach (string competitionId in new[] { "div_bra_3", "div_bra_5", "div_bra_1", "div_bra_2" })
        {
            pyramid = PyramidEditor.RemoveLevel(pyramid, competitionId);

            Assert.Equal(Enumerable.Range(1, pyramid.Levels.Count), pyramid.Levels.Select(l => l.Level).Order());
            Assert.DoesNotContain(Codes(pyramid), code => structural.Contains(code));
        }
    }
}

/// <summary>
/// Rounds and matches derive from the stage and the field size — never typed (ADR-0007 §2,
/// ADR-0012 §4). With both an even and an odd field, which is where a round-robin count goes
/// wrong quietly.
/// </summary>
public sealed class CompetitionStageTests
{
    private static CompetitionShape League(int legs, int clubs) =>
        CompetitionStages.Shape(new CompetitionStage(1, StageKind.League, legs), clubs);

    /// <summary>The pilot league is the proof: 20 clubs, two legs, 38 rounds, 380 matches — what the
    /// authored data typed and what the real Brasileirão plays.</summary>
    [Fact]
    public void ThePilotLeague_DerivesToItsOwnAuthoredNumbers()
    {
        Competition pilot = WorldJsonReader.Read(WorldFixture.Json).Competitions.Single();

        Assert.Equal(new CompetitionShape(38, 380), CompetitionStages.ShapeOf(pilot));
    }

    [Theory]
    // n even: everyone plays every round, so n−1 rounds.
    [InlineData(20, 19, 190)]
    [InlineData(4, 3, 6)]
    // n odd: someone sits out each round, so it takes n.
    [InlineData(19, 19, 171)]
    [InlineData(5, 5, 10)]
    public void OneLeg_IsARoundRobin(int clubs, int rounds, int matches) =>
        Assert.Equal(new CompetitionShape(rounds, matches), League(1, clubs));

    [Theory]
    [InlineData(20, 38, 380)]
    [InlineData(19, 38, 342)]
    [InlineData(4, 6, 12)]
    [InlineData(5, 10, 20)]
    public void TwoLegs_IsTwiceThat(int clubs, int rounds, int matches) =>
        Assert.Equal(new CompetitionShape(rounds, matches), League(2, clubs));

    [Fact]
    public void NoLeagueCanBePlayedByOneClub_OrInThreeLegs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => League(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => League(3, 20));
    }

    [Fact]
    public void EveryLeagueHasALabel()
    {
        Assert.Equal("Pontos corridos, turno único", CompetitionStages.Label(new CompetitionStage(1, StageKind.League, 1)));
        Assert.Equal("Pontos corridos, turno e returno", CompetitionStages.Label(new CompetitionStage(1, StageKind.League, 2)));
    }

    /// <summary>ADR-0012 §7: the tier float is the participants' mean strength. The pilot's twenty
    /// average 0.8605, which is the 0.86 the batch typed.</summary>
    [Fact]
    public void ThePilotsTierFloat_IsDerivedFromItsParticipants()
    {
        WorldSnapshot world = WorldJsonReader.Read(WorldFixture.Json);
        var clubs = world.Clubs.ToDictionary(club => club.ClubId);

        Assert.Equal(0.86, CompetitionStages.TierFloat(world.Seasons.Single(), clubs));
    }

    [Fact]
    public void AnEmptySeason_HasNoTierFloat()
    {
        var season = new CompetitionSeason("edt_x_2026", "x", 2026, []);

        Assert.Null(CompetitionStages.TierFloat(season, new Dictionary<string, ClubIdentity>()));
    }
}
