using SoccerSim.Core.Ai;
using SoccerSim.Core.Ai.Steering;
using SoccerSim.Core.Ai.Utility;
using SoccerSim.Core.Pitch;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class SteeringBehaviorsTests
{
    [Fact]
    public void Arrive_FarFromTarget_MovesAtFullSpeedTowardIt()
    {
        Vec2 velocity = SteeringBehaviors.Arrive(new Vec2(0, 0), new Vec2(20, 0), maxSpeed: 6.0, slowingRadius: 3.0);

        Assert.Equal(6.0, velocity.Length, 6);
        Assert.True(velocity.X > 0 && Math.Abs(velocity.Y) < 1e-9);
    }

    [Fact]
    public void Arrive_InsideSlowingRadius_Decelerates()
    {
        Vec2 velocity = SteeringBehaviors.Arrive(new Vec2(0, 0), new Vec2(1.5, 0), maxSpeed: 6.0, slowingRadius: 3.0);

        Assert.Equal(3.0, velocity.Length, 6);
    }

    [Fact]
    public void Arrive_AtTarget_IsStill()
    {
        Assert.Equal(Vec2.Zero, SteeringBehaviors.Arrive(new Vec2(5, 5), new Vec2(5, 5), 6.0, 3.0));
    }

    [Fact]
    public void Pursue_LeadsAMovingTarget()
    {
        // Target at (10, 0) running "up" (+Y): pursuit must aim above the target's current spot.
        Vec2 velocity = SteeringBehaviors.Pursue(new Vec2(0, 0), new Vec2(10, 0), new Vec2(0, 4), maxSpeed: 6.0);

        Assert.True(velocity.Y > 0.5, $"expected an anticipating Y component, got {velocity}");
        Assert.Equal(6.0, velocity.Length, 6);
    }

    [Fact]
    public void Interpose_HeadsForThePointBetween()
    {
        Vec2 velocity = SteeringBehaviors.Interpose(new Vec2(0, 10), new Vec2(-10, 0), new Vec2(10, 0), maxSpeed: 6.0);

        // Midpoint is (0, 0): straight down from (0, 10).
        Assert.True(velocity.Y < 0 && Math.Abs(velocity.X) < 1e-9, $"expected straight descent, got {velocity}");
    }

    [Fact]
    public void Separation_PushesAwayFromCloseNeighbours()
    {
        Vec2 velocity = SteeringBehaviors.Separation(
            new Vec2(0, 0), new[] { new Vec2(-1.0, 0) }, radius: 4.0, maxSpeed: 6.0);

        Assert.True(velocity.X > 0, $"expected a push away from the neighbour, got {velocity}");
    }

    [Fact]
    public void Separation_IgnoresNeighboursOutsideRadius()
    {
        Vec2 velocity = SteeringBehaviors.Separation(
            new Vec2(0, 0), new[] { new Vec2(10, 0) }, radius: 4.0, maxSpeed: 6.0);

        Assert.Equal(Vec2.Zero, velocity);
    }

    [Fact]
    public void Blend_CapsTheCombinedVelocityAtMaxSpeed()
    {
        Vec2 velocity = SteeringBehaviors.Blend(6.0, new[]
        {
            (new Vec2(6, 0), 1.0),
            (new Vec2(0, 6), 1.0),
        });

        Assert.Equal(6.0, velocity.Length, 6);
    }

    [Fact]
    public void Blend_WithNegativeWeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SteeringBehaviors.Blend(6.0, new[] { (new Vec2(1, 0), -0.5) }));
    }

    [Theory]
    [InlineData(ResponseCurveKind.Linear, 0.5, 0.5)]
    [InlineData(ResponseCurveKind.Quadratic, 0.5, 0.25)]
    [InlineData(ResponseCurveKind.SquareRoot, 0.25, 0.5)]
    [InlineData(ResponseCurveKind.Inverse, 0.3, 0.7)]
    public void ResponseCurves_ShapeInputsAsDocumented(ResponseCurveKind kind, double input, double expected)
    {
        Assert.Equal(expected, new ResponseCurve(kind).Evaluate(input), 6);
    }

    [Fact]
    public void ResponseCurves_ClampInputsToUnitRange()
    {
        Assert.Equal(1.0, ResponseCurve.Linear.Evaluate(7.0), 6);
        Assert.Equal(0.0, ResponseCurve.Quadratic.Evaluate(-3.0), 6);
    }

    [Fact]
    public void LaneClearance_IsZeroWhenAnOpponentSitsOnTheLane()
    {
        var opponents = new[] { new PlayerState(9, TeamSide.Away, new Vec2(5, 0), Vec2.Zero) };

        Assert.Equal(0.0, UtilityDecider.LaneClearance(new Vec2(0, 0), new Vec2(10, 0), opponents), 6);
    }

    [Fact]
    public void LaneClearance_IsFullWhenNobodyIsNear()
    {
        var opponents = new[] { new PlayerState(9, TeamSide.Away, new Vec2(5, 30), Vec2.Zero) };

        Assert.Equal(1.0, UtilityDecider.LaneClearance(new Vec2(0, 0), new Vec2(10, 0), opponents), 6);
    }

    [Fact]
    public void InfluenceMap_ReflectsLocalNumericalSuperiority()
    {
        var map = InfluenceMap.CreateDefault(PitchDimensions.Standard);
        var players = new List<PlayerState>();
        for (int i = 0; i < 5; i++)
        {
            players.Add(new PlayerState(i, TeamSide.Home, new Vec2(20 + i, 20), Vec2.Zero));
            players.Add(new PlayerState(100 + i, TeamSide.Away, new Vec2(80 + i, 50), Vec2.Zero));
        }

        map.Rebuild(players);

        Assert.True(map.ControlAt(new Vec2(20, 20), TeamSide.Home) > 0.7);
        Assert.True(map.ControlAt(new Vec2(80, 50), TeamSide.Home) < 0.3);
        Assert.True(map.ControlAt(new Vec2(80, 50), TeamSide.Away) > 0.7);
    }
}
