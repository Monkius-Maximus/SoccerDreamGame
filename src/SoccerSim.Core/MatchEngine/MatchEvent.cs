namespace SoccerSim.Core.MatchEngine;

/// <summary>How play restarts after the ball has left the field of play.</summary>
public enum RestartType
{
    ThrowIn,
    Corner,
    GoalKick,
}

/// <summary>
/// A timestamped match event. GEOMETRIC events (kick-off, half/full time, goal, ball out of play)
/// are computed deterministically by <see cref="MatchSimulation"/> using the shared
/// last-touch state. JUDGEMENT events (foul, card, penalty, offside, substitution) are NOT decided
/// by the engine — they come from the <see cref="IReferee"/>, which only reads state.
/// </summary>
public abstract record MatchEvent(int Tick, int Minute)
{
    public sealed record KickOff(int Tick, int Minute) : MatchEvent(Tick, Minute);

    public sealed record HalfTime(int Tick, int Minute) : MatchEvent(Tick, Minute);

    public sealed record FullTime(int Tick, int Minute) : MatchEvent(Tick, Minute);

    public sealed record GoalScored(int Tick, int Minute, int TeamId, int? ScorerPlayerId) : MatchEvent(Tick, Minute);

    public sealed record BallOutOfPlay(int Tick, int Minute, RestartType Restart, int RestartTeamId) : MatchEvent(Tick, Minute);

    public sealed record Foul(int Tick, int Minute, int OffenderPlayerId, int VictimPlayerId) : MatchEvent(Tick, Minute);

    public sealed record Card(int Tick, int Minute, int PlayerId, bool IsRed) : MatchEvent(Tick, Minute);

    public sealed record PenaltyAwarded(int Tick, int Minute, int TeamId) : MatchEvent(Tick, Minute);

    public sealed record Offside(int Tick, int Minute, int PlayerId) : MatchEvent(Tick, Minute);

    public sealed record SubstitutionRequested(int Tick, int Minute, int TeamId, int OutPlayerId, int InPlayerId)
        : MatchEvent(Tick, Minute);
}
