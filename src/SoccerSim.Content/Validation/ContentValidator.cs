using SoccerSim.Content.Model;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Content.Validation;

/// <summary>
/// The one and only implementation of "is this content usable?".
///
/// It is deliberately shared by every call site — the authoring tool calls it while you type,
/// the export refuses to write without it, and the importer runs it again before touching the
/// database. One rule set means the tool can never green-light content the game then rejects.
/// </summary>
public sealed class ContentValidator
{
    public static ContentValidator Default { get; } = new();

    public ContentValidationResult Validate(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var issues = new List<ContentIssue>();

        // Identity first: everything downstream resolves references by key, so duplicate or
        // malformed keys would make the later rules report nonsense.
        CheckIdentity(bundle.Nations, ContentCategory.Nations, issues);
        CheckIdentity(bundle.Stadiums, ContentCategory.Stadiums, issues);
        CheckIdentity(bundle.Competitions, ContentCategory.Competitions, issues);
        CheckIdentity(bundle.Coaches, ContentCategory.Coaches, issues);
        CheckIdentity(bundle.Contracts, ContentCategory.Contracts, issues);
        CheckIdentity(bundle.Traits, ContentCategory.Traits, issues);
        CheckIdentity(bundle.Leagues, ContentCategory.Leagues, issues);
        CheckIdentity(bundle.Teams, ContentCategory.Teams, issues);
        CheckIdentity(bundle.Players, ContentCategory.Players, issues);
        CheckIdentity(bundle.HousingItems, ContentCategory.HousingItems, issues);
        CheckIdentity(bundle.World.Seasons, ContentCategory.Seasons, issues);
        CheckIdentity(bundle.World.Fixtures, ContentCategory.Fixtures, issues);

        var traitKeys = ToKeySet(bundle.Traits);
        var leagueKeys = ToKeySet(bundle.Leagues);
        var teamKeys = ToKeySet(bundle.Teams);
        var playerKeys = ToKeySet(bundle.Players);
        var seasonKeys = ToKeySet(bundle.World.Seasons);
        var nationKeys = ToKeySet(bundle.Nations);
        var stadiumKeys = ToKeySet(bundle.Stadiums);
        var competitionKeys = ToKeySet(bundle.Competitions);
        var coachKeys = ToKeySet(bundle.Coaches);

        CheckNations(bundle, issues);
        CheckStadiums(bundle, nationKeys, issues);
        CheckCompetitions(bundle, nationKeys, issues);
        CheckTraits(bundle, issues);
        CheckLeagues(bundle, competitionKeys, nationKeys, issues);
        CheckTeams(bundle, leagueKeys, nationKeys, stadiumKeys, issues);
        CheckPlayers(bundle, teamKeys, traitKeys, nationKeys, issues);
        CheckCoaches(bundle, teamKeys, nationKeys, issues);
        CheckContracts(bundle, playerKeys, coachKeys, teamKeys, issues);
        CheckHousingItems(bundle, issues);
        CheckWorld(bundle, leagueKeys, teamKeys, playerKeys, seasonKeys, issues);
        CheckSquads(bundle, issues);

        return new ContentValidationResult { Issues = issues };
    }

