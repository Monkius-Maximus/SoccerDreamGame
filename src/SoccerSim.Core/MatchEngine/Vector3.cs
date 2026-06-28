namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Minimal deterministic 3D vector (double precision). The match engine lives in the
/// engine-agnostic core, so it cannot use Godot's <c>Vector3</c>; this is the ONE vector type
/// the simulation uses. Convention: <c>Y</c> is up; the pitch lies on the <c>X</c> (length) /
/// <c>Z</c> (width) plane. Double precision is chosen over float for cleaner integration
/// determinism (a fixed operation order reproduces bit-identical trajectories).
/// </summary>
public readonly record struct Vector3(double X, double Y, double Z)
{
    public static Vector3 Zero => new(0, 0, 0);

    public static Vector3 Up => new(0, 1, 0);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3 operator -(Vector3 a) => new(-a.X, -a.Y, -a.Z);

    public static Vector3 operator *(Vector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vector3 operator *(double s, Vector3 a) => a * s;

    public static Vector3 operator /(Vector3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vector3 o) => (X * o.X) + (Y * o.Y) + (Z * o.Z);

    public Vector3 Cross(Vector3 o) => new(
        (Y * o.Z) - (Z * o.Y),
        (Z * o.X) - (X * o.Z),
        (X * o.Y) - (Y * o.X));

    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Unit vector, or <see cref="Zero"/> if this is (near) zero length.</summary>
    public Vector3 Normalized()
    {
        double len = Length;
        return len < 1e-12 ? Zero : this / len;
    }

    public Vector3 WithY(double y) => new(X, y, Z);

    /// <summary>Planar (X/Z) distance, ignoring height. Handy for "who is nearest the ball" checks.</summary>
    public double PlanarDistanceTo(Vector3 o)
    {
        double dx = X - o.X;
        double dz = Z - o.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
