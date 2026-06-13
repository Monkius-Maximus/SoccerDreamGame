using SoccerSim.Core.Domain;

namespace SoccerSim.Core.Simulation;

/// <summary>
/// Level-of-detail tier for a league (GDD §6). The integer value matches the
/// <c>Leagues.Tier</c> column so it maps straight to/from the database.
/// </summary>
public enum SimulationTier
{
    /// <summary>Tier 1: full event generation + minute-by-minute match sim + daily form.</summary>
    ActiveHuman = 1,

    /// <summary>Tier 2: matches resolved via Elo/tactics; form updated weekly.</summary>
    MajorForeign = 2,

    /// <summary>Tier 3: pure mathematical resolution at week's end; form ignored.</summary>
    Minor = 3,
}

/// <summary>A goal scored in a match.</summary>
public readonly record struct ScorerLine(int PlayerId, int Minute);

/// <summary>The resolved outcome of a single match, regardless of which tier produced it.</summary>
public sealed record MatchResult(
    int MatchId,
    int HomeGoals,
    int AwayGoals,
    IReadOnlyList<ScorerLine> Scorers,
    IReadOnlyDictionary<int, double> PlayerRatings);

/// <summary>Minimal team data a resolver needs — no rendering assets (GDD §7).</summary>
public sealed record TeamSnapshot(int TeamId, int Elo, IReadOnlyList<int> SquadPlayerIds);

/// <summary>Everything a resolver needs to play one match, free of persistence concerns.</summary>
public sealed record MatchContext(Match Match, TeamSnapshot Home, TeamSnapshot Away);

/// <summary>Strategy for resolving a match at a given tier (minute-sim / Elo / pure-math).</summary>
public interface ILeagueResolver
{
    SimulationTier Tier { get; }

    MatchResult Resolve(MatchContext context);

    /// <summary>Apply the tier's form policy (Tier 1 daily, Tier 2 weekly, Tier 3 no-op).</summary>
    void UpdateForm(MatchContext context, MatchResult result);
}

/// <summary>
/// Read/write port the LOD manager uses to fetch due fixtures and persist results.
/// Implemented by the infrastructure layer over repositories; the core never sees SQL.
/// </summary>
public interface IFixtureGateway
{
    SimulationTier GetTier(int leagueId);

    IReadOnlyList<MatchContext> GetDueMatches(DateTime date, SimulationTier tier);

    MatchContext? GetMatchContext(int matchId);

    void SaveResult(MatchContext context, MatchResult result);
}
