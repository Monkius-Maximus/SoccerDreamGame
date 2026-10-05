namespace SoccerSim.Core.World;

/// <summary>
/// A competition's definition: what it is, not what one season of it was (ADR-0012 §2). It
/// outlives seasons. Distinct from the legacy <c>SoccerSim.Core.Domain.League</c>/<c>Season</c>
/// pair — the tool authors the structure and the game plays it (ADR-0012 §1), so nothing here is
/// a fixture, a result or a standing.
/// </summary>
public sealed record Competition(
    string CompetitionId,
    string Name,
    CompetitionScope Scope,
    /// <summary>Where on the map. Not the country: see <see cref="CountryId"/>.</summary>
    string AnchorGeoNodeId,
    /// <summary>Whose pyramid: the ISO code ("BRA"). Required for <see cref="CompetitionScope.SubNational"/>
    /// and <see cref="CompetitionScope.National"/>, null for zonal, continental and intercontinental
    /// scopes.</summary>
    string? CountryId,
    /// <summary>The pyramid level, 1 being the top flight. Set only on a national league that
    /// belongs to its country's pyramid ("Divisão" on the screen).</summary>
    int? Level,
    int ClubCount,
    /// <summary>How the competition is played, in order. Exactly one league stage today
    /// (ADR-0012 §4).</summary>
    IReadOnlyList<CompetitionStage> Stages,
    /// <summary>Where the clubs of a finished season go (ADR-0012 §5). Authored here, applied by
    /// the game.</summary>
    IReadOnlyList<TransitionRule> Transitions);

/// <summary>Closed vocabulary of stage kinds. Only the league exists until a competition needs
/// another (ADR-0012 §4).</summary>
public enum StageKind { League }

/// <summary>One stage of a competition. A league stage is a round robin played over
/// <see cref="Legs"/> legs: 1 is "turno único", 2 is "turno e returno".</summary>
public sealed record CompetitionStage(int Ordinal, StageKind Kind, int Legs);

/// <summary>
/// At the end of a season of the owning competition, the clubs finishing
/// <see cref="RankFrom"/>..<see cref="RankTo"/> go to the next season of
/// <see cref="TargetCompetitionId"/>. Whether that is a promotion, a relegation or a qualification
/// follows from the two competitions' levels and scopes; it is not stored.
/// </summary>
public sealed record TransitionRule(int RankFrom, int RankTo, string TargetCompetitionId)
{
    /// <summary>How many clubs the rule moves.</summary>
    public int Count => RankTo - RankFrom + 1;
}

/// <summary>
/// One edition of a competition: the year and the clubs taking part, in their authored order
/// (ADR-0012 §2). The tool writes only the world's current season (§3).
/// </summary>
public sealed record CompetitionSeason(
    string SeasonId,
    string CompetitionId,
    int Year,
    IReadOnlyList<string> ParticipantClubIds);

/// <summary>One citation row. A record with a null <see cref="Url"/> is a prose cross-reference,
/// not a broken link — DATA_CONTRACT.md §5 warns against rendering it as an anchor tag.</summary>
public sealed record WorldSource(string Tema, string? Numero, string? Fonte, string? Url);
