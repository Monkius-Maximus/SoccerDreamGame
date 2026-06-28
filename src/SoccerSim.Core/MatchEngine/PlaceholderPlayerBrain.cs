namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Trivial, fully deterministic brain that only proves the tick cycle: the outfield teammate
/// closest to the ball chases it (and shoots at the opponent goal once on it); the goalkeeper hugs
/// its line and tracks the ball's width; everyone else returns to their formation slot. This is NOT
/// real steering/utility AI — that is a separate module which replaces this brain without any engine
/// change. It draws no randomness, so the engine's seeded shot variation is the only stochastic input.
/// </summary>
public sealed class PlaceholderPlayerBrain : IPlayerBrain
{
    /// <summary>Within this planar distance (m) a player is treated as controlling the ball.</summary>
    public const double ControlRadius = 1.2;

    public Intention Decide(int tick, PlayerPerception perception)
    {
        PlayerState self = perception.Self;

        if (self.IsGoalkeeper)
        {
            // Sit on the goal line, slide across to the ball's width.
            var target = new Vector3(perception.OwnGoalCentre.X, 0, perception.BallPosition.Z);
            return MoveToward(self.Position, target);
        }

        if (IsClosestOutfieldTeammateToBall(perception))
        {
            if (self.Position.PlanarDistanceTo(perception.BallPosition) <= ControlRadius)
            {
                // On the ball → drive it at the opponent goal. The engine adds the seeded shot variation.
                Vector3 toGoal = (perception.AttackingGoalCentre - perception.BallPosition).Normalized();
                return new Intention.Shoot(toGoal, Power: 1.0, Spin: Vector3.Zero);
            }

            return MoveToward(self.Position, perception.BallPosition);
        }

        return MoveToward(self.Position, self.HomePosition);
    }

    private static Intention MoveToward(Vector3 from, Vector3 to)
    {
        Vector3 dir = (to.WithY(0) - from.WithY(0)).Normalized();
        return dir.LengthSquared < 1e-12 ? new Intention.Idle() : new Intention.Move(dir);
    }

    private static bool IsClosestOutfieldTeammateToBall(PlayerPerception perception)
    {
        PlayerState self = perception.Self;
        double mine = self.Position.PlanarDistanceTo(perception.BallPosition);

        foreach (PlayerState mate in perception.Teammates)
        {
            if (mate.IsGoalkeeper || mate.PlayerId == self.PlayerId)
                continue;

            double d = mate.Position.PlanarDistanceTo(perception.BallPosition);
            // Strictly closer, or equal distance with a lower id, wins — keeps the choice deterministic.
            if (d < mine || (d == mine && mate.PlayerId < self.PlayerId))
                return false;
        }

        return true;
    }
}
