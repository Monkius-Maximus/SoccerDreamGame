using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Generation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>What the panel sends: the five controls, exactly as the user set them.</summary>
public sealed record SquadGenerationRequest(
    int SquadSize,
    string Formation,
    int TargetOverall,
    string AgeProfile,
    long Seed);

/// <summary>One slot of the drawn XI: who plays, where, and on which of the four lines.</summary>
public sealed record XiSlotDto(string PlayerId, Position Position, string LastName, int Overall, int Line);

/// <summary>
/// A squad the user can look at before deciding. Nothing is written to produce this — the same
/// seed will produce the same players again when they confirm.
/// </summary>
public sealed record SquadPreviewDto(
    IReadOnlyList<CharacterRecord> Squad,
    SquadMetrics Metrics,
    IReadOnlyList<XiSlotDto> StartingXi,
    /// <summary>The mean overall of the drawn XI — what the target control is aiming at, and so
    /// the number the user is really choosing.</summary>
    int XiOverall,
    IReadOnlyDictionary<Position, int> Composition,
    SquadGenerationRequest Options,
    /// <summary>How many players the club has now. Non-zero means confirming replaces them.</summary>
    int ExistingPlayers,
    /// <summary>How many of those are anchored to a real person. These are the ones whose loss
    /// actually costs research, so the warning names them separately.</summary>
    int ExistingAnchored);

public sealed record SquadGenerationResultDto(int Written, int Replaced, int PendingEdits);

