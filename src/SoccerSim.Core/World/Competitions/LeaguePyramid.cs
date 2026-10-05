using SoccerSim.Core.World.Validation;

namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// One level of a country's pyramid: a national league competition with a <see cref="Competition.Level"/>
/// and its season of the year the pyramid is drawn for. "Divisão" on the screen (ADR-0012 §2).
/// </summary>
public sealed record PyramidLevel(Competition Competition, CompetitionSeason Season)
{
    public int Level => Competition.Level
        ?? throw new InvalidOperationException($"{Competition.CompetitionId} has no pyramid level.");

    /// <summary>Rounds and matches, derived from the stage and the field — null when the
    /// competition cannot be played as authored (<see cref="CompetitionStages.ShapeOf"/>).</summary>
    public CompetitionShape? Shape => CompetitionStages.ShapeOf(Competition);
}

/// <summary>
/// A country's pyramid for one season: its levelled national leagues, top to bottom. A view,
/// computed from the competitions and their seasons and never stored (ADR-0012 §6).
/// </summary>
public sealed record LeaguePyramid(string CountryId, int Year, IReadOnlyList<PyramidLevel> Levels)
{
    /// <summary>
    /// Draws the pyramid from the world's competitions. Every levelled competition of the country
    /// must have a season of <paramref name="year"/>: the pyramid editor, the importer and
    /// migration 0019 all create it together with the level, so a missing one is corruption and
    /// throws.
    /// </summary>
    public static LeaguePyramid Of(
        string countryId,
        int year,
        IReadOnlyList<Competition> competitions,
        IReadOnlyList<CompetitionSeason> seasons) =>
        new(
            countryId,
            year,
            competitions
                .Where(competition => competition.CountryId == countryId && competition.Level is not null)
                .OrderBy(competition => competition.Level)
                .ThenBy(competition => competition.CompetitionId, StringComparer.Ordinal)
                .Select(competition => new PyramidLevel(
                    competition,
                    seasons.SingleOrDefault(season => season.CompetitionId == competition.CompetitionId && season.Year == year)
                        ?? throw new InvalidOperationException(
                            $"{competition.CompetitionId} is a level of {countryId}'s pyramid but has no {year} season.")))
                .ToList());

    public PyramidLevel? Find(string competitionId) =>
        Levels.FirstOrDefault(level => level.Competition.CompetitionId == competitionId);

    /// <summary>
    /// How many clubs leave a level each season, by direction: up to a higher level (promotion)
    /// or down to a lower one (relegation). Derived from the rules and the levels they target —
    /// which kind of move a rule is is never stored (ADR-0012 §5).
    /// </summary>
    public (int Up, int Down) Moves(PyramidLevel level)
    {
        int up = 0, down = 0;

        foreach (TransitionRule rule in level.Competition.Transitions)
        {
            PyramidLevel? target = Find(rule.TargetCompetitionId);
            if (target is null)
                continue;

            if (target.Level < level.Level)
                up += rule.Count;
            else if (target.Level > level.Level)
                down += rule.Count;
        }

        return (up, down);
    }
}

/// <summary>
/// What has to be true of a pyramid for a season to be playable at all (ROADMAP.md Sprint 9,
/// restated over competitions by ADR-0012 §6).
/// </summary>
public static class PyramidRules
{
    /// <param name="competitions">Every competition in the world, so a transition rule can be
    /// checked against a target outside this pyramid.</param>
    public static IReadOnlyList<Finding> Check(LeaguePyramid pyramid, IReadOnlyList<Competition> competitions)
    {
        var findings = new List<Finding>();
        List<PyramidLevel> levels = [.. pyramid.Levels.OrderBy(level => level.Level)];

        if (levels.Count == 0)
        {
            findings.Add(new Finding(FindingLevel.Error, "PYRAMID_EMPTY", "Pirâmide vazia",
                $"{pyramid.CountryId} não tem nenhuma divisão"));
            return findings;
        }

        CheckLevels(findings, levels);
        CheckStages(findings, levels);
        CheckTransitions(findings, levels, competitions);
        CheckFlow(findings, levels);
        CheckParticipants(findings, levels);

        return findings;
    }

