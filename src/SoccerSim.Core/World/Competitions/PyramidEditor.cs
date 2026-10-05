namespace SoccerSim.Core.World.Competitions;

/// <summary>Thrown when a pyramid edit is refused. The message is the reason, written for the
/// person who tried it — the API turns it into a 400 with that text.</summary>
public sealed class PyramidException : Exception
{
    public PyramidException(string message) : base(message)
    {
    }
}

/// <summary>
/// The operations the "Ligas" screen performs on a country's pyramid (ROADMAP.md Sprint 9,
/// restated over competitions by ADR-0012 §5–§6). Pure, like <see cref="GeoTree"/>: they take a
/// pyramid and return a pyramid, so the rules can be stated and tested without a database, and
/// <see cref="PyramidStore"/> persists the answer.
///
/// <para>The structural rules <see cref="PyramidRules"/> checks — levels contiguous and unique,
/// the flow balanced — cannot be broken from the screen at all. A level is always added at the
/// BOTTOM and removing one closes the gap behind it (ADR-0008 §2). Promotion and relegation are
/// written in pairs from one exchange number, so a pyramid built here is balanced by
/// construction.</para>
/// </summary>
public static class PyramidEditor
{
    /// <summary>
    /// Adds a national league below the last level, with an empty season of the pyramid's year.
    /// <paramref name="exchange"/> is how many clubs it swaps with the level above each season:
    /// that many go down from the bottom of the level above and the same number come up from its
    /// top. The first level has no level above and its exchange is 0.
    /// </summary>
    public static LeaguePyramid AddLevel(
        LeaguePyramid pyramid,
        WorldSnapshot world,
        string competitionId,
        string name,
        string anchorGeoNodeId,
        int legs,
        int clubCount,
        int exchange)
    {
        if (string.IsNullOrWhiteSpace(competitionId))
            throw new PyramidException("uma divisão precisa de um id.");

        if (string.IsNullOrWhiteSpace(name))
            throw new PyramidException("uma divisão precisa de um nome.");

        if (world.Competitions.Any(competition => competition.CompetitionId == competitionId))
            throw new PyramidException($"já existe uma competição com o id '{competitionId}'.");

        GeoNode anchor = world.GeoNodes.FirstOrDefault(node => node.GeoNodeId == anchorGeoNodeId)
            ?? throw new PyramidException($"não existe o nó geográfico '{anchorGeoNodeId}'.");

        if (anchor.Kind != GeoNodeKind.Country)
            throw new PyramidException($"a âncora de uma divisão é um país, e '{anchor.DisplayName}' é {anchor.Kind}.");

        CheckShape(legs, clubCount);

        PyramidLevel? above = pyramid.Levels.MaxBy(level => level.Level);

        if (above is null && exchange != 0)
            throw new PyramidException("a primeira divisão não tem com quem trocar clubes; a troca é 0.");

        var competition = new Competition(
            competitionId.Trim(),
            name.Trim(),
            CompetitionScope.National,
            anchor.GeoNodeId,
            pyramid.CountryId,
            Level: (above?.Level ?? 0) + 1,
            clubCount,
            CompetitionStages.League(legs),
            Transitions: []);

        var added = new PyramidLevel(
            competition,
            new CompetitionSeason(SeasonIdFor(competition.CompetitionId, pyramid.Year), competition.CompetitionId, pyramid.Year, []));

        LeaguePyramid grown = pyramid with { Levels = [.. pyramid.Levels, added] };

        return above is null ? grown : WithExchange(grown, above.Competition.CompetitionId, competition.CompetitionId, exchange);
    }