/// <summary>
/// Squad generation (Sprint 5). A club without players is a dead end — the rest of the tool
/// cannot rate it, value it, or field it — so authoring a club has to be able to end with a
/// squad in it.
/// </summary>
internal static class GenerationEndpoints
{
    public static void MapGenerationApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/clubs/{clubId}/squad/options", GetOptionsAsync);
        api.MapPost("/clubs/{clubId}/squad/preview", PreviewAsync);
        api.MapPost("/clubs/{clubId}/squad/generate", GenerateAsync);
    }

    /// <summary>The defaults the panel opens with: the club's own squad size, the formation its
    /// tactical style implies, and the overall its strength predicts.</summary>
    private static async Task<IResult> GetOptionsAsync(
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity? club = await unitOfWork.Clubs.GetAsync(clubId, cancellationToken);
        if (club is null)
            return Results.NotFound();

        // A fresh seed each time the panel opens, so "generate again" means something without
        // the user having to invent a number.
        long seed = Random.Shared.NextInt64(1, 1_000_000);
        SquadGenerationOptions defaults = SquadGenerationOptions.For(club, seed);

        return Results.Ok(ToRequest(defaults));
    }

    private static async Task<IResult> PreviewAsync(
        string clubId,
        SquadGenerationRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        GenerationInputs? inputs = await LoadAsync(clubId, unitOfWork, cancellationToken);
        if (inputs is null)
            return Results.NotFound();

        SquadGenerationOptions options;
        try
        {
            options = ToOptions(request);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        IReadOnlyList<CharacterRecord> squad;
        try
        {
            squad = SquadGenerator.Generate(inputs.Club, options, inputs.Profiles, inputs.Calibration, inputs.MasterSeed, inputs.Country);
        }
        catch (InvalidOperationException ex)
        {
            // Profiles missing: an operational state with a clear remedy, not a server fault.
            return Results.BadRequest(new { error = ex.Message });
        }

        IReadOnlyList<CharacterRecord> existing =
            await unitOfWork.Characters.ListByClubAsync(clubId, cancellationToken);

        IReadOnlyList<XiSlotDto> startingXi = BuildStartingXi(squad, options.Formation);

        return Results.Ok(new SquadPreviewDto(
            Squad: squad,
            Metrics: SquadMetrics.For(squad),
            StartingXi: startingXi,
            XiOverall: (int)Math.Round(startingXi.Average(slot => slot.Overall), MidpointRounding.AwayFromZero),
            Composition: SquadShape.CompositionFor(options.SquadSize, options.Formation),
            Options: ToRequest(options),
            ExistingPlayers: existing.Count,
            ExistingAnchored: existing.Count(player => player.Provenance == Provenance.Anchored)));
    }

    /// <summary>
    /// Writes the generated squad, replacing whatever the club had. Destructive on purpose and
    /// only on an explicit request: the preview is where the user decides, and the panel warns
    /// when anchored players are among the casualties.
    /// </summary>
    private static async Task<IResult> GenerateAsync(
        string clubId,
        SquadGenerationRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        GenerationInputs? inputs = await LoadAsync(clubId, unitOfWork, cancellationToken);
        if (inputs is null)
            return Results.NotFound();

        SquadGenerationOptions options;
        IReadOnlyList<CharacterRecord> squad;
        try
        {
            options = ToOptions(request);
            squad = SquadGenerator.Generate(inputs.Club, options, inputs.Profiles, inputs.Calibration, inputs.MasterSeed, inputs.Country);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        IReadOnlyList<CharacterRecord> existing =
            await unitOfWork.Characters.ListByClubAsync(clubId, cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await WorldHistory.RecordAsync(
                unitOfWork, $"Gerar elenco de {inputs.Club.Identity.ShortName}", cancellationToken);

            foreach (CharacterRecord player in existing)
                await unitOfWork.Characters.DeleteAsync(player.PlayerId, cancellationToken);

            foreach (CharacterRecord player in squad)
                await unitOfWork.Characters.AddAsync(player, cancellationToken);

            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(
                    WorldEntityType.Club,
                    clubId,
                    "squad",
                    existing.Count == 0 ? null : $"{existing.Count} jogadores",
                    $"{squad.Count} jogadores gerados (semente {options.Seed})",
                    DateTime.UtcNow),
                cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(new SquadGenerationResultDto(
            Written: squad.Count,
            Replaced: existing.Count,
            PendingEdits: await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    /// <summary>The eleven the formation names, taken as the best available in each position —
    /// the same order the panel draws them in, four lines from keeper to attack.</summary>
    private static IReadOnlyList<XiSlotDto> BuildStartingXi(
        IReadOnlyList<CharacterRecord> squad,
        Formation formation)
    {
        var remaining = squad.OrderByDescending(player => player.Overall).ToList();
        var xi = new List<XiSlotDto>();

        foreach ((Position position, int needed) in SquadShape.Starters(formation).OrderBy(entry => SquadShape.LineOf(entry.Key)))
        {
            for (int i = 0; i < needed; i++)
            {
                CharacterRecord? pick = remaining.FirstOrDefault(player => player.PrimaryPosition == position);
                if (pick is null)
                    continue;

                remaining.Remove(pick);
                xi.Add(new XiSlotDto(pick.PlayerId, position, pick.LastName, pick.Overall, SquadShape.LineOf(position)));
            }
        }

        return xi;
    }

    private sealed record GenerationInputs(
        ClubIdentity Club,
        GenerationProfiles Profiles,
        WorldCalibration Calibration,
        long MasterSeed,
        CountryProfile Country);

    private static async Task<GenerationInputs?> LoadAsync(
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity? club = await unitOfWork.Clubs.GetAsync(clubId, cancellationToken);
        if (club is null)
            return null;

        WorldCalibration calibration = await unitOfWork.Calibration.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("No calibration is loaded; run `worldbuilder import <file>` first.");

        // An empty profile set produces a clear 400 from the generator rather than a null here.
        GenerationProfiles profiles = await unitOfWork.GenerationProfiles.GetAsync(cancellationToken)
            ?? new GenerationProfiles(new Dictionary<Position, IReadOnlyDictionary<Attr, AttributeProfile>>(), [], []);

        string? seed = await unitOfWork.Settings.GetAsync(WorldMeta.MasterSeedKey, cancellationToken);

        // No profile for the club's country means the tool has never been told where its players
        // come from. The generator refuses rather than inventing, and this is where that starts.
        CountryProfile country = await unitOfWork.Countries.GetAsync(club.Geography.CountryId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"{club.Geography.CountryId} has no country profile. A country needs a nationality "
                + "distribution before squads can be generated in it.");

        return new GenerationInputs(club, profiles, calibration, seed is null ? 0 : long.Parse(seed), country);
    }

    private static SquadGenerationOptions ToOptions(SquadGenerationRequest request)
    {
        if (!TryParseFormation(request.Formation, out Formation formation))
        {
            throw new ArgumentException(
                $"'{request.Formation}' is not a formation (allowed: 4-3-3, 4-2-3-1, 4-4-2, 3-5-2).");
        }

        if (!Enum.TryParse(request.AgeProfile, ignoreCase: false, out AgeProfile ageProfile) || !Enum.IsDefined(ageProfile))
        {
            throw new ArgumentException(
                $"'{request.AgeProfile}' is not an age profile (allowed: {string.Join(", ", Enum.GetNames<AgeProfile>())}).");
        }

        if (request.SquadSize < SquadShape.MinSquadSize || request.SquadSize > SquadShape.MaxSquadSize)
        {
            throw new ArgumentException(
                $"Squad size {request.SquadSize} is outside {SquadShape.MinSquadSize}–{SquadShape.MaxSquadSize}.");
        }

        return new SquadGenerationOptions(request.SquadSize, formation, request.TargetOverall, ageProfile, request.Seed);
    }

    private static bool TryParseFormation(string label, out Formation formation)
    {
        foreach (Formation candidate in Enum.GetValues<Formation>())
        {
            if (SquadShape.Label(candidate) == label)
            {
                formation = candidate;
                return true;
            }
        }

        formation = default;
        return false;
    }

    private static SquadGenerationRequest ToRequest(SquadGenerationOptions options) => new(
        options.SquadSize,
        SquadShape.Label(options.Formation),
        options.TargetOverall,
        options.AgeProfile.ToString(),
        options.Seed);
}
