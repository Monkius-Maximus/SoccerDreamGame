namespace SoccerSim.Core.Simulation;

/// <summary>
/// Display-only metadata for a rendered match (team and player names + which side is home).
/// Kept separate from the engine's <see cref="TeamSnapshot"/>/<see cref="PlayerSnapshot"/> so the
/// simulation stays free of presentation data (GDD §7); loaded only for the rendered fixture.
/// </summary>
public sealed record MatchDisplayInfo(
    int HomeTeamId,
    string HomeTeamName,
    int AwayTeamId,
    string AwayTeamName,
    IReadOnlyDictionary<int, string> PlayerNames);

/// <summary>Everything the rendered match scene needs: the full simulation plus display names.</summary>
public sealed record MatchPresentation(MatchSimulation Simulation, MatchDisplayInfo Display);

/// <summary>
/// On-demand entry point for the rendered (Tier 1) match: simulates a fixture in full detail,
/// persists the result exactly once, and returns the timeline + box score + display names for the
/// match scene to play back. Pure core — all persistence flows through <see cref="IFixtureGateway"/>.
/// </summary>
public interface IMatchPresenter
{
    /// <summary>Simulate and persist a specific unplayed match, returning it for rendering.</summary>
    MatchPresentation Play(int matchId);

    /// <summary>Play the next unplayed fixture for the tier, or <c>null</c> if none is pending.</summary>
    MatchPresentation? PlayNextFixture(SimulationTier tier);
}

/// <inheritdoc cref="IMatchPresenter"/>
public sealed class MatchPresentationService : IMatchPresenter
{
    private readonly IFixtureGateway _gateway;
    private readonly MatchEngine _engine;

    public MatchPresentationService(IFixtureGateway gateway, MatchEngine engine)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public MatchPresentation Play(int matchId)
    {
        MatchContext context = _gateway.GetMatchContext(matchId)
            ?? throw new InvalidOperationException($"Match {matchId} not found.");

        // SaveResult is not idempotent (it inserts Goals rows and increments Standings), so refuse
        // to re-play a match that already has a stored result rather than double-count it.
        if (context.Match.Played)
            throw new InvalidOperationException($"Match {matchId} has already been played.");

        MatchSimulation simulation = _engine.SimulateDetailed(context);
        _gateway.SaveResult(context, simulation.Result);

        MatchDisplayInfo display = _gateway.GetMatchDisplayInfo(matchId)
            ?? throw new InvalidOperationException($"Display info for match {matchId} not found.");

        return new MatchPresentation(simulation, display);
    }

    public MatchPresentation? PlayNextFixture(SimulationTier tier)
    {
        int? matchId = _gateway.GetNextUnplayedMatchId(tier);
        return matchId is null ? null : Play(matchId.Value);
    }
}
