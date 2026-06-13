using SoccerSim.Core.Events;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.Tests;

/// <summary>Deterministic RNG that replays a scripted sequence, then never fires (returns 1.0).</summary>
internal sealed class ScriptedRandom : IRandom
{
    private readonly Queue<double> _doubles;

    public ScriptedRandom(params double[] doubles) => _doubles = new Queue<double>(doubles);

    public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 1.0;

    public int Next(int maxExclusive) => 0;
}

/// <summary>RNG that always returns the same value — handy for crossing a probability threshold.</summary>
internal sealed class StubRandom : IRandom
{
    private readonly double _value;

    public StubRandom(double value) => _value = value;

    public double NextDouble() => _value;

    public int Next(int maxExclusive) => 0;
}

/// <summary>No-op LOD manager that just counts the day/week callbacks for assertions.</summary>
internal sealed class NullLodManager : ISimulationLODManager
{
    public int DayCount { get; private set; }

    public int WeekCount { get; private set; }

    public SimulationTier GetTier(int leagueId) => SimulationTier.ActiveHuman;

    public void OnDayElapsed(DateTime date) => DayCount++;

    public void OnWeekElapsed(DateTime weekEnd) => WeekCount++;

    public MatchResult ResolveMatch(int matchId, SimulationTier tier) =>
        new(matchId, 0, 0, Array.Empty<ScorerLine>(), new Dictionary<int, double>());
}
