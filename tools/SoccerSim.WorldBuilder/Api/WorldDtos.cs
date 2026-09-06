using SoccerSim.Core.World;
using SoccerSim.Core.World.Validation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>
/// The HTTP contract shapes. Deliberately separate from the domain records: these carry values
/// the domain does not store because they are computed (squad overall, invariant level, the
/// geographic breadcrumb), and they are computed HERE, on the server, by Core. The browser
/// renders; it never derives.
/// </summary>
public sealed record WorldSummaryDto(
    string SchemaVersion,
    bool Loaded,
    WorldCountsDto Counts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Enums,
    WorldCalibration? Calibration);

public sealed record WorldCountsDto(int Clubs, int Characters, int GeoNodes, int Competitions, int Sources);

/// <summary>A row in the club rail: enough to identify, colour and rank a club without loading it.</summary>
public sealed record ClubListItemDto(
    string ClubId,
    string DisplayCode,
    string ShortName,
    string OfficialName,
    string CityName,
    PrestigeBand PrestigeBand,
    string PrimaryColor,
    int SquadOverall,
    FindingLevel InvariantLevel);

/// <summary>Everything the club page shows, in one response — the point of the whole tool is that
/// a club is one page, so it is also one request.</summary>
public sealed record ClubPageDto(
    ClubIdentity Club,
    /// <summary>The concurrency token the form patches with — it travels with the page so an
    /// edit can say which version of the club it was made against.</summary>
    long Version,
    IReadOnlyList<string> GeoPath,
    IReadOnlyList<Finding> Findings,
    FindingLevel InvariantLevel,
    SquadMetrics Metrics,
    string? DerbyRivalName,
    StadiumProfileEntry? StadiumProfile,
    double BandValueMult);
