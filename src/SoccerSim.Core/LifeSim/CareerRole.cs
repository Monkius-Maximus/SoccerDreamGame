namespace SoccerSim.Core.LifeSim;

/// <summary>
/// Which career the human is living.
///
/// <para>
/// The off-pitch life simulation is deliberately ONE system for both roles: the same six
/// <see cref="NeedKind"/>s decay for a player and for a manager, driven by the same
/// <see cref="LifeSimulator"/> and stored in the same save shape. Only two things differ —
/// the <see cref="NeedProfile"/> tuning (how fast each need drains and how much it counts)
/// and which derived field of <see cref="WellbeingSnapshot"/> the consuming system reads.
/// That is what keeps one simulator, one persistence table, and one HUD instead of two
/// parallel life-sims that inevitably drift apart.
/// </para>
/// </summary>
public enum CareerRole
{
    /// <summary>The human controls one athlete: wellbeing feeds on-pitch form and injury risk.</summary>
    Player,

    /// <summary>The human controls a club: wellbeing feeds decision quality and burnout risk.</summary>
    Manager,
}

/// <summary>
/// Bitmask of <see cref="CareerRole"/>s. Gates which <see cref="LifeActivity"/> entries a role may
/// perform, so the shared catalogue can carry role-exclusive leaves (an athlete's gym session, a
/// manager's film study) without splitting into two catalogues.
/// </summary>
[Flags]
public enum CareerRoles
{
    None = 0,

    Player = 1 << 0,

    Manager = 1 << 1,

    /// <summary>Available to every role — the shared spine of the activity catalogue.</summary>
    Both = Player | Manager,
}