    /// <summary>
    /// Removes a level and pulls every level below it up one, so the pyramid stays contiguous. The
    /// rules that pointed at it go with it: the levels that become neighbours exchange nobody until
    /// the author says otherwise.
    ///
    /// <para>Refused while its season has participants, for the same reason a geo node with clubs
    /// on it cannot be deleted: the clubs would leave the pyramid without anyone saying where they
    /// went. Withdraw them first — that is a decision, and this would be a side effect.</para>
    /// </summary>
    public static LeaguePyramid RemoveLevel(LeaguePyramid pyramid, string competitionId)
    {
        PyramidLevel target = Find(pyramid, competitionId);

        if (target.Season.ParticipantClubIds.Count > 0)
        {
            throw new PyramidException(
                $"{target.Competition.Name} ainda tem {target.Season.ParticipantClubIds.Count} clube(s) "
                + "na temporada — retire-os antes de apagá-la.");
        }

        return pyramid with
        {
            Levels = [.. pyramid.Levels
                .Where(level => level.Competition.CompetitionId != competitionId)
                .Select(level => level with
                {
                    Competition = level.Competition with
                    {
                        Level = level.Level > target.Level ? level.Level - 1 : level.Level,
                        Transitions = [.. level.Competition.Transitions.Where(rule => rule.TargetCompetitionId != competitionId)],
                    },
                })],
        };
    }

    /// <summary>
    /// Rewrites what the author states about a level: its name, its legs, the size it is meant to
    /// reach and the exchange with the level below. The level number is not here — it is the
    /// league's place in the pyramid, and only adding and removing levels change it. The exchange
    /// with the level above is that level's to state.
    /// </summary>
    public static LeaguePyramid Rewrite(
        LeaguePyramid pyramid,
        string competitionId,
        string name,
        int legs,
        int clubCount,
        int exchangeBelow)
    {
        PyramidLevel target = Find(pyramid, competitionId);

        if (string.IsNullOrWhiteSpace(name))
            throw new PyramidException("uma divisão precisa de um nome.");

        CheckShape(legs, clubCount);

        LeaguePyramid rewritten = Replace(pyramid, target with
        {
            Competition = target.Competition with
            {
                Name = name.Trim(),
                ClubCount = clubCount,
                Stages = CompetitionStages.League(legs),
            },
        });

        PyramidLevel? below = pyramid.Levels.FirstOrDefault(level => level.Level == target.Level + 1);

        if (below is null)
        {
            if (exchangeBelow != 0)
                throw new PyramidException($"{target.Competition.Name} é a última divisão; não há com quem trocar clubes abaixo.");

            return rewritten;
        }

        return WithExchange(rewritten, competitionId, below.Competition.CompetitionId, exchangeBelow);
    }

    /// <summary>
    /// Enrols a club in a level's season, taking it out of whatever other level of this country it
    /// was in. A club plays one division per country and season, so a move is what enrolling
    /// always means — asking the author to withdraw first would only give them a chance to forget.
    /// </summary>
    public static LeaguePyramid Enrol(LeaguePyramid pyramid, string competitionId, string clubId)
    {
        PyramidLevel target = Find(pyramid, competitionId);

        if (string.IsNullOrWhiteSpace(clubId))
            throw new PyramidException("nenhum clube informado.");

        if (target.Season.ParticipantClubIds.Contains(clubId))
            throw new PyramidException($"{clubId} já está em {target.Competition.Name}.");

        return pyramid with
        {
            Levels = [.. pyramid.Levels.Select(level => level with
            {
                Season = level.Season with
                {
                    ParticipantClubIds = level.Competition.CompetitionId == competitionId
                        ? [.. level.Season.ParticipantClubIds, clubId]
                        : [.. level.Season.ParticipantClubIds.Where(id => id != clubId)],
                },
            })],
        };
    }

    /// <summary>Takes a club out of a level's season, leaving it in no division of this country.</summary>
    public static LeaguePyramid Withdraw(LeaguePyramid pyramid, string competitionId, string clubId)
    {
        PyramidLevel target = Find(pyramid, competitionId);

        if (!target.Season.ParticipantClubIds.Contains(clubId))
            throw new PyramidException($"{clubId} não está em {target.Competition.Name}.");

        return Replace(pyramid, target with
        {
            Season = target.Season with { ParticipantClubIds = [.. target.Season.ParticipantClubIds.Where(id => id != clubId)] },
        });
    }

