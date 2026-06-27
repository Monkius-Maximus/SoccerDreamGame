namespace SoccerSim.Core.Modes;

/// <summary>
/// The authority on which <see cref="GameMode"/> is active and which transitions are legal.
/// Pure and engine-agnostic, so it is unit-tested headlessly; the Godot autoload wraps it to
/// drive scene loading and time controls.
///
/// <para>
/// Fail-fast: any transition not listed in <see cref="Allowed"/> throws — there is no silent
/// "snap to a safe state" fallback. Self-transitions are deliberately absent: re-entering the
/// current mode is a caller bug, not a no-op.
/// </para>
/// </summary>
public sealed class ModeStateMachine
{
    // Adjacency of legal transitions. Loading is the hub: reachable from anywhere, and from it
    // any mode can be entered. From the calendar you can drop into a match or the life-sim;
    // a match or the life-sim hand back to the calendar (or quit to the hub) — you do not jump
    // straight from a match into the life-sim or vice versa.
    private static readonly IReadOnlyDictionary<GameMode, IReadOnlySet<GameMode>> Allowed =
        new Dictionary<GameMode, IReadOnlySet<GameMode>>
        {
            [GameMode.Loading] = new HashSet<GameMode> { GameMode.Calendar, GameMode.LifeSim, GameMode.Match },
            [GameMode.Calendar] = new HashSet<GameMode> { GameMode.Match, GameMode.LifeSim, GameMode.Loading },
            [GameMode.LifeSim] = new HashSet<GameMode> { GameMode.Calendar, GameMode.Loading },
            [GameMode.Match] = new HashSet<GameMode> { GameMode.Calendar, GameMode.Loading },
        };

    public ModeStateMachine(GameMode initial = GameMode.Loading) => CurrentMode = initial;

    public GameMode CurrentMode { get; private set; }

    /// <summary>Raised after a successful transition, with <c>(previous, next)</c>.</summary>
    public event Action<GameMode, GameMode>? ModeChanged;

    public bool CanTransitionTo(GameMode target) =>
        Allowed.TryGetValue(CurrentMode, out IReadOnlySet<GameMode>? targets) && targets.Contains(target);

    /// <summary>
    /// Moves to <paramref name="target"/>, or throws if that transition is not legal from the
    /// current mode.
    /// </summary>
    public void TransitionTo(GameMode target)
    {
        if (!CanTransitionTo(target))
        {
            string legal = Allowed.TryGetValue(CurrentMode, out IReadOnlySet<GameMode>? targets)
                ? string.Join(", ", targets)
                : "(none)";
            throw new InvalidOperationException(
                $"Illegal mode transition {CurrentMode} -> {target}. Legal targets from {CurrentMode}: {legal}.");
        }

        GameMode previous = CurrentMode;
        CurrentMode = target;
        ModeChanged?.Invoke(previous, target);
    }
}
