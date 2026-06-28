namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Deterministic ball aerodynamics and ground interaction — the centrepiece of the match engine.
/// Pure double math integrated with semi-implicit (symplectic) Euler, so a fixed tick order
/// reproduces a trajectory bit-for-bit. Coordinate system: <c>Y</c> is up, gravity acts along
/// <c>-Y</c>, the pitch surface is the plane <c>Y = 0</c>, and the ball rests with its centre at
/// <c>Y = Radius</c>.
///
/// <para>
/// The constants below are plausible STARTING values to be calibrated by game feel (not CFD
/// realism). Drag + Magnus together reproduce the validation target — a struck ball with side
/// spin leaves almost straight and curves laterally as it slows.
/// </para>
/// </summary>
public static class BallPhysics
{
    /// <summary>Ball mass (kg). Regulation ~0.43.</summary>
    public const double Mass = 0.43;

    /// <summary>Ball radius (m). Regulation ~0.11.</summary>
    public const double Radius = 0.11;

    /// <summary>Gravitational acceleration (m/s^2).</summary>
    public const double Gravity = 9.81;

    /// <summary>Air density rho (kg/m^3) near sea level.</summary>
    public const double AirDensity = 1.2;

    /// <summary>Drag coefficient Cd.</summary>
    public const double DragCoefficient = 0.25;

    /// <summary>Magnus coefficient Cm. Small so spin curves the ball without overpowering it.</summary>
    public const double MagnusCoefficient = 0.0002;

    /// <summary>Coefficient of restitution for a dry pitch (fraction of vertical speed kept per bounce).</summary>
    public const double Restitution = 0.6;

    /// <summary>Horizontal velocity lost per ground contact on a dry pitch (rolling/sliding friction).</summary>
    public const double GroundFriction = 0.10;

    /// <summary>Fraction of spin shed per second of flight.</summary>
    public const double SpinDecayPerSecond = 0.3;

    /// <summary>Cross-sectional area A = pi * r^2 (m^2), used by the drag term.</summary>
    public static readonly double Area = Math.PI * Radius * Radius;

    /// <summary>
    /// Advance the ball by one fixed tick under gravity + drag + Magnus + wind, then resolve any
    /// ground contact. Mutates <paramref name="ball"/> in place.
    /// </summary>
    public static void Step(BallState ball, MatchConditions conditions, double dt)
    {
        if (ball is null)
            throw new ArgumentNullException(nameof(ball));
        if (dt <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dt), $"dt must be > 0, was {dt}.");

        // Air-relative velocity: subtract wind so a tailwind eases drag and a headwind adds to it.
        Vector3 airVelocity = ball.Velocity - conditions.Wind;
        double airSpeed = airVelocity.Length;

        // Start with gravity, then add the aerodynamic forces.
        Vector3 force = new(0, -Mass * Gravity, 0);

        if (airSpeed > 1e-9)
        {
            // Drag: F_d = -0.5 * rho * Cd * A * |v| * v   (opposes air-relative motion, grows with v^2).
            double dragMagnitude = 0.5 * AirDensity * DragCoefficient * Area * airSpeed;
            force -= airVelocity * dragMagnitude;

            // Magnus: F_m = Cm * rho * (spin x v)   (perpendicular to both → side spin curves,
            // backspin lifts, topspin dips).
            force += ball.Spin.Cross(airVelocity) * (MagnusCoefficient * AirDensity);
        }

        Vector3 acceleration = force / Mass;

        // Semi-implicit Euler: integrate velocity first, then position with the new velocity.
        ball.Velocity += acceleration * dt;
        ball.Position += ball.Velocity * dt;

        // Spin bleeds off over time.
        ball.Spin *= Math.Max(0.0, 1.0 - (SpinDecayPerSecond * dt));

        ResolveGround(ball, conditions);
    }

    /// <summary>Bounce + friction when the ball reaches the surface, modulated by rain and pitch wear.</summary>
    private static void ResolveGround(BallState ball, MatchConditions conditions)
    {
        if (ball.Position.Y >= Radius)
            return;

        double wetness = Math.Clamp(conditions.RainIntensity, 0.0, 1.0);
        double wear = Math.Clamp(conditions.PitchWear, 0.0, 1.0);

        // Wet pitches retain less energy (skiddy, lower bounce); worn pitches grip more (rougher),
        // wet pitches grip less. All calibratable.
        double restitution = Restitution * (1.0 - (0.4 * wetness));
        double friction = Math.Clamp(GroundFriction * (1.0 + (0.5 * wear) - (0.3 * wetness)), 0.0, 1.0);

        Vector3 v = ball.Velocity;
        ball.Velocity = new Vector3(
            v.X * (1.0 - friction),
            -v.Y * restitution,
            v.Z * (1.0 - friction));
        ball.Position = ball.Position.WithY(Radius);
    }
}