    private static void CheckNations(ContentBundle bundle, List<ContentIssue> issues)
    {
        var seenCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ContentNation nation in bundle.Nations)
        {
            RequireText(nation.Name, ContentCategory.Nations, nation.Key, nameof(nation.Name), issues);
            RequireRange(nation.Reputation, 0, 100, ContentCategory.Nations, nation.Key,
                nameof(nation.Reputation), issues);

            if (nation.Code?.Length != 3)
            {
                issues.Add(Error(Codes.NationCode, ContentCategory.Nations, nation.Key, nameof(nation.Code),
                    $"Country code must be exactly 3 characters, got '{nation.Code}'."));
            }
            else if (!seenCodes.TryAdd(nation.Code, nation.Key))
            {
                // The column is UNIQUE, so this would otherwise fail as a constraint violation
                // deep inside the import with no indication of which two nations clashed.
                issues.Add(Error(Codes.NationCode, ContentCategory.Nations, nation.Key, nameof(nation.Code),
                    $"Code '{nation.Code}' is already used by '{seenCodes[nation.Code]}'."));
            }
        }
    }

    private static void CheckStadiums(
        ContentBundle bundle, IReadOnlySet<string> nationKeys, List<ContentIssue> issues)
    {
        foreach (ContentStadium stadium in bundle.Stadiums)
        {
            RequireText(stadium.Name, ContentCategory.Stadiums, stadium.Key, nameof(stadium.Name), issues);
            RequireOptionalRef(stadium.NationKey, nationKeys, ContentCategory.Stadiums, stadium.Key,
                nameof(stadium.NationKey), ContentCategory.Nations, issues);

            // Bounds mirror the laws of the game and the CHECK constraints in 0007.
            RequireRange(stadium.PitchLengthM, 90, 120, ContentCategory.Stadiums, stadium.Key,
                nameof(stadium.PitchLengthM), issues);
            RequireRange(stadium.PitchWidthM, 45, 90, ContentCategory.Stadiums, stadium.Key,
                nameof(stadium.PitchWidthM), issues);

            if (stadium.Capacity < 0)
            {
                issues.Add(Error(Codes.Range, ContentCategory.Stadiums, stadium.Key, nameof(stadium.Capacity),
                    $"Capacity must not be negative, got {stadium.Capacity}."));
            }
        }
    }

    private static void CheckCompetitions(
        ContentBundle bundle, IReadOnlySet<string> nationKeys, List<ContentIssue> issues)
    {
        foreach (ContentCompetition competition in bundle.Competitions)
        {
            RequireText(competition.Name, ContentCategory.Competitions, competition.Key,
                nameof(competition.Name), issues);
            RequireOptionalRef(competition.NationKey, nationKeys, ContentCategory.Competitions,
                competition.Key, nameof(competition.NationKey), ContentCategory.Nations, issues);
            RequireRange(competition.Reputation, 0, 100, ContentCategory.Competitions, competition.Key,
                nameof(competition.Reputation), issues);

            int divisions = bundle.Leagues.Count(l =>
                string.Equals(l.CompetitionKey, competition.Key, StringComparison.Ordinal));

            // A cup still needs exactly one division row, because Matches.LeagueId is what the
            // LOD router reads — a cup fixture with nowhere to point could not be resolved.
            if (competition.Format == CompetitionFormat.Cup && divisions != 1)
            {
                issues.Add(Error(Codes.CompetitionDivisions, ContentCategory.Competitions, competition.Key,
                    nameof(competition.Format),
                    $"A cup must own exactly one division for fixture routing, found {divisions}."));
            }
            else if (competition.Format == CompetitionFormat.League && divisions == 0)
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = Codes.CompetitionDivisions,
                    Category = ContentCategory.Competitions,
                    EntityKey = competition.Key,
                    Message = "No divisions belong to this competition yet.",
                });
            }

            if (competition.PointsWin < competition.PointsDraw)
            {
                issues.Add(Error(Codes.Range, ContentCategory.Competitions, competition.Key,
                    nameof(competition.PointsWin),
                    $"A win ({competition.PointsWin}) must be worth at least as much as a draw "
                    + $"({competition.PointsDraw})."));
            }
        }
    }

    private static void CheckCoaches(
        ContentBundle bundle,
        IReadOnlySet<string> teamKeys,
        IReadOnlySet<string> nationKeys,
        List<ContentIssue> issues)
    {
        var headCoachByTeam = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ContentCoach coach in bundle.Coaches)
        {
            RequireText(coach.FirstName, ContentCategory.Coaches, coach.Key, nameof(coach.FirstName), issues);
            RequireText(coach.LastName, ContentCategory.Coaches, coach.Key, nameof(coach.LastName), issues);
            RequireOptionalRef(coach.TeamKey, teamKeys, ContentCategory.Coaches, coach.Key,
                nameof(coach.TeamKey), ContentCategory.Teams, issues);
            RequireOptionalRef(coach.NationKey, nationKeys, ContentCategory.Coaches, coach.Key,
                nameof(coach.NationKey), ContentCategory.Nations, issues);

            foreach ((string field, int value) in new[]
                     {
                         (nameof(coach.Coaching), coach.Coaching),
                         (nameof(coach.TacticalKnowledge), coach.TacticalKnowledge),
                         (nameof(coach.ManManagement), coach.ManManagement),
                         (nameof(coach.Fitness), coach.Fitness),
                         (nameof(coach.Scouting), coach.Scouting),
                     })
            {
                RequireRange(value, PlayerAttributes.MinValue, PlayerAttributes.MaxValue,
                    ContentCategory.Coaches, coach.Key, field, issues);
            }

            if (!Mentalities.Contains(coach.PreferredMentality))
            {
                issues.Add(Error(Codes.EnumInvalid, ContentCategory.Coaches, coach.Key,
                    nameof(coach.PreferredMentality),
                    $"'{coach.PreferredMentality}' is not a known mentality. Expected one of: "
                    + string.Join(", ", Mentalities)));
            }

            if (coach.Role == CoachRole.HeadCoach && coach.TeamKey is not null
                && !headCoachByTeam.TryAdd(coach.TeamKey, coach.Key))
            {
                issues.Add(Error(Codes.DuplicateHeadCoach, ContentCategory.Coaches, coach.Key,
                    nameof(coach.Role),
                    $"'{coach.TeamKey}' already has head coach '{headCoachByTeam[coach.TeamKey]}'."));
            }
        }
    }

    private static void CheckContracts(
        ContentBundle bundle,
        IReadOnlySet<string> playerKeys,
        IReadOnlySet<string> coachKeys,
        IReadOnlySet<string> teamKeys,
        List<ContentIssue> issues)
    {
        foreach (ContentContract contract in bundle.Contracts)
        {
            RequireRef(contract.TeamKey, teamKeys, ContentCategory.Contracts, contract.Key,
                nameof(contract.TeamKey), ContentCategory.Teams, issues);
            RequireOptionalRef(contract.PlayerKey, playerKeys, ContentCategory.Contracts, contract.Key,
                nameof(contract.PlayerKey), ContentCategory.Players, issues);
            RequireOptionalRef(contract.CoachKey, coachKeys, ContentCategory.Contracts, contract.Key,
                nameof(contract.CoachKey), ContentCategory.Coaches, issues);

            // Mirrors the XOR CHECK in 0007: a contract binds exactly one person.
            if ((contract.PlayerKey is null) == (contract.CoachKey is null))
            {
                issues.Add(Error(Codes.ContractSubject, ContentCategory.Contracts, contract.Key,
                    nameof(contract.PlayerKey),
                    "A contract must name exactly one of playerKey or coachKey."));
            }

            if (contract.EndDate <= contract.StartDate)
            {
                issues.Add(Error(Codes.ContractDates, ContentCategory.Contracts, contract.Key,
                    nameof(contract.EndDate),
                    $"End date {contract.EndDate:yyyy-MM-dd} must be after start date "
                    + $"{contract.StartDate:yyyy-MM-dd}."));
            }

            if (contract.WeeklyWage < 0)
            {
                issues.Add(Error(Codes.Range, ContentCategory.Contracts, contract.Key,
                    nameof(contract.WeeklyWage),
                    $"Weekly wage must not be negative, got {contract.WeeklyWage}."));
            }
        }

        CheckOverlappingContracts(bundle, issues);
    }

    private static void CheckOverlappingContracts(ContentBundle bundle, List<ContentIssue> issues)
    {
        IEnumerable<IGrouping<string, ContentContract>> bySubject = bundle.Contracts
            .Where(c => c.PlayerKey is not null || c.CoachKey is not null)
            .GroupBy(c => c.PlayerKey ?? c.CoachKey!, StringComparer.Ordinal);

        foreach (IGrouping<string, ContentContract> group in bySubject)
        {
            List<ContentContract> ordered = group.OrderBy(c => c.StartDate).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartDate < ordered[i - 1].EndDate)
                {
                    issues.Add(Error(Codes.ContractOverlap, ContentCategory.Contracts, ordered[i].Key,
                        nameof(ContentContract.StartDate),
                        $"Overlaps '{ordered[i - 1].Key}', which runs to "
                        + $"{ordered[i - 1].EndDate:yyyy-MM-dd}. One person cannot hold two "
                        + "contracts at the same time."));
                }
            }
        }
    }

    private static readonly HashSet<string> Mentalities = new(StringComparer.Ordinal)
    {
        "VeryDefensive", "Defensive", "Balanced", "Attacking", "VeryAttacking",
    };

    private static void CheckIdentity<T>(IReadOnlyList<T> entities, string category, List<ContentIssue> issues)
        where T : IContentEntity
    {
        var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenIds = new Dictionary<int, string>();

        foreach (T entity in entities)
        {
            // A bundle can arrive from hand-edited JSON, where a missing key deserializes to
            // null despite the non-nullable declaration. Normalise once so every rule below
            // reports against a real string.
            string key = entity.Key ?? string.Empty;

            if (!ContentKey.IsValid(key))
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = Codes.KeyFormat,
                    Category = category,
                    EntityKey = key,
                    Field = nameof(IContentEntity.Key),
                    Message = $"'{key}' is not a valid key. Use lowercase letters, digits and "
                              + "'-', '_' or '.', starting with a letter.",
                });
            }
            else if (!seenKeys.TryAdd(key, entity.Id))
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = Codes.KeyDuplicate,
                    Category = category,
                    EntityKey = key,
                    Field = nameof(IContentEntity.Key),
                    Message = $"Key '{key}' is used more than once in {category}.",
                });
            }

            if (entity.Id <= 0)
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = Codes.IdInvalid,
                    Category = category,
                    EntityKey = key,
                    Field = nameof(IContentEntity.Id),
                    Message = $"Id must be a positive integer, got {entity.Id}.",
                });
            }
            else if (!seenIds.TryAdd(entity.Id, key))
            {
                // Ids are foreign keys in the save file. Two entities sharing one is the exact
                // corruption a branch merge produces, so it has to be a hard failure.
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = Codes.IdDuplicate,
                    Category = category,
                    EntityKey = key,
                    Field = nameof(IContentEntity.Id),
                    Message = $"Id {entity.Id} is already used by '{seenIds[entity.Id]}'. "
                              + "Ids must be unique within a category — this usually means two branches "
                              + "allocated the same id and the merge needs fixing.",
                });
            }
        }
    }

    private static void CheckTraits(ContentBundle bundle, List<ContentIssue> issues)
    {
        foreach (ContentTrait trait in bundle.Traits)
        {
            RequireText(trait.DisplayName, ContentCategory.Traits, trait.Key,
                nameof(trait.DisplayName), issues);
            RequireRange(trait.Aggression, 0, 100, ContentCategory.Traits, trait.Key,
                nameof(trait.Aggression), issues);
            RequireRange(trait.Selfishness, 0, 100, ContentCategory.Traits, trait.Key,
                nameof(trait.Selfishness), issues);
        }
    }

    private static void CheckLeagues(
        ContentBundle bundle,
        IReadOnlySet<string> competitionKeys,
        IReadOnlySet<string> nationKeys,
        List<ContentIssue> issues)
    {
        foreach (ContentLeague league in bundle.Leagues)
        {
            RequireText(league.Name, ContentCategory.Leagues, league.Key, nameof(league.Name), issues);
            RequireText(league.Country, ContentCategory.Leagues, league.Key, nameof(league.Country), issues);
            RequireOptionalRef(league.CompetitionKey, competitionKeys, ContentCategory.Leagues, league.Key,
                nameof(league.CompetitionKey), ContentCategory.Competitions, issues);
            RequireOptionalRef(league.NationKey, nationKeys, ContentCategory.Leagues, league.Key,
                nameof(league.NationKey), ContentCategory.Nations, issues);
            RequireRange(league.PyramidLevel, 1, 10, ContentCategory.Leagues, league.Key,
                nameof(league.PyramidLevel), issues);

            if (!Enum.IsDefined(league.Tier))
            {
                issues.Add(Error(Codes.EnumInvalid, ContentCategory.Leagues, league.Key, nameof(league.Tier),
                    $"'{league.Tier}' is not a known simulation tier."));
            }
        }
    }

    private static void CheckTeams(
        ContentBundle bundle,
        IReadOnlySet<string> leagueKeys,
        IReadOnlySet<string> nationKeys,
        IReadOnlySet<string> stadiumKeys,
        List<ContentIssue> issues)
    {
        foreach (ContentTeam team in bundle.Teams)
        {
            RequireText(team.Name, ContentCategory.Teams, team.Key, nameof(team.Name), issues);
            RequireRef(team.LeagueKey, leagueKeys, ContentCategory.Teams, team.Key,
                nameof(team.LeagueKey), ContentCategory.Leagues, issues);
            RequireOptionalRef(team.NationKey, nationKeys, ContentCategory.Teams, team.Key,
                nameof(team.NationKey), ContentCategory.Nations, issues);
            RequireOptionalRef(team.StadiumKey, stadiumKeys, ContentCategory.Teams, team.Key,
                nameof(team.StadiumKey), ContentCategory.Stadiums, issues);
            RequireRange(team.Reputation, 0, 100, ContentCategory.Teams, team.Key,
                nameof(team.Reputation), issues);

            if (team.Budget < 0)
            {
                issues.Add(Error(Codes.Range, ContentCategory.Teams, team.Key, nameof(team.Budget),
                    $"Budget must not be negative, got {team.Budget}."));
            }
        }
    }

    private static void CheckPlayers(
        ContentBundle bundle,
        IReadOnlySet<string> teamKeys,
        IReadOnlySet<string> traitKeys,
        IReadOnlySet<string> nationKeys,
        List<ContentIssue> issues)
    {
        foreach (ContentPlayer player in bundle.Players)
        {
            RequireText(player.FirstName, ContentCategory.Players, player.Key, nameof(player.FirstName), issues);
            RequireText(player.LastName, ContentCategory.Players, player.Key, nameof(player.LastName), issues);

            // A null TeamKey is a free agent, which is legitimate; a non-null one must resolve.
            RequireOptionalRef(player.TeamKey, teamKeys, ContentCategory.Players, player.Key,
                nameof(player.TeamKey), ContentCategory.Teams, issues);
            RequireOptionalRef(player.NationKey, nationKeys, ContentCategory.Players, player.Key,
                nameof(player.NationKey), ContentCategory.Nations, issues);

            if (player.SquadNumber is int number && number is < 1 or > 99)
            {
                issues.Add(Error(Codes.Range, ContentCategory.Players, player.Key,
                    nameof(player.SquadNumber), $"Squad number must be between 1 and 99, got {number}."));
            }

            foreach ((string name, int value) in player.Attributes.Enumerate())
            {
                RequireRange(value, PlayerAttributes.MinValue, PlayerAttributes.MaxValue,
                    ContentCategory.Players, player.Key, $"{nameof(player.Attributes)}.{name}", issues);
            }

            var seenTraits = new HashSet<string>(StringComparer.Ordinal);
            foreach (string traitKey in player.TraitKeys)
            {
                RequireRef(traitKey, traitKeys, ContentCategory.Players, player.Key,
                    nameof(player.TraitKeys), ContentCategory.Traits, issues);

                if (!seenTraits.Add(traitKey))
                {
                    // PlayerTraitAssignments is keyed on (PlayerId, TraitId); a repeat would
                    // fail on insert rather than here, so catch it while the user can see it.
                    issues.Add(Error(Codes.TraitDuplicate, ContentCategory.Players, player.Key,
                        nameof(player.TraitKeys), $"Trait '{traitKey}' is assigned twice."));
                }
            }
        }
    }

    private static void CheckHousingItems(ContentBundle bundle, List<ContentIssue> issues)
    {
        foreach (ContentHousingItem item in bundle.HousingItems)
        {
            RequireText(item.Name, ContentCategory.HousingItems, item.Key, nameof(item.Name), issues);
            RequireText(item.StatKey, ContentCategory.HousingItems, item.Key, nameof(item.StatKey), issues);

            if (item.Cost < 0)
            {
                issues.Add(Error(Codes.Range, ContentCategory.HousingItems, item.Key, nameof(item.Cost),
                    $"Cost must not be negative, got {item.Cost}."));
            }

            if (item.YieldMultiplier <= 0)
            {
                issues.Add(Error(Codes.Range, ContentCategory.HousingItems, item.Key,
                    nameof(item.YieldMultiplier),
                    $"Yield multiplier must be greater than zero, got {item.YieldMultiplier}."));
            }
        }
    }

    private static void CheckWorld(
        ContentBundle bundle,
        IReadOnlySet<string> leagueKeys,
        IReadOnlySet<string> teamKeys,
        IReadOnlySet<string> playerKeys,
        IReadOnlySet<string> seasonKeys,
        List<ContentIssue> issues)
    {
        ContentWorld world = bundle.World;

        var currentByLeague = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ContentSeason season in world.Seasons)
        {
            RequireRef(season.LeagueKey, leagueKeys, ContentCategory.Seasons, season.Key,
                nameof(season.LeagueKey), ContentCategory.Leagues, issues);

            if (season.EndDate <= season.StartDate)
            {
                issues.Add(Error(Codes.SeasonDates, ContentCategory.Seasons, season.Key,
                    nameof(season.EndDate),
                    $"End date {season.EndDate:yyyy-MM-dd} must be after start date {season.StartDate:yyyy-MM-dd}."));
            }

            if (season.IsCurrent && !currentByLeague.TryAdd(season.LeagueKey, season.Key))
            {
                issues.Add(Error(Codes.SeasonCurrentDuplicate, ContentCategory.Seasons, season.Key,
                    nameof(season.IsCurrent),
                    $"League '{season.LeagueKey}' already has '{currentByLeague[season.LeagueKey]}' "
                    + "marked as its current season."));
            }
        }

        foreach (ContentFixture fixture in world.Fixtures)
        {
            RequireRef(fixture.SeasonKey, seasonKeys, ContentCategory.Fixtures, fixture.Key,
                nameof(fixture.SeasonKey), ContentCategory.Seasons, issues);
            RequireRef(fixture.HomeTeamKey, teamKeys, ContentCategory.Fixtures, fixture.Key,
                nameof(fixture.HomeTeamKey), ContentCategory.Teams, issues);
            RequireRef(fixture.AwayTeamKey, teamKeys, ContentCategory.Fixtures, fixture.Key,
                nameof(fixture.AwayTeamKey), ContentCategory.Teams, issues);

            if (string.Equals(fixture.HomeTeamKey, fixture.AwayTeamKey, StringComparison.Ordinal))
            {
                // MatchEntryGuard throws on this at kickoff; catching it here means the authoring
                // tool refuses to export rather than the game crashing on Play Next Fixture.
                issues.Add(Error(Codes.FixtureSameTeam, ContentCategory.Fixtures, fixture.Key,
                    nameof(fixture.AwayTeamKey),
                    $"A club cannot play itself ('{fixture.HomeTeamKey}')."));
            }
        }

        foreach (ContentPlayerFinance finance in world.Finances)
        {
            RequireRef(finance.PlayerKey, playerKeys, ContentCategory.World, finance.PlayerKey,
                nameof(finance.PlayerKey), ContentCategory.Players, issues);
        }

        if (world.HumanPlayerKey is not null)
        {
            RequireRef(world.HumanPlayerKey, playerKeys, ContentCategory.World, world.HumanPlayerKey,
                nameof(world.HumanPlayerKey), ContentCategory.Players, issues);

            // SqliteCareerService resolves the human's club and throws when there is none, so an
            // unattached human is a guaranteed crash at startup.
            ContentPlayer? human = bundle.Players
                .FirstOrDefault(p => string.Equals(p.Key, world.HumanPlayerKey, StringComparison.Ordinal));
            if (human is not null && human.TeamKey is null)
            {
                issues.Add(Error(Codes.CareerNoTeam, ContentCategory.World, world.HumanPlayerKey,
                    nameof(world.HumanPlayerKey),
                    "The human player has no club. A career cannot start from a free agent."));
            }
        }
    }

    private static void CheckSquads(ContentBundle bundle, List<ContentIssue> issues)
    {
        // Duplicate keys are themselves a reported error, so these lookups must tolerate them
        // rather than throw — a validator that crashes on invalid input reports nothing at all.
        var leagueByTeam = FirstByKey(bundle.Teams, t => t.Key, t => t.LeagueKey);
        var tierByLeague = FirstByKey(bundle.Leagues, l => l.Key, l => l.Tier);

        var squadSizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ContentPlayer player in bundle.Players)
        {
            if (player.TeamKey is null)
                continue;
            squadSizes[player.TeamKey] = squadSizes.GetValueOrDefault(player.TeamKey) + 1;
        }

        foreach (ContentTeam team in bundle.Teams)
        {
            int size = squadSizes.GetValueOrDefault(team.Key);
            if (!leagueByTeam.TryGetValue(team.Key, out string? leagueKey)
                || !tierByLeague.TryGetValue(leagueKey, out SimulationTier tier))
            {
                continue;   // already reported as a dangling league reference
            }

            // Warning, not error: the rendered Tier 1 match wants a full XI, but a work in
            // progress must stay exportable. Blocking here would make the tool unusable
            // halfway through entering a squad.
            if (tier == SimulationTier.ActiveHuman && size < 11)
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = Codes.SquadTooSmall,
                    Category = ContentCategory.Teams,
                    EntityKey = team.Key,
                    Message = $"Only {size} player(s); a Tier 1 club needs 11 to field a full side.",
                });
            }
        }

        foreach (ContentLeague league in bundle.Leagues)
        {
            int teamCount = bundle.Teams.Count(t => string.Equals(t.LeagueKey, league.Key, StringComparison.Ordinal));
            if (teamCount < 2)
            {
                issues.Add(new ContentIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = Codes.LeagueTooSmall,
                    Category = ContentCategory.Leagues,
                    EntityKey = league.Key,
                    Message = $"Only {teamCount} club(s); a league needs at least 2 to produce fixtures.",
                });
            }
        }
    }

    private static void RequireText(
        string? value, string category, string entityKey, string field, List<ContentIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(Error(Codes.Required, category, entityKey, field, "Must not be empty."));
    }

    private static void RequireRange(
        int value, int min, int max, string category, string entityKey, string field, List<ContentIssue> issues)
    {
        if (value < min || value > max)
        {
            issues.Add(Error(Codes.Range, category, entityKey, field,
                $"Must be between {min} and {max}, got {value}."));
        }
    }

    private static void RequireRef(
        string? value,
        IReadOnlySet<string> known,
        string category,
        string entityKey,
        string field,
        string targetCategory,
        List<ContentIssue> issues)
    {
        if (string.IsNullOrEmpty(value))
        {
            issues.Add(Error(Codes.Required, category, entityKey, field,
                $"Must reference a {targetCategory} key."));
            return;
        }

        if (!known.Contains(value))
        {
            issues.Add(Error(Codes.RefDangling, category, entityKey, field,
                $"'{value}' does not match any key in {targetCategory}."));
        }
    }

    /// <summary>A reference that may legitimately be absent, but must resolve when present.</summary>
    private static void RequireOptionalRef(
        string? value,
        IReadOnlySet<string> known,
        string category,
        string entityKey,
        string field,
        string targetCategory,
        List<ContentIssue> issues)
    {
        if (value is null)
            return;

        RequireRef(value, known, category, entityKey, field, targetCategory, issues);
    }

    private static ContentIssue Error(string code, string category, string entityKey, string field, string message)
        => new()
        {
            Severity = IssueSeverity.Error,
            Code = code,
            Category = category,
            EntityKey = entityKey,
            Field = field,
            Message = message,
        };

    private static HashSet<string> ToKeySet<T>(IReadOnlyList<T> entities) where T : IContentEntity
        => new(entities.Select(e => e.Key).Where(k => !string.IsNullOrEmpty(k)), StringComparer.Ordinal);

    /// <summary>
    /// Like <c>ToDictionary</c>, but keeps the first entry when a key repeats instead of
    /// throwing. Duplicates are reported by <see cref="CheckIdentity"/>; the later rules just
    /// need to keep running so the user sees every problem in one pass.
    /// </summary>
    private static Dictionary<string, TValue> FirstByKey<T, TValue>(
        IReadOnlyList<T> entities, Func<T, string> key, Func<T, TValue> value)
    {
        var map = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (T entity in entities)
            map.TryAdd(key(entity) ?? string.Empty, value(entity));
        return map;
    }

    /// <summary>Stable rule codes. The UI links to them and the tests assert on them.</summary>
    public static class Codes
    {
        public const string KeyFormat = "KEY_FORMAT";
        public const string KeyDuplicate = "KEY_DUPLICATE";
        public const string IdInvalid = "ID_INVALID";
        public const string IdDuplicate = "ID_DUPLICATE";
        public const string Required = "REQUIRED";
        public const string Range = "RANGE";
        public const string EnumInvalid = "ENUM_INVALID";
        public const string RefDangling = "REF_DANGLING";
        public const string TraitDuplicate = "TRAIT_DUPLICATE";
        public const string SeasonDates = "SEASON_DATES";
        public const string SeasonCurrentDuplicate = "SEASON_CURRENT_DUPLICATE";
        public const string FixtureSameTeam = "FIXTURE_SAME_TEAM";
        public const string CareerNoTeam = "CAREER_NO_TEAM";
        public const string SquadTooSmall = "SQUAD_TOO_SMALL";
        public const string LeagueTooSmall = "LEAGUE_TOO_SMALL";
        public const string NationCode = "NATION_CODE";
        public const string CompetitionDivisions = "COMPETITION_DIVISIONS";
        public const string DuplicateHeadCoach = "DUPLICATE_HEAD_COACH";
        public const string ContractSubject = "CONTRACT_SUBJECT";
        public const string ContractDates = "CONTRACT_DATES";
        public const string ContractOverlap = "CONTRACT_OVERLAP";
    }
}
