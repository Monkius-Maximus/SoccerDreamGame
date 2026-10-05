using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Import;

namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// Reads a country's pyramid for the current season out of the stored competitions, and writes
/// an edited one back. The pyramid itself is never stored (ADR-0012 §6): writing it means writing
/// the competitions and seasons it was drawn from.
/// </summary>
public static class PyramidStore
{
    public static async Task<LeaguePyramid> LoadAsync(
        IWorldUnitOfWork unitOfWork,
        string countryId,
        CancellationToken cancellationToken = default) =>
        LeaguePyramid.Of(
            countryId,
            await WorldStore.CurrentSeasonAsync(unitOfWork, cancellationToken),
            await unitOfWork.Competitions.ListAsync(cancellationToken),
            await unitOfWork.Seasons.ListAsync(cancellationToken));

    /// <summary>
    /// Writes <paramref name="after"/> over <paramref name="before"/>: levels that are gone are
    /// deleted (their seasons and rules with them), the rest are written in level order. Opens no
    /// transaction — the caller's act (history entry, generation) and this write must commit or
    /// roll back together, and a rule may name a level written later in the same transaction.
    ///
    /// <para>Deletes come first and levels are written top-down, so a level moving up into a freed
    /// slot never meets <c>UNIQUE (CountryId, Level)</c> on its way.</para>
    /// </summary>
    public static async Task WriteAsync(
        IWorldUnitOfWork unitOfWork,
        LeaguePyramid before,
        LeaguePyramid after,
        CancellationToken cancellationToken = default)
    {
        var kept = after.Levels.Select(level => level.Competition.CompetitionId).ToHashSet(StringComparer.Ordinal);
        var existing = before.Levels.Select(level => level.Competition.CompetitionId).ToHashSet(StringComparer.Ordinal);

        foreach (PyramidLevel gone in before.Levels.Where(level => !kept.Contains(level.Competition.CompetitionId)))
            await unitOfWork.Competitions.DeleteAsync(gone.Competition.CompetitionId, cancellationToken);

        foreach (PyramidLevel level in after.Levels.OrderBy(level => level.Level))
        {
            if (existing.Contains(level.Competition.CompetitionId))
                await unitOfWork.Competitions.UpdateAsync(level.Competition, cancellationToken);
            else
                await unitOfWork.Competitions.AddAsync(level.Competition, cancellationToken);

            await unitOfWork.Seasons.SaveAsync(level.Season, cancellationToken);
        }
    }
}
