using SoccerSim.Core.World;
using SoccerSim.Core.World.Fields;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>
/// One field change. The client sends the path it is editing, the new value as text, and the
/// version it read — sending the version is what turns "last write wins" into a detectable
/// conflict.
/// </summary>
public sealed record FieldPatchRequest(string Path, string? Value, long Version);

/// <summary>What a successful patch produces: the new concurrency token and the pending-export
/// counter, so the client does not need a second round trip to stay in sync.</summary>
public sealed record FieldPatchResponse(long Version, int PendingEdits);

/// <summary>The form definition: the groups, their fields, and for each field its kind,
/// provenance seal and whether it can be edited at all.</summary>
public sealed record FieldCatalogDto(
    IReadOnlyList<WorldFieldGroup> Club,
    IReadOnlyList<WorldFieldGroup> Character);

/// <summary>
/// A player as the modal shows them: the record, the position weights that explain which
/// attributes matter for the position, and what the economy WOULD be if recalculated now.
///
/// <para>
/// Stored and recalculated values can only differ after a calibration re-fit, since every write
/// recalculates. Showing the difference rather than silently correcting it is deliberate — it is
/// how the user finds out a re-fit aged the batch (ALGORITHMS.md §2.4).
/// </para>
/// </summary>
public sealed record CharacterPageDto(
    CharacterRecord Character,
    long Version,
    IReadOnlyDictionary<Attr, double> PositionWeights,
    CharacterEconomyDto Stored,
    CharacterEconomyDto Recalculated,
    bool EconomyDiverges);

public sealed record CharacterEconomyDto(int Overall, int PotentialOverall, int MarketValueEur, int SalaryMonthlyBrl);
