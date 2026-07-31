namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The immutable derived read model of a <see cref="WellbeingState"/> — and the single point every
/// other system reads. Nothing outside the life-sim inspects raw need values.
///
/// <para>
/// This type is what stops the needs from being decorative. Each field is consumed by a system that
/// already exists:
/// </para>
/// <list type="bullet">
///   <item><see cref="FormModifier"/> is a −5..+5 delta shaped for <see cref="SoccerSim.Core.Domain.FormMood"/>,
///   so poor wellbeing lowers effective attributes through the existing
///   <see cref="SoccerSim.Core.Domain.Player.EffectiveAttributes"/> path — no new plumbing in the match engine.</item>
///   <item><see cref="EventProbabilityMultiplier"/> feeds <c>EventRollContext.GlobalProbabilityMultiplier</c>,
///   which <c>EventManager.RollForDay</c> already multiplies into every roll — so a player who is
///   burning out genuinely attracts more life events.</item>
///   <item><see cref="InjuryRisk"/> is read by the training/match layer for a
///   <see cref="CareerRole.Player"/>.</item>
///   <item><see cref="DecisionQuality"/> and <see cref="BurnoutRisk"/> are read by the dugout layer for a
///   <see cref="CareerRole.Manager"/>.</item>
/// </list>
///
/// <para>
/// Every field is computed for every role — the role decides which one its consumer reads, not which
/// ones exist. That keeps one snapshot type across both careers.
/// </para>
/// </summary>
/// <param name="Role">The career this snapshot describes.</param>
/// <param name="Index">Weighted 0–100 wellbeing score; comparable across roles because profile weights sum to 1.</param>
/// <param name="FormModifier">−5..+5, shaped to drop straight into <see cref="SoccerSim.Core.Domain.FormMood"/>.</param>
/// <param name="EventProbabilityMultiplier">Multiplier for the daily life-event roll; &gt;1 means more events fire.</param>
/// <param name="InjuryRisk">0–1 chance-weight of picking up a knock; driven by fitness and energy deficits.</param>
/// <param name="BurnoutRisk">0–1 chance-weight of a manager burning out; driven by energy, focus and morale deficits.</param>
/// <param name="DecisionQuality">0–1 sharpness of tactical/managerial decisions.</param>
/// <param name="CriticalNeeds">Needs currently in <see cref="NeedBand.Critical"/>, in <see cref="Needs.All"/> order.</param>
public readonly record struct WellbeingSnapshot(
    CareerRole Role,
    double Index,
    int FormModifier,
    double EventProbabilityMultiplier,
    double InjuryRisk,
    double BurnoutRisk,
    double DecisionQuality,
    IReadOnlyList<NeedKind> CriticalNeeds)
{
    /// <summary>Wellbeing index at which the event-pressure multiplier is exactly 1.0 (neutral).</summary>
    public const double NeutralEventPressureIndex = 75.0;

    /// <summary>Event-pressure multiplier floor (thriving) and ceiling (falling apart).</summary>
    public const double MinEventMultiplier = 0.75;

    /// <inheritdoc cref="MinEventMultiplier"/>
    public const double MaxEventMultiplier = 1.75;

    /// <summary>Baseline injury risk that exists even at perfect conditioning.</summary>
    private const double BaseInjuryRisk = 0.02;

    /// <summary>
    /// Derive the snapshot. Pure: the same state always yields the same snapshot, which is what lets
    /// the wellbeing → form → match-result chain stay deterministic under a fixed seed.
    /// </summary>
    public static WellbeingSnapshot From(WellbeingState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        double index = 0.0;
        List<NeedKind>? critical = null;
        foreach (NeedKind need in Needs.All)
        {
            double value = state[need];
            index += value * state.Profile[need].Weight;
            if (Needs.BandOf(value) == NeedBand.Critical)
                (critical ??= new List<NeedKind>()).Add(need);
        }

        double energy = state[NeedKind.Energy] / 100.0;
        double fitness = state[NeedKind.Fitness] / 100.0;
        double morale = state[NeedKind.Morale] / 100.0;
        double focus = state[NeedKind.Focus] / 100.0;

        return new WellbeingSnapshot(
            state.Role,
            index,
            // Index 0 -> -5, 50 -> 0, 100 -> +5. Rounds to the nearest whole FormMood step.
            FormModifier: (int)Math.Round(Math.Clamp((index - 50.0) / 10.0, -5.0, 5.0), MidpointRounding.AwayFromZero),
            // Linear in the index and anchored so that NeutralEventPressureIndex maps to exactly 1.0:
            // thriving suppresses event pressure, slipping raises it.
            EventProbabilityMultiplier: Math.Clamp(
                1.0 + (NeutralEventPressureIndex - index) / 100.0,
                MinEventMultiplier,
                MaxEventMultiplier),
            // A player breaks down when conditioning and rest are gone; nutrition acts through Fitness.
            InjuryRisk: Math.Clamp(BaseInjuryRisk + (1.0 - fitness) * 0.45 + (1.0 - energy) * 0.28, 0.0, 1.0),
            // A manager breaks down when rest, clarity and mood are gone; conditioning barely features.
            BurnoutRisk: Math.Clamp((1.0 - energy) * 0.40 + (1.0 - focus) * 0.35 + (1.0 - morale) * 0.25, 0.0, 1.0),
            // Sharpness of a decision: mostly clarity, then rest, then mood.
            DecisionQuality: Math.Clamp(focus * 0.50 + energy * 0.30 + morale * 0.20, 0.0, 1.0),
            CriticalNeeds: (IReadOnlyList<NeedKind>?)critical ?? Array.Empty<NeedKind>());
    }

    /// <summary>True when at least one need has bottomed out into <see cref="NeedBand.Critical"/>.</summary>
    public bool HasCriticalNeed => CriticalNeeds.Count > 0;
}
