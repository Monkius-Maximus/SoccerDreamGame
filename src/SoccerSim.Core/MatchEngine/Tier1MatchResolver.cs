using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Tier 1 resolution: runs the full deterministic tick engine headlessly (<see cref="MatchSimulation.RunToCompletion"/>)
/// and returns its <see cref="MatchResult"/> — identical math whether or not a scene renders it. Uses
/// the placeholder brain + no-op referee for now; the real brain/referee plug into the engine without
/// touching this resolver. Conditions default to calm/dry until the world supplies per-fixture ones.
/// </summary>
public sealed class Tier1MatchResolver : ILeagueResolver
{
    private readonly IDeterministicRandom _rng;
    private readonly MatchConditions _conditions;

    public Tier1MatchResolver(IDeterministicRandom rng, MatchConditions? conditions = null)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _conditions = conditions ?? MatchConditions.Default;
    }

    public SimulationTier Tier => SimulationTier.ActiveHuman;

    public MatchResult Resolve(MatchContext context)
    {
        var simulation = new MatchSimulation(
            context.Match.Id,
            context.Home,
            context.Away,
            _conditions,
            _rng,
            new PlaceholderPlayerBrain(),
            new NoOpReferee());

        return simulation.RunToCompletion();
    }

    public void UpdateForm(MatchContext context, MatchResult result)
    {
        // Tier 1 tracks form daily; the gateway persists it from result.PlayerRatings.
    }
}
