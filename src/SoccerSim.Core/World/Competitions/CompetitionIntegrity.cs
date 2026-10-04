namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// What a set of competitions and seasons must satisfy before it is a world at all (ADR-0012
/// §2–§5). Every way into the tool — the JSON document, the CSV tabs, an undo snapshot — passes
/// through here, so a broken structure is refused once, by name, with every problem listed.
///
/// <para>This is structure, not authoring state: a dangling reference or a season of another
/// year is refused, while an unbalanced flow or an unfilled season is something the author is
/// still working on and <see cref="PyramidRules"/> reports.</para>
/// </summary>
public static class CompetitionIntegrity
{
    public static IReadOnlyList<string> Check(
        IReadOnlyList<Competition> competitions,
        IReadOnlyList<CompetitionSeason> seasons,
        int currentSeason,
        IReadOnlyCollection<string> clubIds)
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (Competition competition in competitions)
        {
            string at = $"competitions[{competition.CompetitionId}]";

            if (!ids.Add(competition.CompetitionId))
                problems.Add($"{at}: the id is used by another competition");

            bool national = competition.Scope is CompetitionScope.National or CompetitionScope.SubNational;
            if (national && competition.CountryId is null)
                problems.Add($"{at}: a {competition.Scope} competition needs a countryId");
            if (!national && competition.CountryId is not null)
                problems.Add($"{at}: a {competition.Scope} competition has no countryId, and this one names {competition.CountryId}");

            if (competition.Level is { } level && (competition.Scope != CompetitionScope.National || level < 1))
                problems.Add($"{at}: only a National league has a pyramid level (1 or more), and this one declares {level}");

            if (competition.ClubCount < 2)
                problems.Add($"{at}: a competition needs at least two clubs, and this one declares {competition.ClubCount}");

            CheckStages(problems, at, competition);
        }

        foreach (Competition competition in competitions)
        {
            // A rule is stored by where its range starts; two starting at one rank cannot both be.
            foreach (int rank in competition.Transitions.GroupBy(rule => rule.RankFrom).Where(g => g.Count() > 1).Select(g => g.Key))
                problems.Add($"competitions[{competition.CompetitionId}]: two transition rules start at rank {rank}");

            foreach (TransitionRule rule in competition.Transitions)
            {
                if (rule.TargetCompetitionId == competition.CompetitionId || !ids.Contains(rule.TargetCompetitionId))
                {
                    problems.Add($"competitions[{competition.CompetitionId}]: ranks {rule.RankFrom}–{rule.RankTo} "
                        + $"go to '{rule.TargetCompetitionId}', which is not another competition of the world");
                }
            }
        }

        foreach (var clash in competitions
                     .Where(competition => competition.Level is not null)
                     .GroupBy(competition => (competition.CountryId, competition.Level))
                     .Where(group => group.Count() > 1))
        {
            problems.Add($"competitions: level {clash.Key.Level} of {clash.Key.CountryId} is claimed by "
                + string.Join(", ", clash.Select(competition => competition.CompetitionId)));
        }

        CheckSeasons(problems, competitions, seasons, currentSeason, ids, clubIds);

        return problems;
    }

    private static void CheckStages(List<string> problems, string at, Competition competition)
    {
        if (competition.Stages.Count == 0)
        {
            problems.Add($"{at}: a competition is played in at least one stage, and this one has none");
            return;
        }

        if (!competition.Stages.Select(stage => stage.Ordinal).Order().SequenceEqual(Enumerable.Range(1, competition.Stages.Count)))
            problems.Add($"{at}: stage ordinals must run 1, 2, 3… without repeats");

        foreach (CompetitionStage stage in competition.Stages.Where(stage => stage.Legs is not (1 or 2)))
            problems.Add($"{at}: stage {stage.Ordinal} is a league of one or two legs, and declares {stage.Legs}");
    }

    private static void CheckSeasons(
        List<string> problems,
        IReadOnlyList<Competition> competitions,
        IReadOnlyList<CompetitionSeason> seasons,
        int currentSeason,
        HashSet<string> competitionIds,
        IReadOnlyCollection<string> clubIds)
    {
        var seasonIds = new HashSet<string>(StringComparer.Ordinal);
        var clubs = clubIds as ISet<string> ?? clubIds.ToHashSet(StringComparer.Ordinal);

        foreach (CompetitionSeason season in seasons)
        {
            string at = $"seasons[{season.SeasonId}]";

            if (!seasonIds.Add(season.SeasonId))
                problems.Add($"{at}: the id is used by another season");

            if (!competitionIds.Contains(season.CompetitionId))
                problems.Add($"{at}: '{season.CompetitionId}' is not a competition of the world");

            // The tool authors one season; the next ones are the game's (ADR-0012 §3).
            if (season.Year != currentSeason)
                problems.Add($"{at}: the world's current season is {currentSeason}, and this one is {season.Year}");

            foreach (string clubId in season.ParticipantClubIds.Where(clubId => !clubs.Contains(clubId)))
                problems.Add($"{at}: participant '{clubId}' is not a club of the world");

            foreach (string clubId in season.ParticipantClubIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key))
                problems.Add($"{at}: '{clubId}' takes part twice");
        }

        foreach (var twice in seasons.GroupBy(season => (season.CompetitionId, season.Year)).Where(group => group.Count() > 1))
            problems.Add($"seasons: {twice.Key.CompetitionId} has {twice.Count()} seasons of {twice.Key.Year}");

        foreach (Competition levelled in competitions.Where(competition => competition.Level is not null))
        {
            if (!seasons.Any(season => season.CompetitionId == levelled.CompetitionId && season.Year == currentSeason))
                problems.Add($"competitions[{levelled.CompetitionId}]: a pyramid level needs its {currentSeason} season");
        }
    }
}