    /// <summary>Levels run 1, 2, 3… with no repeats and no holes. A hole is a league that
    /// relegates clubs into a level that does not exist.</summary>
    private static void CheckLevels(List<Finding> findings, List<PyramidLevel> levels)
    {
        foreach (var group in levels.GroupBy(level => level.Level).Where(g => g.Count() > 1))
        {
            findings.Add(new Finding(FindingLevel.Error, "LEVEL_DUP", "Nível duplicado",
                $"o nível {group.Key} está em {group.Count()} divisões "
                + $"({string.Join(", ", group.Select(level => level.Competition.CompetitionId))})"));
        }

        List<int> numbers = [.. levels.Select(level => level.Level).Distinct().Order()];

        if (numbers[0] != 1)
        {
            findings.Add(new Finding(FindingLevel.Error, "LEVEL_GAP", "Pirâmide sem topo",
                $"o nível mais alto é {numbers[0]}; a pirâmide começa em 1"));
        }

        for (int i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] != numbers[i - 1] + 1)
            {
                findings.Add(new Finding(FindingLevel.Error, "LEVEL_GAP", "Lacuna de nível",
                    $"não existe divisão no nível {numbers[i - 1] + 1}, entre {numbers[i - 1]} e {numbers[i]}"));
            }
        }
    }

    /// <summary>One stage, and one its field can play (ADR-0012 §4).</summary>
    private static void CheckStages(List<Finding> findings, List<PyramidLevel> levels)
    {
        foreach (PyramidLevel level in levels)
        {
            Competition competition = level.Competition;

            if (competition.Stages.Count != 1)
            {
                findings.Add(new Finding(FindingLevel.Error, "STAGE_COUNT", "Fases",
                    $"{competition.Name} tem {competition.Stages.Count} fase(s); só existe a fase de liga, uma por competição"));
                continue;
            }

            if (CompetitionStages.Unplayable(competition.Stages[0], competition.ClubCount) is { } reason)
            {
                findings.Add(new Finding(FindingLevel.Error, "STAGE_UNPLAYABLE", "Fase impossível",
                    $"{competition.Name}: {reason}"));
            }
        }
    }

    /// <summary>Each rule names ranks the competition has, no two rules claim the same rank, and
    /// the target is another competition that exists.</summary>
    private static void CheckTransitions(
        List<Finding> findings,
        List<PyramidLevel> levels,
        IReadOnlyList<Competition> competitions)
    {
        var known = competitions.Select(competition => competition.CompetitionId).ToHashSet(StringComparer.Ordinal);

        foreach (PyramidLevel level in levels)
        {
            Competition competition = level.Competition;

            foreach (TransitionRule rule in competition.Transitions)
            {
                if (rule.RankFrom < 1 || rule.RankFrom > rule.RankTo || rule.RankTo > competition.ClubCount)
                {
                    findings.Add(new Finding(FindingLevel.Error, "TRANSITION_RANGE", "Faixa de posições",
                        $"{competition.Name}: {rule.RankFrom}º–{rule.RankTo}º não cabe em 1º–{competition.ClubCount}º"));
                }

                if (rule.TargetCompetitionId == competition.CompetitionId || !known.Contains(rule.TargetCompetitionId))
                {
                    findings.Add(new Finding(FindingLevel.Error, "TRANSITION_TARGET", "Destino da transição",
                        $"{competition.Name}: {rule.RankFrom}º–{rule.RankTo}º vão para '{rule.TargetCompetitionId}', "
                        + "que não é outra competição do mundo"));
                }
            }

            var claimed = competition.Transitions
                .SelectMany(rule => Enumerable.Range(rule.RankFrom, Math.Max(0, rule.Count)))
                .GroupBy(rank => rank)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Order()
                .ToList();

            if (claimed.Count > 0)
            {
                findings.Add(new Finding(FindingLevel.Error, "TRANSITION_OVERLAP", "Posições em duas regras",
                    $"{competition.Name}: {string.Join(", ", claimed.Select(rank => $"{rank}º"))} "
                    + "aparecem em mais de uma regra de transição"));
            }
        }
    }

    /// <summary>
    /// The flow balance (ADR-0007 §3), computed from the rules: clubs arriving in a league each
    /// season equal clubs leaving it. When they differ, the league changes size every season —
    /// silently, and only visibly three seasons later.
    /// </summary>
    private static void CheckFlow(List<Finding> findings, List<PyramidLevel> levels)
    {
        foreach (PyramidLevel level in levels)
        {
            string id = level.Competition.CompetitionId;

            int arriving = levels
                .Where(other => other.Competition.CompetitionId != id)
                .SelectMany(other => other.Competition.Transitions)
                .Where(rule => rule.TargetCompetitionId == id)
                .Sum(rule => rule.Count);

            int leaving = level.Competition.Transitions.Sum(rule => rule.Count);

            if (arriving != leaving)
            {
                findings.Add(new Finding(FindingLevel.Error, "PYRAMID_FLOW", "Fluxo desequilibrado",
                    $"{level.Competition.Name} recebe {arriving} e perde {leaving} por temporada — "
                    + $"a divisão muda de tamanho ({level.Competition.ClubCount} clubes hoje)"));
            }
        }
    }

    /// <summary>A club takes part in one levelled season per country per year, and the season is
    /// as big as the competition declares.</summary>
    private static void CheckParticipants(List<Finding> findings, List<PyramidLevel> levels)
    {
        var seen = new Dictionary<string, List<PyramidLevel>>();

        foreach (PyramidLevel level in levels)
        {
            foreach (string clubId in level.Season.ParticipantClubIds)
            {
                if (!seen.TryGetValue(clubId, out List<PyramidLevel>? list))
                    seen[clubId] = list = [];
                list.Add(level);
            }

            int enrolled = level.Season.ParticipantClubIds.Count;
            if (enrolled != level.Competition.ClubCount)
            {
                findings.Add(new Finding(FindingLevel.Warning, "SEASON_UNFILLED", "Temporada incompleta",
                    $"{level.Competition.Name} declara {level.Competition.ClubCount} clubes e tem {enrolled} na temporada {level.Season.Year}"));
            }
        }

        foreach ((string clubId, List<PyramidLevel> memberships) in seen.Where(entry => entry.Value.Count > 1))
        {
            findings.Add(new Finding(FindingLevel.Error, "CLUB_TWO_LEAGUES", "Clube em duas divisões",
                $"{clubId} está em {string.Join(" e ", memberships.Select(level => level.Competition.Name))} "
                + "— um clube joga uma divisão por país e temporada"));
        }
    }
}
