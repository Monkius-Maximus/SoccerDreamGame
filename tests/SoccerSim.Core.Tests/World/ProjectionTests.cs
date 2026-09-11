using SoccerSim.Core.Domain;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Projection;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Sprint 6's contract, against the real batch: the authored world becomes something
/// <c>MatchEngine</c> can play, the same way every time, and every value lands inside the legacy
/// schema's own CHECK constraints.
/// </summary>
public sealed class ProjectionTests
{
    private static readonly Lazy<WorldSnapshot> Snapshot = new(() => WorldJsonReader.Read(WorldFixture.Json));

    private static LegacyWorld Project()
    {
        WorldSnapshot world = Snapshot.Value;
        return WorldToLegacyProjection.Project(world.Clubs, world.Characters, world.Competitions, world.GeoNodes);
    }

    [Fact]
    public void Projection_IsDeterministic()
    {
        // The whole point of a projection rather than an import: it can be re-run.
        LegacyWorld first = Project();
        LegacyWorld second = Project();

        Assert.Equal(
            first.Teams.Select(team => (team.Id, team.Name, team.LeagueId, team.Budget, team.EloRating)),
            second.Teams.Select(team => (team.Id, team.Name, team.LeagueId, team.Budget, team.EloRating)));

        Assert.Equal(
            first.Players.Select(player => (player.Id, player.TeamId, player.BaseAttributes)),
            second.Players.Select(player => (player.Id, player.TeamId, player.BaseAttributes)));
    }

    [Fact]
    public void EveryClubAndCharacter_IsProjected()
    {
        WorldSnapshot world = Snapshot.Value;
        LegacyWorld legacy = Project();

        Assert.Equal(world.Clubs.Count, legacy.Teams.Count);
        Assert.Equal(world.Characters.Count, legacy.Players.Count);
        Assert.Single(legacy.Leagues);
        Assert.Equal(legacy.Leagues.Count, legacy.Seasons.Count);
    }

    [Fact]
    public void Ids_AreContiguousFromOne()
    {
        LegacyWorld legacy = Project();

        Assert.Equal(Enumerable.Range(1, legacy.Teams.Count), legacy.Teams.Select(team => team.Id));
        Assert.Equal(Enumerable.Range(1, legacy.Players.Count), legacy.Players.Select(player => player.Id));
    }

    /// <summary>A <c>MatchEngine</c> precondition, stated in ADR-0002 and checked here.</summary>
    [Fact]
    public void EveryTeam_HasAtLeastElevenPlayers()
    {
        LegacyWorld legacy = Project();
        ILookup<int?, Player> squads = legacy.Players.ToLookup(player => player.TeamId);

        foreach (Team team in legacy.Teams)
            Assert.True(squads[team.Id].Count() >= 11, $"{team.Name} would be projected with fewer than 11 players.");
    }

    [Fact]
    public void EveryPlayer_LandsInsideTheLegacyCheckConstraints()
    {
        // sql/0001: CHECK (<attribute> BETWEEN 1 AND 20) on all seven columns. A value outside
        // this is a failed insert, so the test is the constraint moved earlier.
        foreach (Player player in Project().Players)
        {
            foreach (int value in Columns(player.BaseAttributes))
                Assert.InRange(value, 1, 20);
        }
    }

    [Fact]
    public void EveryPlayer_BelongsToATeamThatExists()
    {
        LegacyWorld legacy = Project();
        var teamIds = legacy.Teams.Select(team => team.Id).ToHashSet();

        Assert.All(legacy.Players, player => Assert.Contains(player.TeamId!.Value, teamIds));
    }

    // ------------------------------------------------------------------ the scale

    [Theory]
    [InlineData(1, 1)]
    [InlineData(99, 20)]
    [InlineData(50, 11)]    // dead centre of 1–99 lands on 10.5 and rounds away from zero
    [InlineData(49, 10)]
    public void TheScale_IsEndpointExact(double world, int legacy) =>
        Assert.Equal(legacy, AttributeMapping.ToLegacyScale(world));

    [Fact]
    public void EveryColumn_WeighsExactlyOne()
    {
        // Weights that sum to anything else silently inflate or deflate the whole scale.
        foreach (IReadOnlyDictionary<Attr, double> mix in AttributeMapping.Outfield.Values)
            Assert.Equal(1.0, mix.Values.Sum(), precision: 9);

        foreach (IReadOnlyDictionary<Attr, double> mix in AttributeMapping.Goalkeeper.Values)
            Assert.Equal(1.0, mix.Values.Sum(), precision: 9);
    }

