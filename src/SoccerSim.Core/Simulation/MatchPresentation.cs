using SoccerSim.Core.MatchEngine;
using SoccerSim.Core.Random;

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

/// <summary>Everything the rendered match scene needs: the engine's playback plus display names.</summary>
public sealed record MatchPresentation(MatchPlayback Playback, MatchDisplayInfo Display);

/// <summary>
/// On-demand entry point for the rendered (Tier 1) match. Runs the deterministic tick engine to
/// completion headlessly, persists the result exactly once, and returns the event stream + box
/// score + display names for the scene to replay. The rendered and headless paths run the IDENTICAL
/// engine — rendering is just an observer. Pure core: all persistence flows through
/// <see cref="IFixtureGateway"/>.
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
    private readonly IDeterministicRandom _rng;
    private readonly int? _humanTeamId;
    private readonly MatchConditions _conditions;

    /// <param name="humanTeamId">
    /// When set, <see cref="PlayNextFixture"/> picks the human club's next fixture (the same
    /// fixtures the LOD manager reserves from background resolution). Null = any fixture in the tier.
    /// </param>
    public MatchPresentationService(
        IFixtureGateway gateway,
        IDeterministicRandom rng,
        int? humanTeamId = null,
        MatchConditions? conditions = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _humanTeamId = humanTeamId;
        _conditions = conditions ?? MatchConditions.Default;
    }

    public MatchPresentation Play(int matchId)
    {
        MatchContext context = _gateway.GetMatchContext(matchId)
            ?? throw new InvalidOperationException($"Match {matchId} not found.");

        // SaveResult is not idempotent (it inserts Goals rows and increments Standings), so refuse
        // to re-play a match that already has a stored result rather than double-count it.
        if (context.Match.Played)
            throw new InvalidOperationException($"Match {matchId} has already been played.");

        var simulation = new MatchSimulation(
            context.Match.Id, context.Home, context.Away, _conditions, _rng,
            new PlaceholderPlayerBrain(), new NoOpReferee());

        MatchResult result = simulation.RunToCompletion();
        _gateway.SaveResult(context, result);

        MatchDisplayInfo display = _gateway.GetMatchDisplayInfo(matchId)
            ?? throw new InvalidOperationException($"Display info for match {matchId} not found.");

        var playback = new MatchPlayback(result, simulation.EventStream, simulation.BoxScore());
        return new MatchPresentation(playback, display);
    }

    public MatchPresentation? PlayNextFixture(SimulationTier tier)
    {
        int? matchId = _gateway.GetNextUnplayedMatchId(tier, _humanTeamId);
        return matchId is null ? null : Play(matchId.Value);
    }
}
