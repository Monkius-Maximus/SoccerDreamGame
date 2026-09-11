using System.Text.RegularExpressions;
using SoccerSim.Core.World.Competitions;

namespace SoccerSim.Core.World.Validation;

/// <summary>What a finding is about, so the report can send the user to the right screen.</summary>
public enum FindingScope { World, Club, Player, GeoNode, Source }

/// <summary>
/// One finding with its subject attached. The club-page <see cref="Finding"/> already knows its
/// club from context; a batch finding does not, so it carries the id it is about and a readable
/// label — which is what makes the row clickable.
/// </summary>
public sealed record BatchFinding(
    FindingLevel Level,
    string Code,
    string Label,
    string Detail,
    FindingScope Scope,
    string EntityId,
    string EntityLabel);

/// <summary>The seven numbers the audit strip shows, and whether the batch is releasable.</summary>
public sealed record BatchAuditReport(
    IReadOnlyList<BatchFinding> Findings,
    int ClubsScanned,
    int PlayersScanned,
    int SourcesRegistered)
{
    public int Errors => Findings.Count(finding => finding.Level == FindingLevel.Error);

    public int Warnings => Findings.Count(finding => finding.Level == FindingLevel.Warning);

    /// <summary>How many clubs carry at least one finding — the number that says whether a
    /// problem is local or systemic.</summary>
    public int ClubsAffected => Findings
        .Where(finding => finding.Scope == FindingScope.Club)
        .Select(finding => finding.EntityId)
        .Distinct()
        .Count();

    /// <summary>
    /// The gate. An error blocks the batch; warnings do not. This is the one place in the tool
    /// that says "no" — the club page deliberately shows errors without blocking edits, because a
    /// user has to be able to save a half-finished club. The batch is where that debt comes due.
    /// </summary>
    public bool Released => Errors == 0;

    public IReadOnlyList<string> Codes => Findings
        .Select(finding => finding.Code)
        .Distinct()
        .OrderBy(code => code, StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// The batch sweep (ROADMAP.md Sprint 8): "número sem fonte não entra" made checkable over the
/// whole world at once instead of club by club.
///
/// <para>Sixteen codes. Six come from <see cref="ClubInvariants"/> — the same checks the club
/// page shows, run over every club — and fifteen are batch-only, because they are about
/// relationships that no single club can see: a duplicate code, a rivalry declared one way, a
/// pointer into a geo tree, an economy that has drifted from the calibration.</para>
///
/// <para><b>Only findings are returned.</b> The club page lists its passing checks so a user can
/// see what was verified; a batch listing 20 clubs × 6 passing checks would bury the four rows
/// that matter.</para>
/// </summary>
public static class BatchAudit
{
    /// <summary>Three uppercase letters. The code is a display token, and a batch where some are
    /// three letters and some are four is a batch where columns stop lining up.</summary>
    private static readonly Regex DisplayCodeFormat = new("^[A-Z]{3}$", RegexOptions.Compiled);

    /// <summary>Which kind of node may parent which. A country can hang off a confederation
    /// directly or off a sub-region; a city off a region or, for a city-state, a country.</summary>
    private static readonly IReadOnlyDictionary<GeoNodeKind, GeoNodeKind[]> AllowedParents =
        new Dictionary<GeoNodeKind, GeoNodeKind[]>
        {
            [GeoNodeKind.World] = [],
            [GeoNodeKind.Confederation] = [GeoNodeKind.World],
            [GeoNodeKind.SubRegion] = [GeoNodeKind.Confederation],
            [GeoNodeKind.Country] = [GeoNodeKind.Confederation, GeoNodeKind.SubRegion],
            [GeoNodeKind.Region] = [GeoNodeKind.Country],
            [GeoNodeKind.City] = [GeoNodeKind.Region, GeoNodeKind.Country],
        };

    /// <summary>The five citation fields. This is the check that turns the project's premise —
    /// no number without a source — into something a sweep can fail on.</summary>
    private static readonly (string Field, Func<ClubDeviationAudit, string?> Read)[] Citations =
    [
        ("audit.foundingSourceCitation", audit => audit.FoundingSourceCitation),
        ("audit.nicknameEvidence", audit => audit.NicknameEvidence),
        ("audit.crest_sourceCitation", audit => audit.CrestSourceCitation),
        ("audit.districtSourceCitation", audit => audit.DistrictSourceCitation),
        ("audit.tacticalStyleEvidence", audit => audit.TacticalStyleEvidence),
    ];

    /// <summary>
    /// The sweep over the world, plus whatever country and pyramid data exists beside it. The
    /// country profiles and divisions live in their own tables rather than in the world document,
    /// so they are passed in rather than read off the snapshot.
    /// </summary>
    public static BatchAuditReport Run(
        WorldSnapshot world,
        IReadOnlyList<CountryProfile> countries,
        IReadOnlyList<LeaguePyramid> pyramids)
    {
        BatchAuditReport report = Run(world);
        var findings = report.Findings.ToList();

        AuditCountries(findings, world, countries);

        foreach (LeaguePyramid pyramid in pyramids)
        {
            foreach (Finding finding in PyramidRules.Check(pyramid))
            {
                findings.Add(new BatchFinding(finding.Level, finding.Code, finding.Label, finding.Detail,
                    FindingScope.World, pyramid.CountryId, pyramid.CountryId));
            }
        }

        return report with { Findings = findings };
    }

    /// <summary>
    /// A country the world has clubs in but knows nothing about cannot be populated: the
    /// generator would have to invent where its players come from (ROADMAP.md Sprint 9 — "País
    /// sem distribuição é erro").
    /// </summary>
    private static void AuditCountries(
        List<BatchFinding> findings,
        WorldSnapshot world,
        IReadOnlyList<CountryProfile> countries)
    {
        Dictionary<string, CountryProfile> byId = countries.ToDictionary(country => country.CountryId);

        foreach (string countryId in world.Clubs.Select(club => club.Geography.CountryId).Distinct().Order())
        {
            if (!byId.TryGetValue(countryId, out CountryProfile? country))
            {
                findings.Add(new BatchFinding(FindingLevel.Error, "COUNTRY_MISSING", "País sem perfil",
                    $"{countryId} tem clubes mas nenhum perfil — sem moeda, piso salarial nem distribuição de nacionalidade",
                    FindingScope.World, countryId, countryId));
                continue;
            }

            if (country.NationalityMix.Count == 0)
            {
                findings.Add(new BatchFinding(FindingLevel.Error, "NATIONALITY_MISSING", "Sem distribuição de nacionalidade",
                    $"{countryId} não pode ser povoado: o gerador teria que inventar de onde vêm os jogadores",
                    FindingScope.World, countryId, countryId));
                continue;
            }

            if (!country.SharesBalance)
            {
                findings.Add(new BatchFinding(FindingLevel.Warning, "NATIONALITY_SUM", "Distribuição não fecha",
                    $"{countryId} soma {country.ShareSum:0.###} em vez de 1",
                    FindingScope.World, countryId, countryId));
            }

            // Measured from the batch it will go on to generate. Not evidence about the world,
            // and the sweep says so rather than letting it pass as sourced.
            if (country.NationalityMixSource is null)
            {
                findings.Add(new BatchFinding(FindingLevel.Warning, "NATIONALITY_UNSOURCED", "Distribuição sem fonte externa",
                    $"a mistura de {countryId} foi medida do próprio lote — isso não é evidência sobre o mundo",
                    FindingScope.World, countryId, countryId));
            }
        }
    }

    public static BatchAuditReport Run(WorldSnapshot world)
    {
        var findings = new List<BatchFinding>();

        Dictionary<string, ClubIdentity> clubs = world.Clubs.ToDictionary(club => club.ClubId);
        Dictionary<string, GeoNode> nodes = world.GeoNodes.ToDictionary(node => node.GeoNodeId);
        ILookup<string, CharacterRecord> squads = world.Characters.ToLookup(player => player.ClubId);

        var duplicateCodes = world.Clubs
            .GroupBy(club => club.DisplayCode, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (ClubIdentity club in world.Clubs)
        {
            string label = club.Identity.ShortName;

            // The six club-page checks, run over the batch. Only the failures survive.
            foreach (Finding finding in ClubInvariants.Check(club, world.Calibration))
            {
                if (finding.Level != FindingLevel.Ok)
                    findings.Add(Club(finding.Level, finding.Code, finding.Label, finding.Detail, club));
            }

            AuditCode(findings, club, duplicateCodes);
            AuditFounding(findings, club);
            AuditCitations(findings, club);
            AuditDerby(findings, club, clubs);
            AuditGeography(findings, club, nodes);
            AuditSquad(findings, club, squads[club.ClubId].ToList(), world.Calibration);
        }

        AuditGeoNodes(findings, world, nodes);

        return new BatchAuditReport(
            findings,
            world.Clubs.Count,
            world.Characters.Count,
            world.Sources.Count);
    }

    // ------------------------------------------------------------------- clubs

    private static void AuditCode(
        List<BatchFinding> findings,
        ClubIdentity club,
        IReadOnlyDictionary<string, int> duplicates)
    {
        if (duplicates.TryGetValue(club.DisplayCode, out int count))
        {
            findings.Add(Club(FindingLevel.Error, "CODE_DUP", "Código de exibição duplicado",
                $"'{club.DisplayCode}' aparece em {count} clubes", club));
        }

        if (!DisplayCodeFormat.IsMatch(club.DisplayCode))
        {
            findings.Add(Club(FindingLevel.Warning, "CODE_FORMAT", "Formato do código de exibição",
                $"'{club.DisplayCode}' não são três letras maiúsculas", club));
        }
    }

    private static void AuditFounding(List<BatchFinding> findings, ClubIdentity club)
    {
        ClubDeviationAudit audit = club.Audit;

        // The whole point of the anchor is that the world DEVIATES from it. A generated year
        // identical to the real one is a club that quietly asserts a real fact.
        if (audit.GeneratedFoundingYear == audit.AnchorFoundingYear)
        {
            findings.Add(Club(FindingLevel.Warning, "FOUNDING_EQ", "Fundação igual à âncora",
                $"{audit.GeneratedFoundingYear} é exatamente o ano real", club));
        }

        // foundingDecadePreserved is the DECADE (1890, 1900, …), not a 0/1 flag — all twenty
        // clubs in the batch spell it that way. The rule it encodes: the year may move, but the
        // decade is the fact the deviation is not allowed to touch.
        int declared = audit.FoundingDecadePreserved;

        foreach ((string field, int year) in new[]
                 {
                     ("anchorFoundingYear", audit.AnchorFoundingYear),
                     ("generatedFoundingYear", audit.GeneratedFoundingYear),
                 })
        {
            if (year / 10 * 10 != declared)
            {
                findings.Add(Club(FindingLevel.Warning, "FOUNDING_DECADE", "Década de fundação",
                    $"{field} = {year} está fora da década declarada ({declared})", club));
            }
        }

        if (!audit.AnchorFactsVerified)
        {
            findings.Add(Club(FindingLevel.Error, "ANCHOR_UNVERIFIED", "Âncora não verificada",
                "audit.anchorFactsVerified = 0 — o clube não pode entrar no lote", club));
        }
    }

    private static void AuditCitations(List<BatchFinding> findings, ClubIdentity club)
    {
        foreach ((string field, Func<ClubDeviationAudit, string?> read) in Citations)
        {
            if (string.IsNullOrWhiteSpace(read(club.Audit)))
            {
                findings.Add(Club(FindingLevel.Error, "CITATION_MISSING", "Citação ausente",
                    $"{field} está vazio", club));
            }
        }
    }

    private static void AuditDerby(
        List<BatchFinding> findings,
        ClubIdentity club,
        IReadOnlyDictionary<string, ClubIdentity> clubs)
    {
        if (club.AiProfile.DerbyRivalClubId is not { } rivalId)
            return;

        if (!clubs.TryGetValue(rivalId, out ClubIdentity? rival))
        {
            findings.Add(Club(FindingLevel.Error, "DERBY_DANGLING", "Clássico apontando para o nada",
                $"derbyRivalClubId = '{rivalId}', que não é um clube do lote", club));
            return;
        }

        // A derby is a relationship, not an attribute. One side naming the other and not being
        // named back means one of the two is wrong, and no club page can see it.
        if (rival.AiProfile.DerbyRivalClubId != club.ClubId)
        {
            string back = rival.AiProfile.DerbyRivalClubId is { } other && clubs.TryGetValue(other, out ClubIdentity? backClub)
                ? backClub.Identity.ShortName
                : "ninguém";

            findings.Add(Club(FindingLevel.Warning, "DERBY_ONE_WAY", "Clássico em mão única",
                $"aponta para {rival.Identity.ShortName}, que aponta para {back}", club));
        }
    }

    private static void AuditGeography(
        List<BatchFinding> findings,
        ClubIdentity club,
        IReadOnlyDictionary<string, GeoNode> nodes)
    {
        if (!nodes.ContainsKey(club.Geography.GeoNodeId))
        {
            findings.Add(Club(FindingLevel.Error, "GEO_DANGLING", "Nó geográfico inexistente",
                $"geography.geoNodeId = '{club.Geography.GeoNodeId}'", club));
        }
    }

    private static void AuditSquad(
        List<BatchFinding> findings,
        ClubIdentity club,
        IReadOnlyList<CharacterRecord> squad,
        WorldCalibration calibration)
    {
        if (squad.Count == 0)
        {
            findings.Add(Club(FindingLevel.Error, "SQUAD_EMPTY", "Clube sem elenco",
                "sem jogadores não há OVR, valor nem folha — a competição não sabe avaliá-lo", club));
            return;
        }

        if (squad.Count != club.World.SquadSize)
        {
            findings.Add(Club(FindingLevel.Warning, "SQUAD_SIZE", "Tamanho do elenco",
                $"{squad.Count} jogadores, mas world.squadSize diz {club.World.SquadSize}", club));
        }

        var duplicateShirts = squad
            .GroupBy(player => player.ShirtNumber)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(number => number)
            .ToList();

        if (duplicateShirts.Count > 0)
        {
            findings.Add(Club(FindingLevel.Error, "SHIRT_DUP", "Camisas repetidas",
                $"números {string.Join(", ", duplicateShirts)} aparecem mais de uma vez", club));
        }

        // A stored economy that no longer follows from the calibration. Shown, never silently
        // corrected: this is how a user learns a re-fit aged the batch (Sprint 9 recalculates).
        int stale = squad.Count(player => IsStale(player, club.World.PrestigeBand, calibration));
        if (stale > 0)
        {
            findings.Add(Club(FindingLevel.Warning, "ECONOMY_STALE", "Economia desatualizada",
                $"{stale} de {squad.Count} jogadores divergem do que a calibração produz agora", club));
        }
    }

    private static bool IsStale(CharacterRecord player, PrestigeBand band, WorldCalibration calibration)
    {
        CharacterRecord fresh = WorldDerivations.Recalculate(player, band, calibration);

        return fresh.Overall != player.Overall
            || fresh.PotentialOverall != player.PotentialOverall
            || fresh.MarketValueEur != player.MarketValueEur
            || fresh.SalaryMonthlyBrl != player.SalaryMonthlyBrl;
    }

    // ---------------------------------------------------------------- geo nodes

    /// <summary>
    /// Audits the NODES, not only the pointers into them. This is the check that caught the
    /// spreadsheet's legend row — a GeoNode with no kind and no name that an extractor swallowed
    /// (DATA_CONTRACT.md §5). An audit that only validates references never finds corrupt data at
    /// the far end of one.
    /// </summary>
    private static void AuditGeoNodes(
        List<BatchFinding> findings,
        WorldSnapshot world,
        IReadOnlyDictionary<string, GeoNode> nodes)
    {
        foreach (GeoNode node in world.GeoNodes)
        {
            string label = string.IsNullOrWhiteSpace(node.DisplayName) ? node.GeoNodeId : node.DisplayName;

            if (!Enum.IsDefined(node.Kind) || string.IsNullOrWhiteSpace(node.DisplayName))
            {
                findings.Add(new BatchFinding(FindingLevel.Error, "GEO_NODE_MALFORMED", "Nó geográfico corrompido",
                    "todo nó precisa de kind dentro do enum e de displayName",
                    FindingScope.GeoNode, node.GeoNodeId, label));
                continue;
            }

            if (node.Kind == GeoNodeKind.World)
            {
                if (node.ParentId is not null)
                {
                    findings.Add(new BatchFinding(FindingLevel.Error, "GEO_NODE_INVALID", "Hierarquia geográfica",
                        "a raiz World não pode ter parentId",
                        FindingScope.GeoNode, node.GeoNodeId, label));
                }

                continue;
            }

            if (node.ParentId is null || !nodes.TryGetValue(node.ParentId, out GeoNode? parent))
            {
                findings.Add(new BatchFinding(FindingLevel.Error, "GEO_NODE_INVALID", "Hierarquia geográfica",
                    node.ParentId is null ? $"{node.Kind} sem parentId" : $"parentId '{node.ParentId}' não existe",
                    FindingScope.GeoNode, node.GeoNodeId, label));
                continue;
            }

            if (!AllowedParents[node.Kind].Contains(parent.Kind))
            {
                findings.Add(new BatchFinding(FindingLevel.Error, "GEO_NODE_INVALID", "Hierarquia geográfica",
                    $"{node.Kind} pendurado em {parent.Kind} "
                    + $"(esperado {string.Join(" ou ", AllowedParents[node.Kind])})",
                    FindingScope.GeoNode, node.GeoNodeId, label));
            }
        }
    }

    private static BatchFinding Club(FindingLevel level, string code, string label, string detail, ClubIdentity club) =>
        new(level, code, label, detail, FindingScope.Club, club.ClubId, club.Identity.ShortName);
}
