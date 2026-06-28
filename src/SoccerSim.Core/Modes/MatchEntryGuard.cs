using SoccerSim.Core.Domain;

namespace SoccerSim.Core.Modes;

/// <summary>
/// Fail-fast preconditions for entering a rendered match. Pure — it takes a club-existence
/// probe rather than reaching into persistence — so it is unit-tested directly and reused by
/// the mode manager. These mirror the Match Engine's entry contract: you cannot render a null,
/// already-played, or malformed fixture, nor one whose clubs are not part of the world.
/// </summary>
public static class MatchEntryGuard
{
    public static void Validate(Match? fixture, Func<int, bool> clubExists)
    {
        ArgumentNullException.ThrowIfNull(clubExists);

        if (fixture is null)
            throw new ArgumentNullException(nameof(fixture), "Cannot enter a match: fixture is null.");
        if (fixture.Played)
            throw new InvalidOperationException($"Cannot enter match {fixture.Id}: it is already played/resolved.");
        if (fixture.HomeTeamId == fixture.AwayTeamId)
            throw new InvalidOperationException(
                $"Cannot enter match {fixture.Id}: home and away club are the same ({fixture.HomeTeamId}).");
        if (!clubExists(fixture.HomeTeamId))
            throw new InvalidOperationException(
                $"Cannot enter match {fixture.Id}: home club {fixture.HomeTeamId} does not exist in the world.");
        if (!clubExists(fixture.AwayTeamId))
            throw new InvalidOperationException(
                $"Cannot enter match {fixture.Id}: away club {fixture.AwayTeamId} does not exist in the world.");
    }
}
