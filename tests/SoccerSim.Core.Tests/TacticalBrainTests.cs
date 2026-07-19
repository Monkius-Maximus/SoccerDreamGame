using SoccerSim.Core.Ai;
using SoccerSim.Core.Pitch;
using SoccerSim.Core.Random;
using SoccerSim.Core.Tactics;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Brain-level acceptance criteria: personality changes the individual, hysteresis prevents
/// twitching, tactics move the anchors, and decisions are deterministic.
/// </summary>
public sealed class TacticalBrainTests
{
    private static readonly FormationSlot ForwardSlot = new(PlayerRole.Forward, Duty.Attack, new Vec2(0.68, 0.42));

    private static PlayerPerception CarrierPerception(int carrierId, Vec2 carrierPosition, Vec2 matePosition, Vec2 opponentPosition)
    {
        var self = new PlayerState(carrierId, TeamSide.Home, carrierPosition, Vec2.Zero);
        return new PlayerPerception(
            self,
            new BallState(carrierPosition, Vec2.Zero, carrierId),
            new[] { new PlayerState(2, TeamSide.Home, matePosition, Vec2.Zero) },
            new[] { new PlayerState(3, TeamSide.Away, opponentPosition, Vec2.Zero) },
            MatchPhase.InPossession,
            new TacticalAiTestKit.FlatInfluence(),
            PitchDimensions.Standard);
    }

    [Fact]
    public void Personality_SelfishAggressiveShooter_ShootsWhereACollectivePlayerPasses()
    {
        // Same situation: carrier 8 m from goal, an open teammate 12 m away, keeper off the lane.
        var carrierPos = new Vec2(97, 34);
        var matePos = new Vec2(88, 26);
        var keeperPos = new Vec2(99, 34);

        var selfish = new TacticalPlayerBrain(
            TacticalAiTestKit.Player(1, new PersonalityProfile(0.8, 0.9, 0.3)),
            ForwardSlot, TeamTactics.Default, new SplitMix64Random(7));
        var collective = new TacticalPlayerBrain(
            TacticalAiTestKit.Player(1, new PersonalityProfile(0.3, 0.1, 0.8)),
            ForwardSlot, TeamTactics.Default, new SplitMix64Random(7));

        Intention selfishChoice = selfish.Decide(0, CarrierPerception(1, carrierPos, matePos, keeperPos));
        Intention collectiveChoice = collective.Decide(0, CarrierPerception(1, carrierPos, matePos, keeperPos));

        Assert.IsType<Intention.Shoot>(selfishChoice);
        Assert.IsType<Intention.Pass>(collectiveChoice);
    }

    [Fact]
    public void Hysteresis_StaticSituation_NeverSwitchesAction()
    {
        var brain = new TacticalPlayerBrain(
            TacticalAiTestKit.Player(1), ForwardSlot, TeamTactics.Default, new SplitMix64Random(11));

        for (int tick = 0; tick < 120; tick++)
        {
            // Mid-pitch, marker at a middling distance: carry and dribble score close together.
            brain.Decide(tick, CarrierPerception(1, new Vec2(60, 34), new Vec2(52, 28), new Vec2(64, 34)));
        }

        Assert.Equal(0, brain.DecisionSwitches);
    }

    [Fact]
    public void Hysteresis_JitteringInputs_StaysCommitted()
    {
        var brain = new TacticalPlayerBrain(
            TacticalAiTestKit.Player(1), ForwardSlot, TeamTactics.Default, new SplitMix64Random(11));

        for (int tick = 0; tick < 240; tick++)
        {
            // The marker bobs in and out, nudging the carry/dribble scores across each other.
            double wobble = (tick % 2 == 0) ? -0.6 : 0.6;
            brain.Decide(tick, CarrierPerception(1, new Vec2(60, 34), new Vec2(52, 28), new Vec2(64 + wobble, 34)));
        }

        // 240 ticks = 30 re-evaluations; without hysteresis this could switch every one of them.
        Assert.True(brain.DecisionSwitches <= 4, $"brain twitched {brain.DecisionSwitches} times");
    }

    [Fact]
    public void Decisions_AreDeterministic_ForTheSameSeed()
    {
        var a = new TacticalPlayerBrain(TacticalAiTestKit.Player(1), ForwardSlot, TeamTactics.Default, new SplitMix64Random(99));
        var b = new TacticalPlayerBrain(TacticalAiTestKit.Player(1), ForwardSlot, TeamTactics.Default, new SplitMix64Random(99));

        for (int tick = 0; tick < 60; tick++)
        {
            PlayerPerception perception = CarrierPerception(1, new Vec2(70 + (tick * 0.1), 34), new Vec2(60, 30), new Vec2(75, 35));
            Assert.Equal(a.Decide(tick, perception), b.Decide(tick, perception));
        }
    }

    [Fact]
    public void Mentality_AttackingAnchorsSitHigherUpThePitch()
    {
        FormationSlot slot = ForwardSlot;
        Vec2 ball = PitchDimensions.Standard.Centre;

        Vec2 AnchorFor(Mentality mentality)
        {
            var weights = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(mentality), PersonalityProfile.Neutral, slot);
            return TacticalAnchor.Compute(slot, weights, TeamSide.Home, ball, MatchPhase.Contested, PitchDimensions.Standard);
        }

        Assert.True(AnchorFor(Mentality.VeryAttacking).X > AnchorFor(Mentality.Balanced).X);
        Assert.True(AnchorFor(Mentality.Balanced).X > AnchorFor(Mentality.VeryDefensive).X);
    }

    [Fact]
    public void Anchor_MirrorsForTheAwaySide()
    {
        var weights = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(), PersonalityProfile.Neutral, ForwardSlot);
        Vec2 ball = PitchDimensions.Standard.Centre;

        Vec2 home = TacticalAnchor.Compute(ForwardSlot, weights, TeamSide.Home, ball, MatchPhase.Contested, PitchDimensions.Standard);
        Vec2 away = TacticalAnchor.Compute(ForwardSlot, weights, TeamSide.Away, ball, MatchPhase.Contested, PitchDimensions.Standard);

        Assert.Equal(home.X, PitchDimensions.Standard.Length - away.X, 6);
    }

    [Fact]
    public void Pressing_WidensTheEngagementRadius_SoDefendersEngageEarlier()
    {
        FormationSlot slot = new(PlayerRole.Midfielder, Duty.Support, new Vec2(0.42, 0.58));

        BehaviourWeights lowPress = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(pressing: 0.1), PersonalityProfile.Neutral, slot);
        BehaviourWeights highPress = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(pressing: 0.9), PersonalityProfile.Neutral, slot);

        Assert.True(highPress.EngageRadiusMetres > lowPress.EngageRadiusMetres + 5.0);
        Assert.True(highPress.TackleBias > lowPress.TackleBias);
    }

    [Fact]
    public void Directness_TiltsThePassBiases()
    {
        FormationSlot slot = new(PlayerRole.Midfielder, Duty.Support, new Vec2(0.42, 0.58));

        BehaviourWeights shortGame = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(directness: 0.1), PersonalityProfile.Neutral, slot);
        BehaviourWeights directGame = BehaviourWeights.Derive(TacticalAiTestKit.Tactics(directness: 0.9), PersonalityProfile.Neutral, slot);

        Assert.True(directGame.LongPassBias > shortGame.LongPassBias);
        Assert.True(shortGame.ShortPassBias > directGame.ShortPassBias);
    }
}
