using SoccerSim.Core.Events;

namespace SoccerSim.Core.Random;

/// <summary>
/// A fully deterministic, cross-platform pseudo-random source. Extends the minimal
/// <see cref="IRandom"/> seam used across the simulation with the wider integer surface
/// (raw 64-bit draws and bounded ints) that hierarchical, per-entity streams need.
///
/// <para>
/// Determinism is a hard requirement: the same seed MUST produce the same sequence on every
/// platform and every .NET version. That is exactly why simulation code never touches
/// <see cref="System.Random"/>, whose sequence is an unspecified implementation detail.
/// </para>
/// </summary>
public interface IDeterministicRandom : IRandom
{
    /// <summary>The next raw 64-bit draw.</summary>
    ulong NextULong();

    /// <summary>A uniformly distributed int in <c>[minInclusive, maxExclusive)</c>.</summary>
    int NextInt(int minInclusive, int maxExclusive);
}
