namespace SoccerSim.Core.World;

/// <summary>
/// The whole authored world in memory: what an import produces and what an export consumes.
/// Ordering is preserved from the source so a round trip is diffable.
/// </summary>
public sealed record WorldSnapshot(
    IReadOnlyList<GeoNode> GeoNodes,
    WorldCalibration Calibration,
    IReadOnlyList<ClubIdentity> Clubs,
    IReadOnlyList<CharacterRecord> Characters,
    IReadOnlyList<Competition> Competitions,
    IReadOnlyList<WorldSource> Sources,
    /// <summary>The document's meta block — schema version and, importantly, the master seed
    /// every deterministic stream in the world derives from.</summary>
    WorldMeta Meta);

/// <summary>
/// World-level facts the document declares about itself. <see cref="MasterSeed"/> is what makes
/// generation reproducible across machines: it belongs with the world it seeds, not in a config
/// file that can drift away from the data.
/// </summary>
public sealed record WorldMeta(long MasterSeed, string SchemaVersion, string? SourceFile)
{
    public const string MasterSeedKey = "masterSeed";
    public const string SchemaVersionKey = "schemaVersion";
    public const string SourceFileKey = "sourceFile";
}
