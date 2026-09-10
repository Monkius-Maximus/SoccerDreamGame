namespace SoccerSim.Core.World.Generation;

/// <summary>
/// The shape of a squad before anybody is in it: how many of each position, which eleven start,
/// and what overall the club's strength implies. Separate from the generator because the panel
/// draws these same numbers (composition bars, the XI by line) before anything is written
/// (ALGORITHMS.md §6.1, §6.2, §6.4).
/// </summary>
public static class SquadShape
{
    public const int MinSquadSize = 28;
    public const int MaxSquadSize = 40;

    /// <summary>The minimum viable roster, position by position. Sums to exactly 28 — a squad
    /// smaller than this cannot field a legal XI plus cover.</summary>
    private static readonly IReadOnlyDictionary<Position, int> Minimum = new Dictionary<Position, int>
    {
        [Position.GK] = 3, [Position.CB] = 3, [Position.FB] = 4, [Position.DM] = 3,
        [Position.CM] = 4, [Position.AM] = 3, [Position.WG] = 4, [Position.ST] = 4,
    };

    /// <summary>Where the observed batch stops stacking a position.</summary>
    private static readonly IReadOnlyDictionary<Position, int> Cap = new Dictionary<Position, int>
    {
        [Position.GK] = 4, [Position.CB] = 7, [Position.FB] = 5, [Position.DM] = 5,
        [Position.CM] = 5, [Position.AM] = 4, [Position.WG] = 5, [Position.ST] = 5,
    };

    /// <summary>Who gets the players above the minimum, cycled until they are spent.</summary>
    private static readonly Position[] SurplusPriority =
    [
        Position.CB, Position.CM, Position.FB, Position.WG,
        Position.ST, Position.DM, Position.GK, Position.AM,
    ];

    private static readonly IReadOnlyDictionary<Formation, IReadOnlyDictionary<Position, int>> Shapes =
        new Dictionary<Formation, IReadOnlyDictionary<Position, int>>
        {
            [Formation.F433] = new Dictionary<Position, int>
            {
                [Position.GK] = 1, [Position.CB] = 2, [Position.FB] = 2, [Position.DM] = 1,
                [Position.CM] = 2, [Position.WG] = 2, [Position.ST] = 1,
            },
            [Formation.F4231] = new Dictionary<Position, int>
            {
                [Position.GK] = 1, [Position.CB] = 2, [Position.FB] = 2, [Position.DM] = 2,
                [Position.AM] = 1, [Position.WG] = 2, [Position.ST] = 1,
            },
            [Formation.F442] = new Dictionary<Position, int>
            {
                [Position.GK] = 1, [Position.CB] = 2, [Position.FB] = 2,
                [Position.CM] = 2, [Position.WG] = 2, [Position.ST] = 2,
            },
            [Formation.F352] = new Dictionary<Position, int>
            {
                [Position.GK] = 1, [Position.CB] = 3, [Position.FB] = 1, [Position.DM] = 1,
                [Position.CM] = 2, [Position.AM] = 1, [Position.ST] = 2,
            },
        };

    /// <summary>The label the UI and the source data use ("4-2-3-1").</summary>
    public static string Label(Formation formation) => formation switch
    {
        Formation.F433 => "4-3-3",
        Formation.F4231 => "4-2-3-1",
        Formation.F442 => "4-4-2",
        Formation.F352 => "3-5-2",
        _ => throw new ArgumentOutOfRangeException(nameof(formation)),
    };

    public static IReadOnlyDictionary<Position, int> Starters(Formation formation) => Shapes[formation];

    /// <summary>A club plays what its tactical style implies unless the user says otherwise.</summary>
    public static Formation DefaultFormationFor(TacticalStyle style) => style switch
    {
        TacticalStyle.Possession => Formation.F433,
        TacticalStyle.Counter => Formation.F442,
        TacticalStyle.HighPress => Formation.F4231,
        TacticalStyle.LowBlock => Formation.F442,
        TacticalStyle.Controlled => Formation.F4231,
        TacticalStyle.Direct => Formation.F442,
        _ => Formation.F433,
    };

    /// <summary>
    /// The overall a club of this strength fields, from a least-squares fit over the twenty real
    /// squads: <c>XI ≈ 39.6 + 41.35 × clubStrength</c>. It is the value the panel pre-fills; the
    /// user can override it.
    /// </summary>
    public static int TargetOverallFor(double clubStrength) =>
        Math.Clamp((int)Math.Round(39.6 + 41.35 * clubStrength, MidpointRounding.AwayFromZero), 52, 88);

    /// <summary>Which of the four drawn lines a position belongs to: keeper, defence, midfield,
    /// attack. Used by the XI preview, not by the simulation.</summary>
    public static int LineOf(Position position) => position switch
    {
        Position.GK => 0,
        Position.CB or Position.FB => 1,
        Position.DM or Position.CM => 2,
        _ => 3,
    };

    /// <summary>
    /// How many of each position a squad of this size holds: the minimum, then the surplus handed
    /// out round-robin by priority while respecting each cap.
    /// </summary>
    public static IReadOnlyDictionary<Position, int> Composition(int squadSize)
    {
        var counts = new Dictionary<Position, int>(Minimum);
        int remaining = Math.Clamp(squadSize, MinSquadSize, MaxSquadSize) - MinSquadSize;

        int index = 0;
        // The cycle bound is a guard, not a rule: every position could be at its cap while
        // players remain, and an unbounded loop would spin.
        while (remaining > 0 && index < SurplusPriority.Length * MaxSquadSize)
        {
            Position position = SurplusPriority[index % SurplusPriority.Length];
            if (counts[position] < Cap[position])
            {
                counts[position]++;
                remaining--;
            }
            index++;
        }

        return counts;
    }

    /// <summary>
    /// The composition, widened where the chosen formation needs more of a position than the
    /// minimum provides (3-5-2 wants three centre-backs). The formation always wins: a squad that
    /// cannot field its own shape is not a squad.
    /// </summary>
    public static IReadOnlyDictionary<Position, int> CompositionFor(int squadSize, Formation formation)
    {
        var counts = new Dictionary<Position, int>(Composition(squadSize));
        foreach ((Position position, int needed) in Starters(formation))
        {
            if (counts[position] < needed)
                counts[position] = needed;
        }

        return counts;
    }
}
