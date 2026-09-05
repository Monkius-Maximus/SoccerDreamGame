namespace SoccerSim.Core.World;

/// <summary>
/// A world-authoring competition. Distinct from the legacy <c>SoccerSim.Core.Domain.League</c>/
/// <c>Season</c> pair — this models scope, prestige and promotion/relegation as authored data,
/// not as something the <c>MatchEngine</c> simulates. No rounds are ever simulated here: the
/// tool states this explicitly wherever standings would otherwise be implied
/// (design_handoff_ferramenta_de_mundo/README.md, "Aviso de escopo").
/// </summary>
public sealed record Competition(
    string CompetitionId,
    string Name,
    CompetitionScope Scope,
    string AnchorGeoNodeId,
    string MemberPredicateId,
    PrestigeBand PrestigeBand,
    double LeagueTierFloat,
    string Format,
    int ClubCount,
    int Rounds,
    int PromotedIn,
    int RelegatedOut,
    string ContinentalSlots,
    string EditionId,
    int Season,
    IReadOnlyList<string> MemberClubIds);

/// <summary>One citation row. A record with a null <see cref="Url"/> is a prose cross-reference,
/// not a broken link — DATA_CONTRACT.md §5 warns against rendering it as an anchor tag.</summary>
public sealed record WorldSource(string Tema, string? Numero, string? Fonte, string? Url);