    /// <summary>
    /// The claim ADR-0002 makes about this mapping: it is not a rescale of a subset. Every one of
    /// the twelve has somewhere to go, counting the goalkeeper table.
    /// </summary>
    [Fact]
    public void EveryWorldAttribute_ReachesAtLeastOneLegacyColumn()
    {
        var used = AttributeMapping.Outfield.Values
            .Concat(AttributeMapping.Goalkeeper.Values)
            .SelectMany(mix => mix.Keys)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<Attr>().ToHashSet(), used);
    }

    /// <summary>
    /// The reason the goalkeeper table exists at all: without it a world-class keeper and a
    /// hopeless one project to the same seven numbers.
    /// </summary>
    [Fact]
    public void AKeepersReflexesAndHandling_ChangeTheProjection()
    {
        CharacterRecord keeper = Snapshot.Value.Characters.First(player => player.PrimaryPosition == Position.GK);

        PlayerAttributes before = AttributeMapping.Project(keeper);
        PlayerAttributes after = AttributeMapping.Project(keeper with
        {
            Attrs = new Dictionary<Attr, int>(keeper.Attrs) { [Attr.Reflexes] = 1, [Attr.Handling] = 1 },
        });

        Assert.True(after.Tackling < before.Tackling, "Reflexes has to reach the projected keeper.");
        Assert.True(after.Vision < before.Vision, "Handling has to reach the projected keeper.");
    }

    [Fact]
    public void AnOutfieldPlayersReflexes_ChangesNothing()
    {
        CharacterRecord outfield = Snapshot.Value.Characters.First(player => player.PrimaryPosition == Position.ST);

        Assert.Equal(
            AttributeMapping.Project(outfield),
            AttributeMapping.Project(outfield with
            {
                Attrs = new Dictionary<Attr, int>(outfield.Attrs) { [Attr.Reflexes] = 1, [Attr.Handling] = 1 },
            }));
    }

    [Fact]
    public void KeepersProjectAsPoorFinishers()
    {
        LegacyWorld legacy = Project();
        WorldSnapshot world = Snapshot.Value;

        var keeperNames = world.Characters
            .Where(player => player.PrimaryPosition == Position.GK)
            .Select(player => player.PlayerId)
            .ToHashSet();

        // The projected keepers, found the same way the projection numbers them.
        List<Player> keepers = world.Characters
            .OrderBy(player => player.PlayerId, StringComparer.Ordinal)
            .Select((player, index) => (player, legacy: legacy.Players[index]))
            .Where(pair => keeperNames.Contains(pair.player.PlayerId))
            .Select(pair => pair.legacy)
            .ToList();

        Assert.NotEmpty(keepers);
        // Nothing subtle: a keeper must not look like a striker to the scoring pool.
        Assert.All(keepers, keeper => Assert.InRange(keeper.BaseAttributes.Shooting, 1, 10));
    }

    // -------------------------------------------------------------- the club numbers

    [Fact]
    public void Elo_IsAnchoredOnTheEnginesOwnDefault()
    {
        // The midpoint of the authored 0.3–1.2 range is the engine's 1500, and a full point of
        // strength is 1000 Elo.
        Assert.Equal(1500, WorldToLegacyProjection.EloFor(0.75));
        Assert.Equal(1610, WorldToLegacyProjection.EloFor(0.86));
        Assert.Equal(1050, WorldToLegacyProjection.EloFor(0.30));
        Assert.Equal(1950, WorldToLegacyProjection.EloFor(1.20));
    }

    [Fact]
    public void Elo_IsClampedForStrengthsOutsideTheAuthoredRange()
    {
        Assert.Equal(1000, WorldToLegacyProjection.EloFor(-5));
        Assert.Equal(2200, WorldToLegacyProjection.EloFor(50));
    }

    [Fact]
    public void TheStrongestClub_ProjectsToTheHighestElo()
    {
        WorldSnapshot world = Snapshot.Value;
        LegacyWorld legacy = Project();

        ClubIdentity strongest = world.Clubs.MaxBy(club => club.World.ClubStrength)!;
        Team top = legacy.Teams.MaxBy(team => team.EloRating)!;

        Assert.Equal(strongest.Identity.ShortName, top.Name);
    }

    [Fact]
    public void Budget_IsTheAnnualWageBill()
    {
        WorldSnapshot world = Snapshot.Value;
        LegacyWorld legacy = Project();

        ClubIdentity club = world.Clubs.OrderBy(c => c.ClubId, StringComparer.Ordinal).First();
        long monthly = world.Characters
            .Where(player => player.ClubId == club.ClubId)
            .Sum(player => (long)player.SalaryMonthlyBrl);

        Assert.Equal(monthly * 12, legacy.Teams.First(team => team.Name == club.Identity.ShortName).Budget);
    }

    // ------------------------------------------------------------ league and season

    [Theory]
    [InlineData(0.86, SimulationTier.ActiveHuman)]
    [InlineData(0.80, SimulationTier.ActiveHuman)]
    [InlineData(0.79, SimulationTier.MajorForeign)]
    [InlineData(0.55, SimulationTier.MajorForeign)]
    [InlineData(0.54, SimulationTier.Minor)]
    public void Tier_FollowsTheDocumentedThresholds(double tierFloat, SimulationTier expected) =>
        Assert.Equal(expected, WorldToLegacyProjection.TierFor(tierFloat));

    [Fact]
    public void TheBatchsCompetition_ProjectsAsATierOneLeague()
    {
        League league = Project().Leagues.Single();

        // 0.86 — the mean club strength of its twenty members — earns the minute-by-minute engine.
        Assert.Equal(SimulationTier.ActiveHuman, league.Tier);
        Assert.Equal("Campeonato Nacional Brasileiro — Primeira Divisão", league.Name);
        // The country is the anchor geo node's name, not its id.
        Assert.Equal("Brasil", league.Country);
    }

    [Fact]
    public void TheSeason_IsTheEditionsCalendarYear_AndTheLeaguePointsAtIt()
    {
        LegacyWorld legacy = Project();
        Season season = legacy.Seasons.Single();
        League league = legacy.Leagues.Single();

        Assert.Equal(new DateTime(2026, 1, 1), season.StartDate);
        Assert.Equal(new DateTime(2026, 12, 31), season.EndDate);
        Assert.Equal(league.Id, season.LeagueId);
        Assert.Equal(season.Id, league.CurrentSeasonId);
    }

    // ------------------------------------------------------------------- refusals

    [Fact]
    public void AClubInNoCompetition_IsRefusedByName()
    {
        WorldSnapshot world = Snapshot.Value;
        Competition competition = world.Competitions.Single();
        string dropped = competition.MemberClubIds[0];

        var exception = Assert.Throws<ProjectionException>(() => WorldToLegacyProjection.Project(
            world.Clubs,
            world.Characters,
            [competition with { MemberClubIds = competition.MemberClubIds.Skip(1).ToList() }],
            world.GeoNodes));

        Assert.Contains(dropped, exception.Message);
        Assert.Single(exception.Problems);
    }

    [Fact]
    public void AClubInTwoNationalCompetitions_IsRefused()
    {
        WorldSnapshot world = Snapshot.Value;
        Competition competition = world.Competitions.Single();

        var exception = Assert.Throws<ProjectionException>(() => WorldToLegacyProjection.Project(
            world.Clubs,
            world.Characters,
            [competition, competition with { CompetitionId = "cmp_bra_tier1_copy" }],
            world.GeoNodes));

        // Every affected club is named, not just the first one found.
        Assert.Equal(world.Clubs.Count, exception.Problems.Count);
        Assert.Contains("cmp_bra_tier1_copy", exception.Message);
    }

    [Fact]
    public void AContinentalCompetition_IsNotALeague()
    {
        WorldSnapshot world = Snapshot.Value;
        Competition national = world.Competitions.Single();

        LegacyWorld legacy = WorldToLegacyProjection.Project(
            world.Clubs,
            world.Characters,
            [national, national with { CompetitionId = "cmp_conmebol", Scope = CompetitionScope.Continental }],
            world.GeoNodes);

        // Leagues own seasons and fixtures; a continental cup is a competition but not that.
        Assert.Single(legacy.Leagues);
    }

    private static IEnumerable<int> Columns(PlayerAttributes attributes) =>
    [
        attributes.Pace, attributes.Stamina, attributes.Strength, attributes.Passing,
        attributes.Shooting, attributes.Tackling, attributes.Vision,
    ];
}
