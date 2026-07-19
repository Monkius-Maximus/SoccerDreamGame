using SoccerSim.Core.Pitch;

namespace SoccerSim.Core.Tactics;

/// <summary>
/// A named set of eleven positional slots. Fail-fast: an incomplete formation (wrong slot
/// count, missing or duplicated goalkeeper) throws at construction — there is no partial mode.
/// </summary>
public sealed class Formation
{
    public const int SquadSize = 11;

    public Formation(string name, IReadOnlyList<FormationSlot> slots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != SquadSize)
            throw new ArgumentException($"A formation needs exactly {SquadSize} slots; got {slots.Count}.", nameof(slots));

        int goalkeepers = slots.Count(s => s.Role == PlayerRole.Goalkeeper);
        if (goalkeepers != 1)
            throw new ArgumentException($"A formation needs exactly one goalkeeper slot; got {goalkeepers}.", nameof(slots));

        Name = name;
        Slots = slots.ToArray();
    }

    public string Name { get; }

    /// <summary>Slot order is the squad order: players are assigned to slots by index.</summary>
    public IReadOnlyList<FormationSlot> Slots { get; }

    /// <summary>
    /// The single MVP formation: a flat 4-4-2. Additional formations are pure data — add
    /// factories here once the tactics screen needs them.
    /// </summary>
    public static Formation FourFourTwo() => new("4-4-2", new[]
    {
        new FormationSlot(PlayerRole.Goalkeeper, Duty.Defend, new Vec2(0.04, 0.50)),
        new FormationSlot(PlayerRole.Defender, Duty.Support, new Vec2(0.18, 0.15)),
        new FormationSlot(PlayerRole.Defender, Duty.Defend, new Vec2(0.15, 0.37)),
        new FormationSlot(PlayerRole.Defender, Duty.Defend, new Vec2(0.15, 0.63)),
        new FormationSlot(PlayerRole.Defender, Duty.Support, new Vec2(0.18, 0.85)),
        new FormationSlot(PlayerRole.Midfielder, Duty.Support, new Vec2(0.42, 0.15)),
        new FormationSlot(PlayerRole.Midfielder, Duty.Defend, new Vec2(0.38, 0.42)),
        new FormationSlot(PlayerRole.Midfielder, Duty.Support, new Vec2(0.42, 0.58)),
        new FormationSlot(PlayerRole.Midfielder, Duty.Support, new Vec2(0.42, 0.85)),
        new FormationSlot(PlayerRole.Forward, Duty.Attack, new Vec2(0.68, 0.42)),
        new FormationSlot(PlayerRole.Forward, Duty.Support, new Vec2(0.64, 0.58)),
    });
}
