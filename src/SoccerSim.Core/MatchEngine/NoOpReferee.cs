namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// A referee that never intervenes — lets the engine run end-to-end before the real officiating
/// module (IFAB rules + empirical data) exists. It plugs in via <see cref="IReferee"/>.
/// </summary>
public sealed class NoOpReferee : IReferee
{
    public IEnumerable<MatchEvent> Evaluate(MatchStateView state) => Array.Empty<MatchEvent>();
}
