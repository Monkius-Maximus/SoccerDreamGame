using SoccerSim.Core.World;

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
}

/// <summary>
/// Characters. Writes recalculate shirt name, overall and the whole economy chain from the
/// attributes, the character's age and the prestige band of their club.
/// </summary>
public interface ICharacterRepository : IRepository<CharacterRecord, string>
{
    Task<IReadOnlyList<CharacterRecord>> ListByClubAsync(string clubId, CancellationToken cancellationToken = default);
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

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
