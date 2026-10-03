using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Competitions;

namespace SoccerSim.Core.World.Generation;

/// <summary>
/// Generating a whole division against the stored world (Sprint 11b): loads what
/// <see cref="DivisionGenerator"/> needs, and writes the batch as one undoable act (ADR-0011 §6).
/// The CLI verb and the HTTP endpoints both call this, the same way they share
/// <see cref="ClubCreation"/> for one club.
/// </summary>
public static class DivisionCreation
{
    /// <summary>
    /// The batch this request would create: clubs, squads and the pyramid with them enrolled,
    /// without writing anything. Throws <see cref="InvalidOperationException"/> when the world lacks
    /// what generation needs (club or squad profiles, country profile, calibration, master seed)
    /// and <see cref="ArgumentException"/> for a request the pyramid cannot hold.
    /// </summary>
    public static async Task<DivisionGenerationResult> PreviewAsync(
        IWorldUnitOfWork unitOfWork,
        DivisionGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ClubProfiles clubProfiles = await ClubCreation.LoadClubProfilesAsync(unitOfWork, request.CountryId, cancellationToken);
        WorldCalibration calibration = await ClubCreation.LoadCalibrationAsync(unitOfWork, cancellationToken);
        long masterSeed = await ClubCreation.LoadMasterSeedAsync(unitOfWork, cancellationToken);

        GenerationProfiles playerProfiles = await unitOfWork.GenerationProfiles.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No squad-generation profiles are loaded; run `worldbuilder import-profiles <file>` first.");

        CountryProfile country = await unitOfWork.Countries.GetAsync(request.CountryId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"{request.CountryId} has no country profile. A country needs a nationality "
                + "distribution before squads can be generated in it.");

        LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(request.CountryId, cancellationToken);
        IReadOnlyList<GeoNode> geoNodes = await unitOfWork.GeoNodes.ListAsync(cancellationToken);
        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync(cancellationToken);

        return DivisionGenerator.Generate(
            request, pyramid, clubProfiles, playerProfiles, country, geoNodes, calibration, clubs, masterSeed);
    }

    /// <summary>
    /// Generates the batch and writes it: every club, every squad and the pyramid, in one
    /// transaction behind one history entry, with one edit per club on the trail. A failure
    /// anywhere writes nothing, and undo takes the whole batch back.
    /// </summary>
    public static async Task<DivisionGenerationResult> ApplyAsync(
        IWorldUnitOfWork unitOfWork,
        DivisionGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        DivisionGenerationResult batch = await PreviewAsync(unitOfWork, request, cancellationToken);
        Division division = batch.Pyramid.Divisions.Single(d => d.DivisionId == request.DivisionId);
        ILookup<string, CharacterRecord> squads = batch.Characters.ToLookup(player => player.ClubId);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            long act = await WorldHistory.RecordAsync(
                unitOfWork, $"Gerar divisão {division.Name} ({batch.Clubs.Count} clubes)", cancellationToken);

            foreach (ClubIdentity club in batch.Clubs)
            {
                await unitOfWork.Clubs.AddAsync(club, cancellationToken);

                foreach (CharacterRecord player in squads[club.ClubId])
                    await unitOfWork.Characters.AddAsync(player, cancellationToken);

                await unitOfWork.Edits.RecordAsync(
                    new WorldEdit(
                        WorldEntityType.Club,
                        club.ClubId,
                        "club",
                        null,
                        $"{club.Identity.OfficialName} gerado em {division.Name} (semente {request.Seed})",
                        DateTime.UtcNow,
                        act),
                    cancellationToken);
            }

            // Upserts every division of the country. Not WorldScale.SavePyramidAsync: that opens
            // its own transaction, and this write has to be inside this one. The set of divisions
            // is the stored one, so there is nothing to delete.
            foreach (Division standing in batch.Pyramid.Divisions)
                await unitOfWork.Divisions.SaveAsync(request.CountryId, standing, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return batch;
    }
}
