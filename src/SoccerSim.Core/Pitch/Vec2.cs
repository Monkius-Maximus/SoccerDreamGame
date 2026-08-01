using SoccerSim.Core.Numerics;

namespace SoccerSim.Core.Pitch;

/// <summary>
/// Deterministic 2D vector in pitch space (metres). SoccerSim.Core must stay engine-agnostic
/// (see docs/ARCHITECTURE.md layering rules), so the on-pitch AI uses this double-precision
/// struct instead of Godot's float-based <c>Vector2</c>. The game layer converts to
/// <c>Godot.Vector2</c> at the rendering boundary. The API mirrors the Godot names
/// (Length, Normalized, DistanceTo, Lerp, Rotated, Dot) so that boundary stays thin.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero => new(0.0, 0.0);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public double LengthSquared => (X * X) + (Y * Y);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);

    public static Vec2 operator *(double s, Vec2 v) => v * s;

    public static Vec2 operator /(Vec2 v, double s) => new(v.X / s, v.Y / s);

    public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);

    /// <summary>Unit vector in this direction; the zero vector normalises to zero.</summary>
    public Vec2 Normalized()
    {
        double length = Length;
        return length < 1e-9 ? Zero : this / length;
    }

    public double Dot(Vec2 other) => (X * other.X) + (Y * other.Y);

    public double DistanceTo(Vec2 other) => (other - this).Length;

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + ((b - a) * t);

    /// <summary>
    /// This vector rotated by <paramref name="radians"/> (counter-clockwise).
    ///
    /// <para>
    /// Trigonometry comes from <see cref="DeterministicMath"/>, not the BCL: the platform libm is
    /// not guaranteed to round correctly, and this runs on the simulation path (pass and shot
    /// angle error), so a last-bit difference between two machines would break match replay.
    /// </para>
    /// </summary>
    public Vec2 Rotated(double radians)
    {
        double cos = DeterministicMath.Cos(radians);
        double sin = DeterministicMath.Sin(radians);
        return new Vec2((X * cos) - (Y * sin), (X * sin) + (Y * cos));
    }

    /// <summary>This vector shortened to at most <paramref name="maxLength"/>.</summary>
    public Vec2 ClampLength(double maxLength)
    {
        if (maxLength < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "maxLength must be >= 0.");

        double length = Length;
        return length <= maxLength ? this : this * (maxLength / length);
    }
}
