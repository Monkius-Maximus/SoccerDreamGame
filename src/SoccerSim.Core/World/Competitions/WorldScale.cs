using System.Text.Json;
using System.Text.Json.Serialization;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// The part of the world that is not in the world document: what the tool knows about countries.
/// It sits apart because the pilot batch predates countries — the exported document describes
/// clubs and competitions, and a country profile is the tool's own statement about a place.
///
/// <para>Divisions used to live here too, only because the document had no place for them. They
/// are competitions now, and competitions are in the document (ADR-0012 §10).</para>
/// </summary>
public sealed record WorldScale(IReadOnlyList<CountryProfile> Countries)
{
    public static WorldScale Empty { get; } = new([]);

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
        CancellationToken cancellationToken = default) =>
        new(await unitOfWork.Countries.ListAsync(cancellationToken));

    /// <summary>
    /// Replaces the stored countries with these, in one transaction. Replace rather than merge,
    /// for the same reason the world itself replaces: this is what undo writes, and a merge would
    /// leave behind exactly the row the user asked to take back.
    /// </summary>
    public static async Task ReplaceAsync(
        IWorldUnitOfWork unitOfWork,
        WorldScale scale,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CountryProfile> existing = await unitOfWork.Countries.ListAsync(cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (CountryProfile country in existing)
                await unitOfWork.Countries.DeleteAsync(country.CountryId, cancellationToken);

            foreach (CountryProfile country in scale.Countries)
                await unitOfWork.Countries.SaveAsync(country, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
