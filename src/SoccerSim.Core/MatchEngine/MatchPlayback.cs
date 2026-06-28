using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.MatchEngine;

/// <summary>Box-score totals for a simulated match. Possession values sum to 100.</summary>
public sealed record MatchBoxScore(
    int HomePossession,
    int AwayPossession,
    int HomeShots,
    int AwayShots,
    int HomeShotsOnTarget,
    int AwayShotsOnTarget);

/// <summary>
/// Everything the rendered scene needs to replay a tick-engine match without re-running it: the
/// persisted <see cref="MatchResult"/>, the full event stream, and the box score.
/// </summary>
public sealed record MatchPlayback(
    MatchResult Result,
    IReadOnlyList<MatchEvent> Events,
    MatchBoxScore Stats);
