using SoccerSim.Core.Domain;

namespace SoccerSim.Core.LifeSim;

/// <summary>
/// Owns the active career's <see cref="WellbeingState"/> and is the seam through which the life-sim
/// reaches the rest of the simulation.
///
/// <para>
/// This interface is the answer to "do the need bars actually do anything". Three existing systems
/// consume it and none of them needed a signature change to accept it:
/// <see cref="SyncFormMood"/> writes into <see cref="Player.FormMood"/> (so effective attributes
/// drop), <see cref="EventProbabilityMultiplier"/> is handed to
/// <c>EventRollContext.GlobalProbabilityMultiplier</c> (so more life events fire), and
/// <see cref="Snapshot"/>'s risk fields are read by the training/match and dugout layers.
/// </para>
/// </summary>
public interface IWellbeingService
{
    /// <summary>The career role being lived; selects the need profile.</summary>
    CareerRole Role { get; }

    /// <summary>Live need gauges. Read through <see cref="Snapshot"/> rather than poking raw values.</summary>
    WellbeingState State { get; }

    /// <summary>The current derived read model.</summary>
    WellbeingSnapshot Snapshot { get; }

    /// <summary>
    /// Shorthand for <c>Snapshot.EventProbabilityMultiplier</c>, shaped to drop straight into the
    /// event roll context the calendar advance builds each day.
    /// </summary>
    double EventProbabilityMultiplier { get; }

    /// <summary>Advance one simulated day, persist the result, and return any degraded needs.</summary>
    IReadOnlyList<NeedAlert> AdvanceDay(DateTime date);

    /// <summary>
    /// True when the human can pay for this activity. Always true when it is free or when no
    /// economy is wired. The UI disables what cannot be afforded rather than letting it be clicked
    /// and refused.
    /// </summary>
    bool CanAfford(string activityKey);

    /// <summary>
    /// Perform an activity by catalogue key, charge it, persist, and log it. Throws when the key is
    /// unknown, the activity is not available to <see cref="Role"/>, or it cannot be afforded.
    /// </summary>
    ActivityOutcome Perform(string activityKey, DateTime date);

    /// <summary>
    /// Push the derived form modifier into a player's <see cref="Player.FormMood"/>, which is what
    /// makes wellbeing reach the pitch: <see cref="Player.EffectiveAttributes"/> already applies
    /// FormMood, so no match-engine code changes to honour it.
    /// </summary>
    void SyncFormMood(Player player);

    /// <summary>Raised whenever the state changed, carrying the freshly derived snapshot (for the HUD).</summary>
    event Action<WellbeingSnapshot>? Changed;
}
