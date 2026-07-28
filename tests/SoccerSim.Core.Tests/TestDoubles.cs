using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Counts up instead of drawing, so ids stay deterministic and readable in failure output while
/// remaining distinct. Shared by the doubles below, which script <see cref="IRandom.NextDouble"/>
/// and have no bit source of their own to derive an id from.
/// </summary>
internal sealed class SequentialGuidSource
{
    private int _next;

    public Guid Next() => new(++_next, 0, 0, new byte[8]);
}

/// <summary>Deterministic RNG that replays a scripted sequence, then never fires (returns 1.0).</summary>
internal sealed class ScriptedRandom : IRandom
{
    private readonly Queue<double> _doubles;
    private readonly SequentialGuidSource _guids = new();

    public ScriptedRandom(params double[] doubles) => _doubles = new Queue<double>(doubles);

    public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 1.0;

    public int Next(int maxExclusive) => 0;

    public Guid NextGuid() => _guids.Next();
}

/// <summary>RNG that always returns the same value — handy for crossing a probability threshold.</summary>
internal sealed class StubRandom : IRandom
{
    private readonly double _value;
    private readonly SequentialGuidSource _guids = new();

    public StubRandom(double value) => _value = value;

    public double NextDouble() => _value;

    public int Next(int maxExclusive) => 0;

    public Guid NextGuid() => _guids.Next();
}

/// <summary>The stream the tests hand to <see cref="Core.Time.TimeManager"/> for its resume tokens.</summary>
internal static class TestStreams
{
    public static RandomStream LifeEvents() => RandomStream.Create(0x7E57_5EEDUL, StreamName.LifeEvents);
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
