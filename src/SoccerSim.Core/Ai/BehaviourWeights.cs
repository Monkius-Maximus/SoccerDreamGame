using SoccerSim.Core.Pitch;
using SoccerSim.Core.Tactics;

namespace SoccerSim.Core.Ai;

/// <summary>
/// THE single modulation point of the player AI (the heart of tactics → behaviour):
/// team tactics + personality + role/duty are combined here, once per player, into the weights
/// BOTH layers consume — the steering blend (movement) and the utility action biases (decision).
/// Nothing else in the AI reads tactics or personality directly, so a tactic change tilts every
/// behaviour through this one function and the resulting play style EMERGES.
/// </summary>
/// <param name="AnchorAdvanceMetres">How far (m) the formation anchor pushes toward the opponent goal (mentality + duty; 0 for the goalkeeper).</param>
/// <param name="PhaseShiftScale">How much of the in/out-of-possession line shift applies to this player (goalkeepers barely move).</param>
/// <param name="WidthScale">Multiplier on the slot's lateral spread (team Width instruction).</param>
/// <param name="BallSway">Fraction of the anchor→ball offset the anchor drifts toward the ball (lower for disciplined players).</param>
/// <param name="ArriveWeight">Steering blend weight of returning to the anchor.</param>
/// <param name="PursueWeight">Steering blend weight of hunting the carrier when engaged (pressing + aggression).</param>
/// <param name="InterposeWeight">Steering blend weight of blocking the carrier→goal line.</param>
/// <param name="SeparationWeight">Steering blend weight of not bunching with teammates.</param>
/// <param name="EngageRadiusMetres">Distance (m) from the opposing carrier at which this player starts pressing — pressing intensity raises it, so high pressing engages EARLIER.</param>
/// <param name="OutOfPossessionSwayBoost">Multiplier on the anchor's ball-sway while defending: a high press steps the WHOLE shape toward the ball, not just the engaged pressers.</param>
/// <param name="SimultaneousPressers">How many of the closest defenders commit to the carrier at once (1 in a passive block, up to 3 in a full press).</param>
/// <param name="ShootBias">Utility multiplier for shooting (selfishness + attacking mentality).</param>
/// <param name="DribbleBias">Utility multiplier for taking the man on.</param>
/// <param name="ShortPassBias">Utility multiplier for safe short passes (collective players, low directness).</param>
/// <param name="LongPassBias">Utility multiplier for direct long balls (high directness).</param>
/// <param name="CarryBias">Utility multiplier for carrying into space (tempo).</param>
/// <param name="TackleBias">Utility multiplier for diving in (aggression + pressing).</param>
/// <param name="ContainBias">Utility multiplier for containing without committing (discipline).</param>
/// <param name="RiskTolerance">General risk appetite in [0, 1] fed into risky-option considerations.</param>
public sealed record BehaviourWeights(
    double AnchorAdvanceMetres,
    double PhaseShiftScale,
    double WidthScale,
    double BallSway,
    double ArriveWeight,
    double PursueWeight,
    double InterposeWeight,
    double SeparationWeight,
    double EngageRadiusMetres,
    double OutOfPossessionSwayBoost,
    int SimultaneousPressers,
    double ShootBias,
    double DribbleBias,
    double ShortPassBias,
    double LongPassBias,
    double CarryBias,
    double TackleBias,
    double ContainBias,
    double RiskTolerance)
{
    /// <summary>Derive the full weight set for one player. Inputs are already validated by their own types.</summary>
    public static BehaviourWeights Derive(TeamTactics tactics, PersonalityProfile personality, FormationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(tactics);
        ArgumentNullException.ThrowIfNull(slot);

        double lean = tactics.Mentality.Lean();
        TeamInstructions i = tactics.Instructions;
        bool isGoalkeeper = slot.Role == PlayerRole.Goalkeeper;

        double dutyAdvance = slot.Duty switch
        {
            Duty.Defend => -3.0,
            Duty.Support => 0.0,
            Duty.Attack => 4.0,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot.Duty, "Unknown duty."),
        };

        // Role scaling is data, not special-cased logic: it only feeds the same shared weights.
        double roleEngageScale = slot.Role switch
        {
            PlayerRole.Goalkeeper => 0.25,
            PlayerRole.Defender => 0.9,
            PlayerRole.Midfielder => 1.0,
            PlayerRole.Forward => 0.8,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot.Role, "Unknown role."),
        };

        return new BehaviourWeights(
            AnchorAdvanceMetres: isGoalkeeper ? 0.0 : (lean * 6.0) + dutyAdvance,
            PhaseShiftScale: isGoalkeeper ? 0.1 : 1.0,
            WidthScale: 0.7 + (i.Width * 0.6),
            BallSway: (isGoalkeeper ? 0.06 : 0.30) * (1.35 - (0.7 * personality.Discipline)),
            ArriveWeight: 0.9 + (0.5 * personality.Discipline),
            PursueWeight: 0.7 + (1.2 * i.PressingIntensity) + (0.4 * personality.Aggression),
            InterposeWeight: 0.5 + (0.7 * i.PressingIntensity),
            SeparationWeight: 0.35,
            EngageRadiusMetres: (5.0 + (11.0 * i.PressingIntensity) + (2.0 * personality.Aggression)) * roleEngageScale,
            OutOfPossessionSwayBoost: 1.0 + (0.8 * i.PressingIntensity),
            SimultaneousPressers: (int)Math.Round(1.0 + (2.0 * i.PressingIntensity)),
            ShootBias: Bias(0.90 + (0.45 * personality.Selfishness) + (0.12 * lean)),
            DribbleBias: Bias(0.66 + (0.38 * personality.Selfishness) + (0.08 * lean)),
            ShortPassBias: Bias(0.80 + (0.35 * (1.0 - personality.Selfishness)) + (0.35 * (1.0 - i.PassingDirectness))),
            LongPassBias: Bias(0.52 + (0.75 * i.PassingDirectness) + (0.10 * lean)),
            CarryBias: Bias(0.70 + (0.25 * i.Tempo)),
            TackleBias: Bias(0.62 + (0.45 * personality.Aggression) + (0.30 * i.PressingIntensity)),
            ContainBias: Bias(0.72 + (0.35 * personality.Discipline) - (0.25 * i.PressingIntensity)),
            RiskTolerance: Math.Clamp(0.45 + (0.25 * lean) + (0.20 * personality.Selfishness), 0.0, 1.0));

        static double Bias(double value) => Math.Max(0.05, value);
    }
}