    /// <summary>
    /// What two adjacent levels exchange each season: how many go down from the upper one and how
    /// many come up from the lower one. Equal for a pyramid built on this screen; data carried
    /// over by migration 0019 can differ, and then the screen shows both and <c>PYRAMID_FLOW</c>
    /// says why.
    /// </summary>
    public static (int Down, int Up) Exchange(PyramidLevel upper, PyramidLevel lower) =>
    (
        upper.Competition.Transitions
            .Where(rule => rule.TargetCompetitionId == lower.Competition.CompetitionId)
            .Sum(rule => rule.Count),
        lower.Competition.Transitions
            .Where(rule => rule.TargetCompetitionId == upper.Competition.CompetitionId)
            .Sum(rule => rule.Count)
    );

    /// <summary>The id of a season the tool creates: <c>edt_&lt;competitionId&gt;_&lt;year&gt;</c>,
    /// the shape migration 0019 gives a converted division's season.</summary>
    public static string SeasonIdFor(string competitionId, int year) => $"edt_{competitionId}_{year}";

    /// <summary>
    /// Writes the pair of rules between two adjacent levels from one number: the bottom
    /// <paramref name="exchange"/> of the upper level go down, the top <paramref name="exchange"/>
    /// of the lower level come up. Whatever either level said about the other before is replaced.
    /// </summary>
    private static LeaguePyramid WithExchange(LeaguePyramid pyramid, string upperId, string lowerId, int exchange)
    {
        if (exchange < 0)
            throw new PyramidException("a troca entre divisões não pode ser negativa.");

        PyramidLevel upper = Find(pyramid, upperId);
        PyramidLevel lower = Find(pyramid, lowerId);

        List<TransitionRule> upperRules = [.. upper.Competition.Transitions.Where(rule => rule.TargetCompetitionId != lowerId)];
        List<TransitionRule> lowerRules = [.. lower.Competition.Transitions.Where(rule => rule.TargetCompetitionId != upperId)];

        CheckRoom(upper.Competition, upperRules, exchange);
        CheckRoom(lower.Competition, lowerRules, exchange);

        if (exchange > 0)
        {
            int clubs = upper.Competition.ClubCount;
            upperRules.Add(new TransitionRule(clubs - exchange + 1, clubs, lowerId));
            lowerRules.Add(new TransitionRule(1, exchange, upperId));
        }

        LeaguePyramid withUpper = Replace(pyramid, upper with
        {
            Competition = upper.Competition with { Transitions = [.. upperRules.OrderBy(rule => rule.RankFrom)] },
        });

        return Replace(withUpper, lower with
        {
            Competition = lower.Competition with { Transitions = [.. lowerRules.OrderBy(rule => rule.RankFrom)] },
        });
    }

    /// <summary>The ranks a league already sends elsewhere plus the new exchange must fit in its
    /// field; otherwise one club would be both promoted and relegated.</summary>
    private static void CheckRoom(Competition competition, IReadOnlyList<TransitionRule> others, int exchange)
    {
        int taken = others.Sum(rule => rule.Count);

        if (taken + exchange > competition.ClubCount)
        {
            throw new PyramidException(
                $"{competition.Name} tem {competition.ClubCount} clubes e já troca {taken}; "
                + $"não cabem mais {exchange}.");
        }
    }

    /// <summary>
    /// Two clubs and one or two legs are the floor the schema itself states
    /// (sql/0019_competitions_as_composition.sql). Checked here as well so the answer is a refusal
    /// with a reason instead of a constraint violation from inside the database.
    /// </summary>
    private static void CheckShape(int legs, int clubCount)
    {
        if (clubCount < 2)
            throw new PyramidException($"uma divisão precisa de ao menos dois clubes, e esta declara {clubCount}.");

        if (legs is not (1 or 2))
            throw new PyramidException($"uma divisão tem um ou dois turnos, e esta declara {legs}.");
    }

    private static PyramidLevel Find(LeaguePyramid pyramid, string competitionId) =>
        pyramid.Find(competitionId)
        ?? throw new PyramidException($"não existe divisão '{competitionId}' em {pyramid.CountryId}.");

    private static LeaguePyramid Replace(LeaguePyramid pyramid, PyramidLevel replacement) =>
        pyramid with
        {
            Levels = [.. pyramid.Levels.Select(level =>
                level.Competition.CompetitionId == replacement.Competition.CompetitionId ? replacement : level)],
        };
}
