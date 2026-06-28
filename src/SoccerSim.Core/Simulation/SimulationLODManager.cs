namespace SoccerSim.Core.Simulation;

/// <inheritdoc cref="ISimulationLODManager"/>
public sealed class SimulationLODManager : ISimulationLODManager
{
    private readonly IFixtureGateway _gateway;
    private readonly IReadOnlyDictionary<SimulationTier, ILeagueResolver> _resolvers;
    private readonly int? _humanTeamId;

    /// <param name="humanTeamId">
    /// When set, fixtures involving this team are reserved for the rendered match scene and are
    /// skipped by background resolution (so the player can still watch them). Null resolves everything.
    /// </param>
    public SimulationLODManager(
        IFixtureGateway gateway,
        IEnumerable<ILeagueResolver> resolvers,
        int? humanTeamId = null)
    {
        _gateway = gateway;
        _resolvers = resolvers.ToDictionary(r => r.Tier);
        _humanTeamId = humanTeamId;
    }

    public SimulationTier GetTier(int leagueId) => _gateway.GetTier(leagueId);

    public void OnDayElapsed(DateTime date)
    {
        // Tier 1 (active human league) resolves daily. The player's own fixture is
        // handed to the rendered match engine elsewhere; the rest are simulated here.
        ResolveDueMatches(date, SimulationTier.ActiveHuman);
    }

    public void OnWeekElapsed(DateTime weekEnd)
    {
        // Tier 2 (major foreign): Elo aggregate + weekly form. Tier 3 (minor): pure math.
        ResolveDueMatches(weekEnd, SimulationTier.MajorForeign);
        ResolveDueMatches(weekEnd, SimulationTier.Minor);
    }

    public MatchResult ResolveMatch(int matchId, SimulationTier tier)
    {
        MatchContext context = _gateway.GetMatchContext(matchId)
            ?? throw new InvalidOperationException($"Match {matchId} not found.");
        return ResolveAndPersist(context, ResolverFor(tier));
    }

    private void ResolveDueMatches(DateTime date, SimulationTier tier)
    {
        if (!_resolvers.TryGetValue(tier, out ILeagueResolver? resolver))
            return;

        foreach (MatchContext context in _gateway.GetDueMatches(date, tier))
        {
            if (IsReservedForHuman(context))
                continue;   // the player watches this one via the rendered match scene

            ResolveAndPersist(context, resolver);
        }
    }

    private bool IsReservedForHuman(MatchContext context) =>
        _humanTeamId is int teamId
        && (context.Match.HomeTeamId == teamId || context.Match.AwayTeamId == teamId);

    private MatchResult ResolveAndPersist(MatchContext context, ILeagueResolver resolver)
    {
        MatchResult result = resolver.Resolve(context);
        resolver.UpdateForm(context, result);
        _gateway.SaveResult(context, result);
        return result;
    }

    private ILeagueResolver ResolverFor(SimulationTier tier)
        => _resolvers.TryGetValue(tier, out ILeagueResolver? resolver)
            ? resolver
            : throw new InvalidOperationException($"No resolver registered for tier {tier}.");
}
