using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Validation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>
/// The read surface for Sprint 3: enough to render the club rail and a complete club page.
/// Every endpoint is a thin adapter — read through the ports, compute with Core, return a DTO.
/// No domain logic lives here (ADR-0001: "Lógica em Core, HTTP fino").
/// </summary>
internal static class WorldEndpoints
{
    public static void MapWorldApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/world", GetWorldAsync);
        api.MapGet("/clubs", GetClubsAsync);
        api.MapGet("/clubs/{clubId}", GetClubAsync);
        api.MapGet("/clubs/{clubId}/squad", GetSquadAsync);
        api.MapGet("/geo", GetGeoAsync);
        api.MapGet("/competitions/{competitionId}", GetCompetitionAsync);
    }

    /// <summary>
    /// What is loaded, plus the two things the shell needs before it can render anything: the
    /// closed enums (filter options, position order) and the calibration.
    /// <c>loaded: false</c> is a normal answer, not an error — a fresh database has a schema and
    /// no world, and the UI explains how to import one.
    /// </summary>
    private static async Task<WorldSummaryDto> GetWorldAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        IReadOnlyList<GeoNode> geoNodes = await unitOfWork.GeoNodes.ListAsync(cancellationToken);
        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync(cancellationToken);
        IReadOnlyList<CharacterRecord> characters = await unitOfWork.Characters.ListAsync(cancellationToken);
        IReadOnlyList<Competition> competitions = await unitOfWork.Competitions.ListAsync(cancellationToken);
        IReadOnlyList<WorldSource> sources = await unitOfWork.Sources.ListAsync(cancellationToken);
        WorldCalibration? calibration = await unitOfWork.Calibration.GetAsync(cancellationToken);

        return new WorldSummaryDto(
            SchemaVersion: "ClubIdentity v2",
            Loaded: clubs.Count > 0,
            Counts: new WorldCountsDto(clubs.Count, characters.Count, geoNodes.Count, competitions.Count, sources.Count),
            Enums: ClosedEnums,
            Calibration: calibration);
    }

    private static async Task<IReadOnlyList<ClubListItemDto>> GetClubsAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync(cancellationToken);
        if (clubs.Count == 0)
            return [];

        WorldCalibration calibration = await RequireCalibrationAsync(unitOfWork, cancellationToken);

        // One read of every character, grouped once: the rail shows a squad overall per club, and
        // 20 separate squad queries to build one list would be 20 round trips for one screen.
        ILookup<string, CharacterRecord> squads =
            (await unitOfWork.Characters.ListAsync(cancellationToken)).ToLookup(character => character.ClubId);

        return clubs
            .Select(club => new ClubListItemDto(
                ClubId: club.ClubId,
                DisplayCode: club.DisplayCode,
                ShortName: club.Identity.ShortName,
                OfficialName: club.Identity.OfficialName,
                CityName: club.Geography.CityName,
                PrestigeBand: club.World.PrestigeBand,
                PrimaryColor: club.Palette.Primary,
                SquadOverall: SquadMetrics.For(squads[club.ClubId].ToList()).Overall,
                InvariantLevel: ClubInvariants.WorstLevel(ClubInvariants.Check(club, calibration))))
            .ToList();
    }

    private static async Task<IResult> GetClubAsync(
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity? club = await unitOfWork.Clubs.GetAsync(clubId, cancellationToken);
        if (club is null)
            return Results.NotFound();

        WorldCalibration calibration = await RequireCalibrationAsync(unitOfWork, cancellationToken);
        IReadOnlyList<Finding> findings = ClubInvariants.Check(club, calibration);
        IReadOnlyList<CharacterRecord> squad = await unitOfWork.Characters.ListByClubAsync(clubId, cancellationToken);

        string? derbyRivalName = null;
        if (club.AiProfile.DerbyRivalClubId is { } rivalId)
        {
            ClubIdentity? rival = await unitOfWork.Clubs.GetAsync(rivalId, cancellationToken);
            derbyRivalName = rival?.Identity.ShortName;
        }

        calibration.StadiumProfile.TryGetValue(club.Geography.CountryId, out StadiumProfileEntry? profile);

        return Results.Ok(new ClubPageDto(
            Club: club,
            Version: await unitOfWork.Clubs.GetVersionAsync(clubId, cancellationToken),
            GeoPath: await BuildGeoPathAsync(club.Geography.GeoNodeId, unitOfWork, cancellationToken),
            Findings: findings,
            InvariantLevel: ClubInvariants.WorstLevel(findings),
            Metrics: SquadMetrics.For(squad),
            DerbyRivalName: derbyRivalName,
            StadiumProfile: profile,
            BandValueMult: calibration.Bands[club.World.PrestigeBand].ValueMult));
    }

    private static async Task<IResult> GetSquadAsync(
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await unitOfWork.Clubs.GetAsync(clubId, cancellationToken) is null)
            return Results.NotFound();

        return Results.Ok(await unitOfWork.Characters.ListByClubAsync(clubId, cancellationToken));
    }

    private static async Task<IReadOnlyList<GeoNode>> GetGeoAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        await unitOfWork.GeoNodes.ListAsync(cancellationToken);

    private static async Task<IResult> GetCompetitionAsync(
        string competitionId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        Competition? competition = await unitOfWork.Competitions.GetAsync(competitionId, cancellationToken);
        return competition is null ? Results.NotFound() : Results.Ok(competition);
    }

    /// <summary>Walks up the geo tree to build the breadcrumb ("Mundo › CONMEBOL › Brasil › … › Cidade").</summary>
    private static async Task<IReadOnlyList<string>> BuildGeoPathAsync(
        string geoNodeId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        Dictionary<string, GeoNode> byId = (await unitOfWork.GeoNodes.ListAsync(cancellationToken))
            .ToDictionary(node => node.GeoNodeId);

        var path = new List<string>();
        string? current = geoNodeId;
        while (current is not null && byId.TryGetValue(current, out GeoNode? node))
        {
            path.Insert(0, node.DisplayName);
            current = node.ParentId;
        }

        return path;
    }

    private static async Task<WorldCalibration> RequireCalibrationAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        await unitOfWork.Calibration.GetAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "No calibration is loaded. Run `worldbuilder import <file>` before serving the tool — "
            + "invariants and squad metrics cannot be computed without it.");

    /// <summary>
    /// The closed enums, straight from the C# types, so the UI's filter options and position
    /// ordering cannot drift from the schema.
    ///
    /// <para>
    /// Every enum a field catalog names must be here: the form builds its options from this
    /// dictionary, and a missing entry renders an empty select that silently cannot be set.
    /// <c>FieldCatalogEnumTests</c> pins that.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedEnums =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [nameof(PreferredFoot)] = Enum.GetNames<PreferredFoot>(),
            [nameof(TacticalStyleProvenance)] = Enum.GetNames<TacticalStyleProvenance>(),
            [nameof(Position)] = Enum.GetNames<Position>(),
            [nameof(Attr)] = Enum.GetNames<Attr>(),
            [nameof(PrestigeBand)] = Enum.GetNames<PrestigeBand>(),
            [nameof(SquadRole)] = Enum.GetNames<SquadRole>(),
            [nameof(Phase)] = Enum.GetNames<Phase>(),
            [nameof(DistrictArchetype)] = Enum.GetNames<DistrictArchetype>(),
            [nameof(ShieldShape)] = Enum.GetNames<ShieldShape>(),
            [nameof(AtmosphereArchetype)] = Enum.GetNames<AtmosphereArchetype>(),
            [nameof(PitchSurface)] = Enum.GetNames<PitchSurface>(),
            [nameof(TacticalStyle)] = Enum.GetNames<TacticalStyle>(),
            [nameof(TypographyStyle)] = Enum.GetNames<TypographyStyle>(),
            [nameof(CollarStyle)] = Enum.GetNames<CollarStyle>(),
            [nameof(FitStyle)] = Enum.GetNames<FitStyle>(),
            [nameof(FabricPattern)] = Enum.GetNames<FabricPattern>(),
            [nameof(NamingRule)] = Enum.GetNames<NamingRule>(),
            [nameof(CompetitionScope)] = Enum.GetNames<CompetitionScope>(),
            [nameof(GeoNodeKind)] = Enum.GetNames<GeoNodeKind>(),
            [nameof(BuildType)] = Enum.GetNames<BuildType>(),
            [nameof(Provenance)] = Enum.GetNames<Provenance>(),
        };
}
