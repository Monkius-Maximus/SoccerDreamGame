using SoccerSim.Core.Pitch;

namespace SoccerSim.Core.Ai.Steering;

/// <summary>
/// Reynolds steering behaviours as pure functions. Every behaviour returns a DESIRED VELOCITY
/// (m/s, capped at maxSpeed); <see cref="Blend"/> combines them with tactic-derived weights.
/// No behaviour mutates state or draws randomness — determinism is structural here.
/// </summary>
public static class SteeringBehaviors
{
    /// <summary>Head to a target, decelerating smoothly inside <paramref name="slowingRadius"/>.</summary>
    public static Vec2 Arrive(Vec2 position, Vec2 target, double maxSpeed, double slowingRadius)
    {
        ValidateSpeed(maxSpeed);
        if (slowingRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(slowingRadius), slowingRadius, "slowingRadius must be > 0.");

        Vec2 offset = target - position;
        double distance = offset.Length;
        if (distance < 1e-6)
            return Vec2.Zero;

        double speed = distance >= slowingRadius ? maxSpeed : maxSpeed * (distance / slowingRadius);
        return offset * (speed / distance);
    }

    /// <summary>Chase a moving target, anticipating where it will be rather than where it is.</summary>
    public static Vec2 Pursue(Vec2 position, Vec2 targetPosition, Vec2 targetVelocity, double maxSpeed)
    {
        ValidateSpeed(maxSpeed);

        double distance = position.DistanceTo(targetPosition);
        double lookAheadSeconds = distance / maxSpeed;
        Vec2 predicted = targetPosition + (targetVelocity * lookAheadSeconds);
        return Seek(position, predicted, maxSpeed);
    }

    /// <summary>
    /// Take up a point on the line between <paramref name="a"/> and <paramref name="b"/>
    /// (mark a runner, block a passing lane). <paramref name="bias"/> = 0 sits on
    /// <paramref name="a"/>, 1 on <paramref name="b"/>, 0.5 midway.
    /// </summary>
    public static Vec2 Interpose(Vec2 position, Vec2 a, Vec2 b, double maxSpeed, double bias = 0.5)
    {
        ValidateSpeed(maxSpeed);
        if (bias is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(bias), bias, "bias must be within [0, 1].");

        return Arrive(position, Vec2.Lerp(a, b, bias), maxSpeed, slowingRadius: 2.0);
    }

    /// <summary>Push away from teammates closer than <paramref name="radius"/> so the shape never bunches.</summary>
    public static Vec2 Separation(Vec2 position, IReadOnlyList<Vec2> neighbours, double radius, double maxSpeed)
    {
        ValidateSpeed(maxSpeed);
        if (radius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "radius must be > 0.");
        ArgumentNullException.ThrowIfNull(neighbours);

        Vec2 push = Vec2.Zero;
        foreach (Vec2 neighbour in neighbours)
        {
            Vec2 away = position - neighbour;
            double distance = away.Length;
            if (distance < 1e-6)
            {
                // Exactly overlapping: push along +X deterministically rather than dividing by zero.
                push += new Vec2(1.0, 0.0);
                continue;
            }

            if (distance < radius)
                push += away * ((1.0 - (distance / radius)) / distance);
        }

        return (push * maxSpeed).ClampLength(maxSpeed);
    }

    /// <summary>
    /// Weighted combination of steering outputs into one desired velocity, capped at
    /// <paramref name="maxSpeed"/>. Weights come from the tactic/personality-derived
    /// <c>BehaviourWeights</c> — never from ad-hoc constants at call sites.
    /// </summary>
    public static Vec2 Blend(double maxSpeed, IReadOnlyList<(Vec2 Velocity, double Weight)> components)
    {
        ValidateSpeed(maxSpeed);
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0)
            throw new ArgumentException("Blend needs at least one component.", nameof(components));

        Vec2 sum = Vec2.Zero;
        foreach ((Vec2 velocity, double weight) in components)
        {
            if (weight < 0.0)
                throw new ArgumentOutOfRangeException(nameof(components), weight, "Blend weights must be >= 0.");
            sum += velocity * weight;
        }

        return sum.ClampLength(maxSpeed);
    }

    private static Vec2 Seek(Vec2 position, Vec2 target, double maxSpeed)
    {
        Vec2 direction = (target - position).Normalized();
        return direction * maxSpeed;
    }

    private static void ValidateSpeed(double maxSpeed)
    {
        if (maxSpeed <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxSpeed), maxSpeed, "maxSpeed must be > 0.");
    }
}
