using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;

namespace SoccerSim.Core.Persistence;

/// <summary>
/// Persistence ports for the world-authoring schema. Keys are the authored string ids
/// (<c>clb_bra_rio_001</c>), not surrogate integers: they come from the source data, they are
/// what the tool's URLs and cross-references use, and inventing a second identity for the same
/// club would mean two ways to say the same thing.
/// </summary>
public interface IGeoNodeRepository : IRepository<GeoNode, string>
{
    Task<IReadOnlyList<GeoNode>> ListChildrenAsync(string? parentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Clubs. Writes recalculate every derived field (ΔE, luminance, polarity rule, home advantage)
/// from the authored ones before persisting — a derived value supplied by a caller is discarded,
/// never stored (ROADMAP.md Sprint 2).
/// </summary>
public interface IClubRepository : IRepository<ClubIdentity, string>
{
    Task<IReadOnlyList<ClubIdentity>> ListByGeoNodeAsync(string geoNodeId, CancellationToken cancellationToken = default);

    /// <summary>The club's current concurrency token, or 0 if there is no such club.</summary>
    Task<long> GetVersionAsync(string clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the club only if it is still at <paramref name="expectedVersion"/>, and returns the
    /// new version. Throws <see cref="WorldConcurrencyException"/> if the row moved on since the
    /// caller read it — a second tab's edit is not something to silently overwrite.
    /// </summary>
    Task<long> UpdateAsync(ClubIdentity club, long expectedVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Characters. Writes recalculate shirt name, overall and the whole economy chain from the
/// attributes, the character's age and the prestige band of their club.
/// </summary>
public interface ICharacterRepository : IRepository<CharacterRecord, string>
{
    Task<IReadOnlyList<CharacterRecord>> ListByClubAsync(string clubId, CancellationToken cancellationToken = default);

    Task<long> GetVersionAsync(string playerId, CancellationToken cancellationToken = default);

    /// <summary>See <see cref="IClubRepository.UpdateAsync(ClubIdentity, long, CancellationToken)"/>.</summary>
    Task<long> UpdateAsync(CharacterRecord character, long expectedVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// The record of what has been edited and what has not yet been exported. The counter in the top
/// bar is the number of pending entries; Sprint 7's export is what clears them.
/// </summary>
public interface IWorldEditLog
{
    Task RecordAsync(WorldEdit edit, CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Most recent first — the edit history, newest at the top.</summary>
    Task<IReadOnlyList<WorldEdit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Stamps every pending entry as exported and returns how many were cleared.</summary>
    Task<int> MarkExportedAsync(DateTime exportedAt, CancellationToken cancellationToken = default);
}

public enum WorldEntityType { Club, Character }

public sealed record WorldEdit(
    WorldEntityType EntityType,
    string EntityId,
    string FieldPath,
    string? OldValue,
    string? NewValue,
    DateTime EditedAt);

/// <summary>Thrown when a write loses a race: the row changed between the read and the write.
/// The API turns this into a 409 so the client can re-read and decide, rather than clobbering.</summary>
public sealed class WorldConcurrencyException : Exception
{
    public WorldConcurrencyException(string entityId, long expectedVersion, long actualVersion)
        : base($"'{entityId}' has moved on: expected version {expectedVersion}, found {actualVersion}.")
    {
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string EntityId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }
}

public interface ICompetitionRepository : IRepository<Competition, string>
{
}

/// <summary>
/// Calibration is one aggregate, not a collection: the constants, the age ladder, the prestige
/// bands, the home-advantage table, the stadium profiles and the position-weight matrix are read
/// and written together, because a formula that reads half of a re-fit is worse than one that
/// waits for all of it.
/// </summary>
public interface ICalibrationRepository
{
    /// <summary>The stored calibration, or null when none has been imported yet.</summary>
    Task<WorldCalibration?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(WorldCalibration calibration, CancellationToken cancellationToken = default);
}

/// <summary>The citation table. Replace-all rather than per-row edits: it is a bibliography
/// imported as a unit, and rows have no stable authored id to update against.</summary>
public interface IWorldSourceRepository
{
    Task<IReadOnlyList<WorldSource>> ListAsync(CancellationToken cancellationToken = default);

    Task ReplaceAllAsync(IReadOnlyList<WorldSource> sources, CancellationToken cancellationToken = default);
}

/// <summary>
/// The squad generator's measured inputs. Replace-all, like the calibration: the attribute shapes
/// and the name pools are one measurement, and half of a newer one mixed with half of an older
/// one describes no batch that ever existed.
/// </summary>
public interface IGenerationProfileRepository
{
    /// <summary>The stored profiles, or null when none have been imported.</summary>
    Task<GenerationProfiles?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GenerationProfiles profiles, CancellationToken cancellationToken = default);
}

/// <summary>
/// World-level facts from the document's meta block — the master seed every deterministic stream
/// derives from, and the schema version. Kept with the world rather than in configuration, so a
/// database always carries the seed that produced it.
/// </summary>
public interface IWorldSettingsRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transaction boundary for the world-authoring schema.
///
/// <para>
/// Deliberately separate from <see cref="IUnitOfWork"/> rather than bolted onto it: the legacy
/// game schema and the authoring schema are two different models that coexist
/// (docs/adr/0002-clubidentity-v2-coexistence.md), and putting ClubIdentity on the port the
/// MatchEngine's persistence uses would couple the game to a model it must not depend on until
/// the Sprint 6 projection exists. Both are backed by the same database and the same
/// connection factory; only the surfaces are kept apart.
/// </para>
/// </summary>
public interface IWorldUnitOfWork : IAsyncDisposable
{
    IGeoNodeRepository GeoNodes { get; }
    IClubRepository Clubs { get; }
    ICharacterRepository Characters { get; }
    ICompetitionRepository Competitions { get; }
    ICalibrationRepository Calibration { get; }
    IWorldSourceRepository Sources { get; }
    IWorldEditLog Edits { get; }
    IGenerationProfileRepository GenerationProfiles { get; }
    IWorldSettingsRepository Settings { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
