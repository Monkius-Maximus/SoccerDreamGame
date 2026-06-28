namespace SoccerSim.Core.Modes;

/// <summary>
/// The mutually-exclusive top-level modes of the game. Exactly one is active at a time; the
/// <see cref="ModeStateMachine"/> owns the legal transitions between them.
/// </summary>
public enum GameMode
{
    /// <summary>The hub/menu state while no simulation runs (also the destination when a scene loads).</summary>
    Loading,

    /// <summary>The calendar / background simulation: LOD match resolution + life-sim event rolls.</summary>
    Calendar,

    /// <summary>The off-pitch life simulation.</summary>
    LifeSim,

    /// <summary>A single rendered match.</summary>
    Match,
}
