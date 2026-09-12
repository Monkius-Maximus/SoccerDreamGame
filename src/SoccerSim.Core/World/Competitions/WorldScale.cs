using System.Text.Json;
using System.Text.Json.Serialization;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// The part of the world that is not in the world document: what the tool knows about countries
/// and what pyramids they run. It sits apart because the pilot batch predates both — the exported
/// document describes a world with one country and one competition, and widening that format to
/// carry structure nobody authored yet would change a contract for nothing.
/// </summary>
public sealed record WorldScale(
    IReadOnlyList<CountryProfile> Countries,
    IReadOnlyList<LeaguePyramid> Pyramids)
{
    public static WorldScale Empty { get; } = new([], []);

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Format);

    public static WorldScale FromJson(string json) =>
        JsonSerializer.Deserialize<WorldScale>(json, Format) ?? Empty;

    // -------------------------------------------------------------- persistence

    public static async Task<WorldScale> LoadAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CountryProfile> countries = await unitOfWork.Countries.ListAsync(cancellationToken);
        var pyramids = new List<LeaguePyramid>();

        foreach (CountryProfile country in countries)
            pyramids.Add(await unitOfWork.Divisions.GetPyramidAsync(country.CountryId, cancellationToken));

        return new WorldScale(countries, pyramids);
    }

    /// <summary>
    /// Replaces the stored countries and divisions with these, in one transaction. Replace rather
    /// than merge, for the same reason the world itself replaces: this is what undo writes, and a
    /// merge would leave behind exactly the row the user asked to take back.
    /// </summary>
    public static async Task ReplaceAsync(
        IWorldUnitOfWork unitOfWork,
        WorldScale scale,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CountryProfile> existing = await unitOfWork.Countries.ListAsync(cancellationToken);
        IReadOnlyList<Division> divisions = await unitOfWork.Divisions.ListAsync(cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (Division division in divisions)
                await unitOfWork.Divisions.DeleteAsync(division.DivisionId, cancellationToken);

            foreach (CountryProfile country in existing)
                await unitOfWork.Countries.DeleteAsync(country.CountryId, cancellationToken);

            foreach (CountryProfile country in scale.Countries)
                await unitOfWork.Countries.SaveAsync(country, cancellationToken);

            foreach (LeaguePyramid pyramid in scale.Pyramids)
            {
                foreach (Division division in pyramid.Divisions)
                    await unitOfWork.Divisions.SaveAsync(pyramid.CountryId, division, cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Writes one country's pyramid: divisions that are gone are deleted, the rest are upserted,
    /// all in one transaction. Scoped rather than a whole-scale replace, because an enrolment is
    /// one country's business and rewriting every other country's rows to record it would make a
    /// single click depend on data it never touched.
    /// </summary>
    public static async Task SavePyramidAsync(
        IWorldUnitOfWork unitOfWork,
        LeaguePyramid pyramid,
        CancellationToken cancellationToken = default)
    {
        LeaguePyramid stored = await unitOfWork.Divisions.GetPyramidAsync(pyramid.CountryId, cancellationToken);
        var keeping = pyramid.Divisions.Select(division => division.DivisionId).ToHashSet(StringComparer.Ordinal);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (Division division in stored.Divisions.Where(d => !keeping.Contains(d.DivisionId)))
                await unitOfWork.Divisions.DeleteAsync(division.DivisionId, cancellationToken);

            foreach (Division division in pyramid.Divisions)
                await unitOfWork.Divisions.SaveAsync(pyramid.CountryId, division, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
