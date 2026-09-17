using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Search;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>One row of the register: every competition and division the world holds, flat.</summary>
public sealed record RegisterRowDto(
    string CountryId,
    string? CountryName,
    /// <summary>"Divisão" for a standing division of a pyramid, "Competição" for an authored
    /// edition. The two are different things (docs/adr/0008) and the register says which.</summary>
    string Kind,
    int? Tier,
    string Id,
    string Name,
    string Format,
    int Clubs,
    int? Rounds,
    int? Matches,
    int? PromotedIn,
    int? RelegatedOut);

/// <summary>
/// The global search and the register (ROADMAP.md Sprint 9).
///
/// <para>Both exist because the tool grew past one screen. The rail filters clubs, which is right
/// for the screen it sits on and useless when what you remember is half a player's surname or the
/// key of a calibration constant. The register is the pyramid's other surface: the same data as a
/// dense table, for reading across countries rather than down one.</para>
/// </summary>
internal static class SearchEndpoints
{
    public static void MapSearchApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/search", SearchAsync);
        api.MapGet("/register", RegisterAsync);
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        IReadOnlyList<CountryProfile> countries = await unitOfWork.Countries.ListAsync(cancellationToken);

        // A query too short to mean anything answers "nothing" rather than 400: the box is typed
        // into one character at a time, and an error on the way to a real query is noise.
        return Results.Ok(WorldSearch.Search(world, countries, q ?? string.Empty));
    }

    private static async Task<IResult> RegisterAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        IReadOnlyList<CountryProfile> countries = await unitOfWork.Countries.ListAsync(cancellationToken);

        IReadOnlyDictionary<string, string> names = CountryProfiles.NamesFrom(world);
        var rows = new List<RegisterRowDto>();

        foreach (CountryProfile country in countries.OrderBy(c => c.CountryId, StringComparer.Ordinal))
        {
            LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(country.CountryId, cancellationToken);

            foreach (Division division in pyramid.Divisions.OrderBy(d => d.Tier))
            {
                rows.Add(new RegisterRowDto(
                    country.CountryId,
                    names.GetValueOrDefault(country.CountryId),
                    "Divisão",
                    division.Tier,
                    division.DivisionId,
                    division.Name,
                    CompetitionFormats.Label(division.Format),
                    division.ClubCount,
                    division.Shape?.Rounds,
                    division.Shape?.Matches,
                    division.PromotedIn,
                    division.RelegatedOut));
            }
        }

        // The authored competitions sit alongside, not inside: a Competition is a frozen edition
        // and a Division is the standing structure editions hang off. Showing them as one kind
        // would be the tool asserting they are the same thing.
        foreach (Competition competition in world.Competitions.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            string countryId = CountryOf(world, competition) ?? "—";

            rows.Add(new RegisterRowDto(
                countryId,
                names.GetValueOrDefault(countryId),
                "Competição",
                null,
                competition.CompetitionId,
                competition.Name,
                competition.Format,
                competition.ClubCount,
                competition.Rounds,
                null,
                competition.PromotedIn,
                competition.RelegatedOut));
        }

        return Results.Ok(rows);
    }

    /// <summary>A competition names a geo node, not a country code; the clubs anchored there are
    /// what connect the two.</summary>
    private static string? CountryOf(WorldSnapshot world, Competition competition) =>
        world.Clubs
            .FirstOrDefault(club => club.Geography.GeoNodeId == competition.AnchorGeoNodeId)
            ?.Geography.CountryId
        ?? world.Clubs.FirstOrDefault()?.Geography.CountryId;
}
