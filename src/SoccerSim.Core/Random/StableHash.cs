namespace SoccerSim.Core.Random;

/// <summary>
/// A stable 64-bit hash of a string, for deriving stream keys such as
/// <c>DeterministicRng.CreateStream(masterSeed, StableHash.Of(clubId), StableHash.Of("squad"))</c>.
/// FNV-1a: tiny, and fixed forever — unlike <see cref="string.GetHashCode()"/>, which is
/// randomised per process and would make every run produce a different world. Shared by every
/// generator so a stream key means the same thing everywhere (ADR-0011 §1).
/// </summary>
public static class StableHash
{
    public static ulong Of(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offsetBasis;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
