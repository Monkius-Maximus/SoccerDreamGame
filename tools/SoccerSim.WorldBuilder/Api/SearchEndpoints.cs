using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Search;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>One row of the register: every competition the world holds, flat.</summary>
public sealed record RegisterRowDto(
    string CountryId,
    string? CountryName,
    /// <summary>"Divisão" for a national league with a pyramid level, "Competição" for any other.
    /// One model (ADR-0012 §2); the word is what the screen calls it.</summary>
    string Kind,
    int? Level,
    string Id,
    string Name,
    string? Format,
    int Clubs,
    int Participants,
    int? Rounds,
    int? Matches,
    /// <summary>Clubs that go up out of this league each season, and down.</summary>
    int? Up,
    int? Down);

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
        IReadOnlyDictionary<string, string> names = CountryProfiles.NamesFrom(world);
        var rows = new List<RegisterRowDto>();

        foreach (Competition competition in world.Competitions
                     .OrderBy(c => c.CountryId ?? "~", StringComparer.Ordinal)
                     .ThenBy(c => c.Level ?? int.MaxValue)
                     .ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            CompetitionSeason? season = world.Seasons.SingleOrDefault(candidate =>
                candidate.CompetitionId == competition.CompetitionId && candidate.Year == world.Meta.CurrentSeason);

            (int Up, int Down)? moves = null;
            if (competition.Level is not null)
            {
                LeaguePyramid pyramid = LeaguePyramid.Of(
                    competition.CountryId!, world.Meta.CurrentSeason, world.Competitions, world.Seasons);
                moves = pyramid.Moves(pyramid.Find(competition.CompetitionId)!);
            }

            CompetitionShape? shape = CompetitionStages.ShapeOf(competition);

            rows.Add(new RegisterRowDto(
                competition.CountryId ?? "—",
                competition.CountryId is null ? null : names.GetValueOrDefault(competition.CountryId),
                competition.Level is null ? "Competição" : "Divisão",
                competition.Level,
                competition.CompetitionId,
                competition.Name,
                competition.Stages.Count == 1 ? CompetitionStages.Label(competition.Stages[0]) : null,
                competition.ClubCount,
                season?.ParticipantClubIds.Count ?? 0,
                shape?.Rounds,
                shape?.Matches,
                moves?.Up,
                moves?.Down));
        }

        return Results.Ok(rows);
    }
}
