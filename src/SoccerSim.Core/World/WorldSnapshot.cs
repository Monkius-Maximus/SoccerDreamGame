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
    IReadOnlyList<WorldSource> Sources);
