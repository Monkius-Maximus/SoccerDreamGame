namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Decides one player's <see cref="Intention"/> for a tick from its <see cref="PlayerPerception"/>.
/// This is the seam the real steering + utility brain (a later module) plugs into without the
/// engine changing.
/// </summary>
public interface IPlayerBrain
{
    Intention Decide(int tick, PlayerPerception perception);
}
