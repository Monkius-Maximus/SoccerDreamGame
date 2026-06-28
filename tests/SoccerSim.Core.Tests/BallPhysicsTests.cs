using SoccerSim.Core.MatchEngine;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class BallPhysicsTests
{
    private const double Dt = 1.0 / 60.0;

    [Fact]
    public void SideSpin_CurvesBallLaterally()
    {
        // Struck along +X with +Y (side) spin. Magnus = spin x velocity points along -Z, so the
        // ball should bend toward smaller Z — the Roberto-Carlos validation target.
        var spun = new BallState(new Vector3(0, 0.5, 34), new Vector3(30, 0, 0), new Vector3(0, 40, 0));
        var straight = new BallState(new Vector3(0, 0.5, 34), new Vector3(30, 0, 0), Vector3.Zero);

        for (int i = 0; i < 120; i++)
        {
            BallPhysics.Step(spun, MatchConditions.Default, Dt);
            BallPhysics.Step(straight, MatchConditions.Default, Dt);
        }

        Assert.True(spun.Position.Z < 34 - 0.1, $"side spin should curve the ball laterally, Z={spun.Position.Z}");
        // With no spin and no wind there is no lateral force at all, so Z is untouched.
        Assert.Equal(34.0, straight.Position.Z, 9);
    }

    [Fact]
    public void Bounce_LosesEnergy_ToRestitution()
    {
        var ball = new BallState(new Vector3(52, 5, 34), Vector3.Zero, Vector3.Zero);

        bool bounceTested = false;
        for (int i = 0; i < 4000 && !bounceTested; i++)
        {
            double yVelBefore = ball.Velocity.Y;
            BallPhysics.Step(ball, MatchConditions.Default, Dt);
            if (yVelBefore < 0 && ball.Velocity.Y > 0)
            {
                // Rebound speed must be less than the impact speed (energy lost).
                Assert.True(ball.Velocity.Y < -yVelBefore,
                    $"bounce should lose energy: up {ball.Velocity.Y} vs impact {yVelBefore}");
                bounceTested = true;
            }
        }

        Assert.True(bounceTested, "the dropped ball should have bounced");
    }

    [Fact]
    public void Drag_SlowsAFlyingBall()
    {
        var ball = new BallState(new Vector3(0, 1.0, 34), new Vector3(20, 0, 0), Vector3.Zero);
        double startSpeed = ball.Velocity.X;

        for (int i = 0; i < 20; i++) // stays airborne, so only drag acts on X
            BallPhysics.Step(ball, MatchConditions.Default, Dt);

        Assert.True(ball.Velocity.X < startSpeed, "drag should reduce horizontal speed");
        Assert.True(ball.Velocity.X > 0, "the ball should still be moving forward");
    }

    [Fact]
    public void Step_IsDeterministic_ForIdenticalInput()
    {
        var a = new BallState(new Vector3(0, 1, 34), new Vector3(25, 3, 2), new Vector3(0, 30, 5));
        var b = new BallState(new Vector3(0, 1, 34), new Vector3(25, 3, 2), new Vector3(0, 30, 5));

        for (int i = 0; i < 300; i++)
        {
            BallPhysics.Step(a, MatchConditions.Default, Dt);
            BallPhysics.Step(b, MatchConditions.Default, Dt);
        }

        Assert.Equal(a.Position, b.Position);
        Assert.Equal(a.Velocity, b.Velocity);
    }
}
