using SoccerSim.Core.Domain;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.World.Projection;

/// <summary>The legacy rows a world projects to, ready to be written as-is.</summary>
public sealed record LegacyWorld(
    IReadOnlyList<League> Leagues,
    IReadOnlyList<Season> Seasons,
    IReadOnlyList<Team> Teams,
    IReadOnlyList<Player> Players)
{
    public override string ToString() =>
        $"{Leagues.Count} leagues, {Seasons.Count} seasons, {Teams.Count} teams, {Players.Count} players";
}

/// <summary>Raised when the world cannot be projected. Lists every reason at once, like the
/// importer does, so the data can be fixed in one pass rather than one run per problem.</summary>
public sealed class ProjectionException : Exception
{
    public ProjectionException(IReadOnlyList<string> problems)
        : base("The world cannot be projected onto the legacy schema:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem)))
        => Problems = problems;

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Turns the authored world into the legacy <c>Leagues</c>/<c>Seasons</c>/<c>Teams</c>/
/// <c>Players</c> shape that <c>MatchEngine</c> and <c>SimulationLODManager</c> already consume
/// (ADR-0002, ROADMAP.md Sprint 6). A club authored in the tool becomes playable without either
/// schema being torn down.
///
/// <para><b>Pure.</b> No database, no clock, no randomness. The same world always produces the
/// same rows, including the same ids — which is what makes the projection re-runnable rather
/// than a one-way import.</para>
///
/// <para><b>Ids.</b> Legacy keys are integers; world keys are strings. Ids are assigned by
/// ordinal position in sorted world-id order, so they are stable for a given world and
/// reproducible on another machine. They are NOT stable across a world that gains or loses a
/// club — which is exactly why <c>LegacyProjectionWriter</c> refuses to overwrite a database
/// that has already been played.</para>
/// </summary>
public static class WorldToLegacyProjection
{
    /// <summary>
    /// Elo from club strength: the midpoint of the authored range (0.3–1.2, ADR-0002) is the
    /// engine's own default of 1500, and one full point of strength is worth 1000 Elo. Over the
    /// current batch (0.66–1.00) that spreads twenty clubs across roughly 1410–1750, which is
    /// about the gap a real top division shows between its best and worst side.
    /// </summary>
    public static int EloFor(double clubStrength) =>
        Math.Clamp((int)Math.Round(1500 + ((clubStrength - 0.75) * 1000), MidpointRounding.AwayFromZero), 1000, 2200);

    /// <summary>
    /// The annual wage bill, in whole BRL. <c>Budget</c> is what the legacy economy spends
    /// against, and a club's wage bill is the only figure in the world that is actually about
    /// money it commits every year (ADR-0002).
    /// </summary>
    public static long BudgetFor(IEnumerable<CharacterRecord> squad) =>
        squad.Sum(player => (long)player.SalaryMonthlyBrl) * 12;

    /// <summary>
    /// <c>leagueTierFloat</c> is a continuous measure of how strong a competition is — in the
    /// current batch it is the mean club strength of its members (0.86 against a measured
    /// 0.8605). The legacy <c>Tier</c> is a level of DETAIL, not of quality, so the mapping says
    /// what it means: a stronger competition earns a more expensive simulation.
    /// </summary>
    public static SimulationTier TierFor(double leagueTierFloat) => leagueTierFloat switch
    {
        >= 0.80 => SimulationTier.ActiveHuman,
        >= 0.55 => SimulationTier.MajorForeign,
        _ => SimulationTier.Minor,
    };

    /// <summary>
    /// The legacy season is the calendar year the edition names. Real fixture windows differ by
    /// country and are a scheduling concern; the projection's job is to give the season a span
    /// the fixtures can fall inside, not to invent a calendar.
    /// </summary>
    public static (DateTime Start, DateTime End) SeasonSpan(int season) =>
        (new DateTime(season, 1, 1), new DateTime(season, 12, 31));

    public static LegacyWorld Project(
        IReadOnlyList<ClubIdentity> clubs,
        IReadOnlyList<CharacterRecord> characters,
        IReadOnlyList<Competition> competitions,
        IReadOnlyList<GeoNode> geoNodes)
    {
        // Domestic leagues only. A continental cup is a competition, but it is not a League in
        // the legacy model — Leagues own Seasons and fixtures, and a club plays in exactly one.
        List<Competition> domestic = competitions
            .Where(competition => competition.Scope == CompetitionScope.National)
            .OrderBy(competition => competition.CompetitionId, StringComparer.Ordinal)
            .ToList();

        var problems = new List<string>();
        Dictionary<string, Competition> leagueOfClub = MapClubsToLeagues(clubs, domestic, problems);

        if (problems.Count > 0)
            throw new ProjectionException(problems);

        Dictionary<string, int> leagueIds = domestic
            .Select((competition, index) => (competition.CompetitionId, Id: index + 1))
            .ToDictionary(entry => entry.CompetitionId, entry => entry.Id);

        Dictionary<string, string> geoNames = geoNodes.ToDictionary(node => node.GeoNodeId, node => node.DisplayName);

        var leagues = new List<League>();
        var seasons = new List<Season>();

        foreach (Competition competition in domestic)
        {
            int id = leagueIds[competition.CompetitionId];
            (DateTime start, DateTime end) = SeasonSpan(competition.Season);

            seasons.Add(new Season { Id = id, LeagueId = id, StartDate = start, EndDate = end });
            leagues.Add(new League
            {
                Id = id,
                Name = competition.Name,
                Country = geoNames.TryGetValue(competition.AnchorGeoNodeId, out string? name)
                    ? name
                    : competition.AnchorGeoNodeId,
                Tier = TierFor(competition.LeagueTierFloat),
                // One season per projection: the edition the competition names.
                CurrentSeasonId = id,
            });
        }

        ILookup<string, CharacterRecord> squads = characters.ToLookup(player => player.ClubId);

        List<ClubIdentity> ordered = clubs
            .OrderBy(club => club.ClubId, StringComparer.Ordinal)
            .ToList();

        Dictionary<string, int> teamIds = ordered
            .Select((club, index) => (club.ClubId, Id: index + 1))
            .ToDictionary(entry => entry.ClubId, entry => entry.Id);

        var teams = ordered
            .Select(club => new Team
            {
                Id = teamIds[club.ClubId],
                Name = club.Identity.ShortName,
                LeagueId = leagueIds[leagueOfClub[club.ClubId].CompetitionId],
                Budget = BudgetFor(squads[club.ClubId]),
                EloRating = EloFor(club.World.ClubStrength),
            })
            .ToList();

        // Players are numbered over the whole world, not per club: the legacy key is global, and
        // sorting by the world id keeps the numbering reproducible.
        var players = characters
            .OrderBy(player => player.PlayerId, StringComparer.Ordinal)
            .Select((player, index) => new Player
            {
                Id = index + 1,
                FirstName = player.FirstName,
                LastName = player.LastName,
                TeamId = teamIds[player.ClubId],
                BaseAttributes = AttributeMapping.Project(player),
                // Traits are a legacy catalogue with no authored counterpart. Assigning one
                // would be the projection inventing a personality the world never stated.
                Traits = [],
            })
            .ToList();

        return new LegacyWorld(leagues, seasons, teams, players);
    }

    /// <summary>
    /// Which domestic league each club plays in. A club in none has nowhere to go —
    /// <c>Teams.LeagueId</c> is NOT NULL and points at a real row — and a club in two cannot be
    /// represented at all. Both are reported rather than guessed at.
    /// </summary>
    private static Dictionary<string, Competition> MapClubsToLeagues(
        IReadOnlyList<ClubIdentity> clubs,
        IReadOnlyList<Competition> domestic,
        List<string> problems)
    {
        var byClub = new Dictionary<string, List<Competition>>();
        foreach (Competition competition in domestic)
        {
            foreach (string clubId in competition.MemberClubIds)
            {
                if (!byClub.TryGetValue(clubId, out List<Competition>? list))
                    byClub[clubId] = list = [];
                list.Add(competition);
            }
        }

        var resolved = new Dictionary<string, Competition>();

        foreach (ClubIdentity club in clubs.OrderBy(club => club.ClubId, StringComparer.Ordinal))
        {
            if (!byClub.TryGetValue(club.ClubId, out List<Competition>? memberships))
            {
                problems.Add(
                    $"{club.ClubId} ({club.Identity.ShortName}) is not a member of any national competition. "
                    + "A projected team has to belong to a league; add the club to one, or leave it out of the projection.");
                continue;
            }

            if (memberships.Count > 1)
            {
                problems.Add(
                    $"{club.ClubId} ({club.Identity.ShortName}) is a member of "
                    + $"{memberships.Count} national competitions ({string.Join(", ", memberships.Select(c => c.CompetitionId))}). "
                    + "A legacy team plays in exactly one league.");
                continue;
            }

            resolved[club.ClubId] = memberships[0];
        }

        return resolved;
    }
}
