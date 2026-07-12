using SoccerSim.Core.Domain;
using SoccerSim.Core.Pitch;
using SoccerSim.Core.Random;
using SoccerSim.Core.Tactics;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>Fail-fast contract of the tactics data types: invalid tactics throw, they never half-work.</summary>
public sealed class TacticsValidationTests
{
    [Fact]
    public void Formation_WithWrongSlotCount_Throws()
    {
        FormationSlot[] tenSlots = Formation.FourFourTwo().Slots.Take(10).ToArray();

        Assert.Throws<ArgumentException>(() => new Formation("broken", tenSlots));
    }

    [Fact]
    public void Formation_WithoutExactlyOneGoalkeeper_Throws()
    {
        FormationSlot[] slots = Formation.FourFourTwo().Slots.ToArray();
        slots[0] = new FormationSlot(PlayerRole.Defender, Duty.Defend, slots[0].BasePosition);

        Assert.Throws<ArgumentException>(() => new Formation("no-gk", slots));

        slots[0] = new FormationSlot(PlayerRole.Goalkeeper, Duty.Defend, new Vec2(0.04, 0.5));
        slots[1] = new FormationSlot(PlayerRole.Goalkeeper, Duty.Defend, new Vec2(0.06, 0.5));
        Assert.Throws<ArgumentException>(() => new Formation("two-gk", slots));
    }

    [Fact]
    public void FormationSlot_WithPositionOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FormationSlot(PlayerRole.Defender, Duty.Defend, new Vec2(1.2, 0.5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FormationSlot(PlayerRole.Defender, Duty.Defend, new Vec2(0.5, -0.1)));
    }

    [Theory]
    [InlineData(1.2, 0.5, 0.5, 0.5)]
    [InlineData(0.5, -0.01, 0.5, 0.5)]
    [InlineData(0.5, 0.5, 2.0, 0.5)]
    [InlineData(0.5, 0.5, 0.5, double.NaN)]
    public void TeamInstructions_OutOfRange_Throws(double pressing, double directness, double width, double tempo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TeamInstructions(pressing, directness, width, tempo));
    }

    [Fact]
    public void PersonalityProfile_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersonalityProfile(1.5, 0.5, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersonalityProfile(0.5, 0.5, -0.2));
    }

    [Fact]
    public void FourFourTwo_IsAValidFormation()
    {
        Formation formation = Formation.FourFourTwo();

        Assert.Equal(Formation.SquadSize, formation.Slots.Count);
        Assert.Equal(1, formation.Slots.Count(s => s.Role == PlayerRole.Goalkeeper));
    }

    [Fact]
    public void PitchSimulation_WithWrongSquadSize_Throws()
    {
        PitchTeam home = TacticalAiTestKit.Team(1, TeamTactics.Default);
        PitchTeam shortAway = TacticalAiTestKit.Team(100, TeamTactics.Default) with
        {
            Players = TacticalAiTestKit.Team(100, TeamTactics.Default).Players.Take(10).ToArray(),
        };

        Assert.Throws<ArgumentException>(() => new PitchSimulation(home, shortAway, new SplitMix64Random(1)));
    }

    [Fact]
    public void PitchSimulation_WithDuplicatePlayerIds_Throws()
    {
        PitchTeam home = TacticalAiTestKit.Team(1, TeamTactics.Default);
        PitchTeam clashingAway = TacticalAiTestKit.Team(1, TeamTactics.Default);

        Assert.Throws<ArgumentException>(() => new PitchSimulation(home, clashingAway, new SplitMix64Random(1)));
    }
}

/// <summary>Shared builders for the tactical AI tests.</summary>
internal static class TacticalAiTestKit
{
    public static PlayerAttributes Attributes(int value = 12) => new(
        Pace: value, Stamina: value, Strength: value, Passing: value,
        Shooting: value, Tackling: value, Vision: value);

    public static PitchPlayer Player(int id, PersonalityProfile? personality = null, int attributeValue = 12)
        => new(id, Attributes(attributeValue), personality ?? PersonalityProfile.Neutral);

    public static PitchTeam Team(int firstId, TeamTactics tactics, PersonalityProfile? personality = null)
        => new(Enumerable.Range(firstId, Formation.SquadSize).Select(id => Player(id, personality)).ToArray(), tactics);

    public static TeamTactics Tactics(
        Mentality mentality = Mentality.Balanced,
        double pressing = 0.5,
        double directness = 0.5,
        double width = 0.5,
        double tempo = 0.5)
        => new(Formation.FourFourTwo(), mentality, new TeamInstructions(pressing, directness, width, tempo));

    /// <summary>Constant-control influence map for hand-built perceptions.</summary>
    public sealed class FlatInfluence : IInfluenceMap
    {
        public double ControlAt(Vec2 point, TeamSide side) => 0.5;
    }
}
