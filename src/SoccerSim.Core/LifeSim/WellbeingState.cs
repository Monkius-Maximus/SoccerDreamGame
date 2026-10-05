namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The mutable off-pitch state of the human's career: six 0–100 need gauges plus the role whose
/// <see cref="NeedProfile"/> governs them.
///
/// <para>
/// Mirrors the shape already used by <see cref="SoccerSim.Core.Domain.Player"/>: a mutable entity
/// that the simulation writes to, paired with an immutable derived read model
/// (<see cref="WellbeingSnapshot"/>) that every consumer reads instead of poking at raw values.
/// Values are stored in a need-indexed array so a day's advance allocates nothing and always
/// iterates in the same order — a hard requirement for a save that must replay from its seed.
/// </para>
/// </summary>
public sealed class WellbeingState
{
    /// <summary>Where a freshly created career starts: comfortable but with clear headroom.</summary>
    public const double DefaultStartValue = 70.0;

    private readonly double[] _values;

    private WellbeingState(CareerRole role, double[] values)
    {
        Role = role;
        Profile = NeedProfile.For(role);
        _values = values;
    }

    /// <summary>Which career this state belongs to; selects the <see cref="Profile"/>.</summary>
    public CareerRole Role { get; }

    /// <summary>The role-dependent decay/weight tuning applied to these needs.</summary>
    public NeedProfile Profile { get; }

    /// <summary>Current value of a need, always within 0–100.</summary>
    public double this[NeedKind need] => _values[(int)need];

    /// <summary>A new career state with every need at <paramref name="startValue"/>.</summary>
    public static WellbeingState CreateDefault(CareerRole role, double startValue = DefaultStartValue)
    {
        if (double.IsNaN(startValue) || startValue is < 0.0 or > 100.0)
            throw new ArgumentOutOfRangeException(nameof(startValue), startValue, "Need values are 0–100.");

        var values = new double[Needs.All.Length];
        Array.Fill(values, startValue);
        return new WellbeingState(role, values);
    }

    /// <summary>
    /// Rehydrate from persistence. Every need must be present: a partially saved state would let a
    /// missing gauge silently default to zero and read as a critical deficit the player never earned.
    /// </summary>
    public static WellbeingState FromValues(CareerRole role, IReadOnlyDictionary<NeedKind, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var restored = new double[Needs.All.Length];
        foreach (NeedKind need in Needs.All)
        {
            if (!values.TryGetValue(need, out double value))
                throw new InvalidOperationException($"Persisted wellbeing state is missing need '{need}'.");
            restored[(int)need] = Clamp(value, need);
        }

        return new WellbeingState(role, restored);
    }

    /// <summary>Overwrite a need, clamped to 0–100.</summary>
    public void Set(NeedKind need, double value) => _values[(int)need] = Clamp(value, need);

    /// <summary>Add a (possibly negative) delta to a need, clamped to 0–100.</summary>
    public void Adjust(NeedKind need, double delta)
    {
        if (double.IsNaN(delta))
            throw new ArgumentException($"Delta for need '{need}' was NaN.", nameof(delta));
        _values[(int)need] = Clamp(_values[(int)need] + delta, need);
    }

    /// <summary>The qualitative band a need currently sits in.</summary>
    public NeedBand BandOf(NeedKind need) => Needs.BandOf(_values[(int)need]);

    /// <summary>An independent copy — used to preview an activity without committing it.</summary>
    public WellbeingState Clone() => new(Role, (double[])_values.Clone());

    /// <summary>Flatten for persistence. Ordering follows <see cref="Needs.All"/>.</summary>
    public IReadOnlyDictionary<NeedKind, double> ToDictionary()
    {
        var map = new Dictionary<NeedKind, double>(Needs.All.Length);
        foreach (NeedKind need in Needs.All)
            map[need] = _values[(int)need];
        return map;
    }

    private static double Clamp(double value, NeedKind need)
    {
        if (double.IsNaN(value))
            throw new ArgumentException($"Value for need '{need}' was NaN.", nameof(value));
        return Math.Clamp(value, 0.0, 100.0);
    }
}
