namespace SoccerSim.Core.World.Generation;

/// <summary>The four formations the generator lays out an XI in (ALGORITHMS.md §6.2).</summary>
public enum Formation
{
    F433,
    F4231,
    F442,
    F352,
}

/// <summary>Shifts every age band by a few years without changing anything else.</summary>
public enum AgeProfile
{
    /// <summary>Ages as observed.</summary>
    Balanced,

    /// <summary>Three years younger across the board.</summary>
    Young,

    /// <summary>Three years older across the board.</summary>
    Experienced,
}

/// <summary>
/// What the user chooses in the generator panel. <see cref="Seed"/> is the whole determinism
/// contract: the same seed against the same club and the same profiles produces the same squad,
/// player for player.
/// </summary>
public sealed record SquadGenerationOptions(
    int SquadSize,
    Formation Formation,
    int TargetOverall,
    AgeProfile AgeProfile,
    long Seed)
{
    /// <summary>
    /// The defaults for a club: the squad size it declares, the formation its tactical style
    /// implies, and the overall its strength predicts.
    /// </summary>
    public static SquadGenerationOptions For(ClubIdentity club, long seed) => new(
        SquadSize: club.World.SquadSize,
        Formation: SquadShape.DefaultFormationFor(club.AiProfile.DefaultTacticalStyle),
        TargetOverall: SquadShape.TargetOverallFor(club.World.ClubStrength),
        AgeProfile: AgeProfile.Balanced,
        Seed: seed);
}
