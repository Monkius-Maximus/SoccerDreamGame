using SoccerSim.Core.Persistence;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// Generating one club against the stored world (Sprint 10b): loads what
/// <see cref="ClubGenerator"/> needs, and writes the result as one undoable act. The CLI verb and
/// the HTTP endpoints both call this, so "generate a club" means one thing wherever it is asked.
///
/// <para>The club is not enrolled in any division: that is the pyramid's decision, taken with the
/// enrol action that already exists (ADR-0011 §6 moves it into the batch in Sprint 11).</para>
/// </summary>
public static class ClubCreation
{
    /// <summary>
    /// The club this request would create, without writing anything. Throws
    /// <see cref="InvalidOperationException"/> when the world lacks what generation needs (club
    /// profiles, calibration, master seed) and <see cref="ArgumentException"/> for a bad request.
    /// </summary>
    public static async Task<ClubIdentity> PreviewAsync(
        IWorldUnitOfWork unitOfWork,
        ClubGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ClubProfiles profiles = await unitOfWork.ClubProfiles.GetAsync(request.CountryId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"{request.CountryId} has no club profiles; run `worldbuilder import-club-profiles <file>` first.");

        WorldCalibration calibration = await unitOfWork.Calibration.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("No calibration is loaded; run `worldbuilder import <file>` first.");

        string seed = await unitOfWork.Settings.GetAsync(WorldMeta.MasterSeedKey, cancellationToken)
            ?? throw new InvalidOperationException("The world has no master seed; run `worldbuilder import <file>` first.");

        IReadOnlyList<GeoNode> geoNodes = await unitOfWork.GeoNodes.ListAsync(cancellationToken);
        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync(cancellationToken);

        return ClubGenerator.Generate(
            request, profiles, geoNodes, calibration, ClubGenerationContext.From(clubs), long.Parse(seed));
    }

    /// <summary>
    /// Generates the club and writes it, with a history entry taken first so undo removes it, and
    /// one edit on the trail. All in one transaction: a failure writes nothing.
    /// </summary>
    public static async Task<ClubIdentity> ApplyAsync(
        IWorldUnitOfWork unitOfWork,
        ClubGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ClubIdentity club = await PreviewAsync(unitOfWork, request, cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            long act = await WorldHistory.RecordAsync(
                unitOfWork, $"Gerar clube {club.Identity.ShortName}", cancellationToken);

            await unitOfWork.Clubs.AddAsync(club, cancellationToken);

            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(
                    WorldEntityType.Club,
                    club.ClubId,
                    "club",
                    null,
                    $"{club.Identity.OfficialName} gerado (semente {request.Seed})",
                    DateTime.UtcNow,
                    act),
                cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return club;
    }
}
