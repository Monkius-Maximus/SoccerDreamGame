namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// One exported tab. <see cref="AuthoringOnly"/> marks the two audit tabs: they carry the
/// ReferenceAnchor — the REAL names the world deviates from — and the source workbook's own
/// LEIA-ME is explicit that they stay in the authoring repository and are never packaged into the
/// build. The dialog says so on the tab itself, where the decision is made.
/// </summary>
public sealed record CsvTab(
    string Name,
    bool AuthoringOnly,
    bool Importable,
    IReadOnlyList<string> Columns,
    Func<WorldSnapshot, IReadOnlyList<IReadOnlyList<string?>>> Rows)
{
    public string FileName => $"{Name}.csv";

    public string Write(WorldSnapshot world) => Csv.Write(Columns, Rows(world));
}

/// <summary>
/// The workbook, written back out: one file per tab, using the original column names so the
/// spreadsheet the tool replaced still opens the tool's output (ROADMAP.md Sprint 7 — "o Excel
/// volta a ser possível, mas como convidado, não como dono").
///
/// <para><b>Two deliberate departures from the original workbook.</b> The tool writes a header
/// row and data rows and nothing else: no title banners, no blank spacer rows, no merged
/// sections. A decorated sheet is unreadable to an importer, and the decoration was never data.
/// And <c>Competicao</c> is a table with one row per competition rather than the original's
/// key/value column pair, which was an artifact of there being exactly one.</para>
///
/// <para><b>Columns the tool never stored are not emitted.</b> The source JSON had already
/// dropped four of Audit_Jogadores' columns (anchorAgeApprox, generatedFullName, reviewedBy,
/// reviewDate) before Sprint 2 ever read it. Emitting them empty would make a lossy round trip
/// look complete.</para>
/// </summary>
public static class WorldCsv
{
    public static IReadOnlyList<CsvTab> Tabs { get; } =
    [
        ReadMe,
        Competitions,
        Clubs,
        KitsAndStadium,
        Players,
        ClubAudit,
        PlayerAudit,
        GeoNodes,
        Calibration,
        PositionWeights,
        Countries,
        Sources,
    ];

    public static CsvTab Tab(string name) =>
        Tabs.FirstOrDefault(tab => tab.Name == name)
        ?? throw new ArgumentException(
            $"'{name}' is not an export tab (there are {Tabs.Count}: {string.Join(", ", Tabs.Select(t => t.Name))}).",
            nameof(name));

    // ------------------------------------------------------------------ the tabs

    /// <summary>
    /// Generated, not round-tripped. The original LEIA-ME was prose about a spreadsheet that no
    /// longer exists; what a reader of this export needs is what THIS export contains and the IP
    /// warning that still applies. Writing it fresh keeps it true; storing it would let it rot.
    /// </summary>
    private static CsvTab ReadMe => new(
        "Leia-me",
        AuthoringOnly: false,
        Importable: false,
        ["secao", "texto"],
        world =>
        [
            ["Terra Paralela — Base de Mundo", "Exportado pela Ferramenta de Mundo."],
            ["Versão do schema", world.Meta.SchemaVersion],
            ["Semente-mestre", CsvValue.Number(world.Meta.MasterSeed)],
            ["Conteúdo", $"{world.Competitions.Count} competições, {world.Clubs.Count} clubes, "
                + $"{world.Characters.Count} jogadores, {world.GeoNodes.Count} nós geográficos, "
                + $"{world.Sources.Count} fontes."],
            ["HIGIENE DE IP — LEIA ANTES",
                "As abas Audit_Clubes e Audit_Jogadores contêm as ReferenceAnchor (nomes REAIS). "
                + "Elas ficam no repositório de autoria e NÃO SÃO EMPACOTADAS no build. O que vai "
                + "ao jogo são apenas Competicao, Clubes, Kits_Estadio e Jogadores, que não contêm "
                + "nenhum nome real de clube ou jogador."],
            ["Formato", "Separador ';' e vírgula decimal (Excel pt-BR). Listas unidas por '|'. "
                + "Campos com aspas, ';' ou quebra de linha vêm entre aspas duplas, com '\"\"' como escape."],
            ["Reimportar", "As abas Competicao, Clubes, Kits_Estadio, Jogadores, Audit_Clubes, "
                + "Audit_Jogadores, GeoNodes e Fontes voltam para a ferramenta. Leia-me, Calibracao, "
                + "Pesos_Posicao e Paises são só leitura."],
            ["Colunas ausentes", "Audit_Jogadores não traz anchorAgeApprox, generatedFullName, "
                + "reviewedBy nem reviewDate: a base de origem já não os tinha, e uma coluna vazia "
                + "faria um round-trip incompleto parecer completo."],
        ]);

    private static CsvTab Competitions => new(
        "Competicao",
        AuthoringOnly: false,
        Importable: true,
        [
            "competitionId", "name", "scope", "anchorGeoNodeId", "memberPredicateId", "prestigeBand",
            "leagueTierFloat", "format", "clubCount", "rounds", "promotedIn", "relegatedOut",
            "continentalSlots", "editionId", "season", "memberClubIds",
        ],
        world => world.Competitions.Select(competition => new string?[]
        {
            competition.CompetitionId,
            competition.Name,
            CsvValue.Enum(competition.Scope),
            competition.AnchorGeoNodeId,
            competition.MemberPredicateId,
            CsvValue.Enum(competition.PrestigeBand),
            CsvValue.Number(competition.LeagueTierFloat),
            competition.Format,
            CsvValue.Int(competition.ClubCount),
            CsvValue.Int(competition.Rounds),
            CsvValue.Int(competition.PromotedIn),
            CsvValue.Int(competition.RelegatedOut),
            competition.ContinentalSlots,
            competition.EditionId,
            CsvValue.Int(competition.Season),
            Csv.JoinArray(competition.MemberClubIds),
        }).ToList());

    private static CsvTab Clubs => new(
        "Clubes",
        AuthoringOnly: false,
        Importable: true,
        [
            "clubId", "displayCode", "officialName", "shortName", "nickname", "foundingYear",
            "cityName", "uf", "countryId", "geoNodeId", "districtArchetype", "prestigeBand",
            "clubStrength", "squadSize", "shieldShape", "centralCharge", "motto",
            "crestColor1", "crestColor2", "crestColor3",
            "palettePrimary", "paletteSecondary", "paletteTertiary", "typographyStyle",
            "stadiumName", "stadiumCapacity", "atmosphereArchetype", "pitchSurface",
            "defaultTacticalStyle", "tacticalStyleProvenance", "homeAdvantageModifier",
            "derbyRivalClubId", "namingRule",
        ],
        world => world.Clubs.Select(club => new string?[]
        {
            club.ClubId,
            club.DisplayCode,
            club.Identity.OfficialName,
            club.Identity.ShortName,
            club.Identity.Nickname,
            CsvValue.Int(club.Identity.FoundingYear),
            club.Geography.CityName,
            club.Geography.Uf,
            club.Geography.CountryId,
            club.Geography.GeoNodeId,
            CsvValue.Enum(club.Geography.DistrictArchetype),
            CsvValue.Enum(club.World.PrestigeBand),
            CsvValue.Number(club.World.ClubStrength),
            CsvValue.Int(club.World.SquadSize),
            CsvValue.Enum(club.Crest.ShieldShape),
            club.Crest.CentralCharge,
            club.Crest.Motto,
            Colour(club.Crest.Colors, 0),
            Colour(club.Crest.Colors, 1),
            Colour(club.Crest.Colors, 2),
            club.Palette.Primary,
            club.Palette.Secondary,
            club.Palette.Tertiary,
            CsvValue.Enum(club.Palette.TypographyStyle),
            club.Stadium.Name,
            CsvValue.Int(club.Stadium.Capacity),
            CsvValue.Enum(club.Stadium.AtmosphereArchetype),
            CsvValue.Enum(club.Stadium.PitchSurface),
            CsvValue.Enum(club.AiProfile.DefaultTacticalStyle),
            CsvValue.Enum(club.AiProfile.TacticalStyleProvenance),
            CsvValue.Number(club.AiProfile.HomeAdvantageModifier),
            club.AiProfile.DerbyRivalClubId,
            CsvValue.Enum(club.World.NamingRule),
        }).ToList());

    private static CsvTab KitsAndStadium => new(
        "Kits_Estadio",
        AuthoringOnly: false,
        Importable: true,
        [
            "clubId", "displayCode", "shortName", "collarStyle", "fitStyle",
            "homePattern", "homeShirt", "homeShorts", "homeSocks", "homeLuminance",
            "awayPattern", "awayShirt", "awayShorts", "awaySocks",
            "deltaE_home_away", "deltaE_threshold", "polarityRule",
        ],
        world => world.Clubs.Select(club => new string?[]
        {
            club.ClubId,
            // displayCode and shortName repeat the Clubes tab so a reader can tell the rows apart
            // without a lookup. They are ignored on import: the club owns them.
            club.DisplayCode,
            club.Identity.ShortName,
            CsvValue.Enum(club.Kits.CollarStyle),
            CsvValue.Enum(club.Kits.FitStyle),
            CsvValue.Enum(club.Kits.Home.FabricPattern),
            club.Kits.Home.Shirt,
            club.Kits.Home.Shorts,
            club.Kits.Home.Socks,
            CsvValue.Number(club.Kits.Home.Luminance),
            CsvValue.Enum(club.Kits.Away.FabricPattern),
            club.Kits.Away.Shirt,
            club.Kits.Away.Shorts,
            club.Kits.Away.Socks,
            CsvValue.Number(club.Kits.DeltaE),
            CsvValue.Number(club.Kits.DeltaEThreshold),
            club.Kits.PolarityRule,
        }).ToList());

    private static CsvTab Players => new(
        "Jogadores",
        AuthoringOnly: false,
        Importable: true,
        [
            "playerId", "clubId", "clubShortName", "prestigeBand", "shirtNumber",
            "firstName", "lastName", "shirtName", "nationality", "secondNationality",
            "dateOfBirth", "age", "phase", "squadRole", "primaryPosition", "secondaryPositions",
            "preferredFoot", "weakFootRating", "skillMovesRating", "height", "buildType",
            "Finishing", "Passing", "Dribbling", "Tackling", "Pace", "Strength", "Stamina",
            "Positioning", "Vision", "Composure", "Reflexes", "Handling",
            "potentialGap", "provenance", "overall", "potentialOverall",
            "marketValueEUR", "salaryMonthlyBRL",
        ],
        world =>
        {
            Dictionary<string, ClubIdentity> clubs = world.Clubs.ToDictionary(club => club.ClubId);

            return world.Characters.Select(player =>
            {
                clubs.TryGetValue(player.ClubId, out ClubIdentity? club);

                var row = new List<string?>
                {
                    player.PlayerId,
                    player.ClubId,
                    // Both denormalised from the club, both ignored on import.
                    club?.Identity.ShortName,
                    club is null ? null : CsvValue.Enum(club.World.PrestigeBand),
                    CsvValue.Int(player.ShirtNumber),
                    player.FirstName,
                    player.LastName,
                    player.ShirtName,
                    player.Nationality,
                    player.SecondNationality,
                    CsvValue.Date(player.DateOfBirth),
                    CsvValue.Int(player.Age),
                    CsvValue.Enum(player.Phase),
                    CsvValue.Enum(player.SquadRole),
                    CsvValue.Enum(player.PrimaryPosition),
                    Csv.JoinArray(player.SecondaryPositions.Select(position => position.ToString())),
                    CsvValue.Enum(player.PreferredFoot),
                    CsvValue.Int(player.WeakFootRating),
                    CsvValue.Int(player.SkillMovesRating),
                    CsvValue.Int(player.Height),
                    CsvValue.Enum(player.BuildType),
                };

                foreach (Attr attr in Enum.GetValues<Attr>())
                    row.Add(CsvValue.Int(player.Attrs[attr]));

                row.Add(CsvValue.Int(player.PotentialGap));
                row.Add(CsvValue.Enum(player.Provenance));
                row.Add(CsvValue.Int(player.Overall));
                row.Add(CsvValue.Int(player.PotentialOverall));
                row.Add(CsvValue.Int(player.MarketValueEur));
                row.Add(CsvValue.Int(player.SalaryMonthlyBrl));

                return (IReadOnlyList<string?>)row;
            }).ToList();
        });

    private static CsvTab ClubAudit => new(
        "Audit_Clubes",
        AuthoringOnly: true,
        Importable: true,
        [
            "clubId", "anchorClubName", "anchorCityName", "anchorFoundingYear",
            "generatedFoundingYear", "foundingDecadePreserved", "foundingSourceCitation",
            "anchorNickname", "nicknameCommercialLevel", "nicknameTrademarked", "nicknameEvidence",
            "namingRule", "namingRuleReason", "phoneticSimilarity",
            "crest_originalChargeReplaced", "crest_substituteCharge", "crest_sourceCitation",
            "districtSourceCitation", "tacticalStyleProvenance", "tacticalStyleEvidence",
            "chromaticPolicy", "anchorFactsVerified", "reviewedBy", "reviewDate", "note",
        ],
        world => world.Clubs.Select(club =>
        {
            ClubDeviationAudit audit = club.Audit;
            return new string?[]
            {
                audit.ClubId,
                audit.AnchorClubName,
                audit.AnchorCityName,
                CsvValue.Int(audit.AnchorFoundingYear),
                CsvValue.Int(audit.GeneratedFoundingYear),
                CsvValue.Int(audit.FoundingDecadePreserved),
                audit.FoundingSourceCitation,
                audit.AnchorNickname,
                CsvValue.Int(audit.NicknameCommercialLevel),
                CsvValue.Flag(audit.NicknameTrademarked),
                audit.NicknameEvidence,
                CsvValue.Enum(audit.NamingRule),
                audit.NamingRuleReason,
                CsvValue.Number(audit.PhoneticSimilarity),
                audit.CrestOriginalChargeReplaced,
                audit.CrestSubstituteCharge,
                audit.CrestSourceCitation,
                audit.DistrictSourceCitation,
                CsvValue.Enum(audit.TacticalStyleProvenance),
                audit.TacticalStyleEvidence,
                audit.ChromaticPolicy,
                CsvValue.Flag(audit.AnchorFactsVerified),
                audit.ReviewedBy,
                audit.ReviewDate,
                audit.Note,
            };
        }).ToList());

    private static CsvTab PlayerAudit => new(
        "Audit_Jogadores",
        AuthoringOnly: true,
        Importable: true,
        [
            "playerId", "clubId", "provenance", "anchorPlayerName", "anchorNationality",
            "deviationFromSurname", "generatedSurname", "phoneticSimilarity", "deviationMethod",
            "anchorFactsVerified",
        ],
        world => world.Characters.Select(player =>
        {
            CharacterDeviationAudit audit = player.Audit;
            return new string?[]
            {
                player.PlayerId,
                player.ClubId,
                CsvValue.Enum(player.Provenance),
                audit.AnchorPlayerName,
                audit.AnchorNationality,
                audit.DeviationFromSurname,
                audit.GeneratedSurname,
                CsvValue.Number(audit.PhoneticSimilarity),
                audit.DeviationMethod,
                CsvValue.Flag(audit.AnchorFactsVerified),
            };
        }).ToList());

    private static CsvTab GeoNodes => new(
        "GeoNodes",
        AuthoringOnly: false,
        Importable: true,
        ["geoNodeId", "kind", "parentId", "displayName"],
        world => world.GeoNodes.Select(node => new string?[]
        {
            node.GeoNodeId,
            CsvValue.Enum(node.Kind),
            node.ParentId,
            node.DisplayName,
        }).ToList());

    /// <summary>
    /// Read-only on the way back in: calibration is edited on its own screen, where a change is
    /// shown against everything it moves. A spreadsheet cannot show that, and a re-fit constant
    /// silently rewrites the economy of all 688 players.
    /// </summary>
    private static CsvTab Calibration => new(
        "Calibracao",
        AuthoringOnly: false,
        Importable: false,
        ["grupo", "chave", "valor", "unidade", "origem"],
        world =>
        {
            var rows = new List<IReadOnlyList<string?>>();

            foreach ((string key, CalibrationConstant constant) in world.Calibration.Constants)
                rows.Add(["constante", key, CsvValue.Number(constant.Value), constant.Unit, constant.Note]);

            foreach (AgeMultStep step in world.Calibration.AgeMult)
                rows.Add(["ageMult", CsvValue.Int(step.Age), CsvValue.Number(step.Multiplier), "multiplicador", ""]);

            foreach ((PrestigeBand band, PrestigeBandCalibration calibration) in world.Calibration.Bands)
            {
                rows.Add(["banda", $"{band}.valueMult", CsvValue.Number(calibration.ValueMult), "multiplicador", $"n={calibration.N}"]);
                rows.Add(["banda", $"{band}.capMean", CsvValue.Number(calibration.CapMean), "lugares", $"n={calibration.N}"]);
                rows.Add(["banda", $"{band}.capSd", CsvValue.Number(calibration.CapSd), "lugares", $"n={calibration.N}"]);
            }

            foreach ((AtmosphereArchetype atmosphere, double advantage) in world.Calibration.HomeAdv)
                rows.Add(["homeAdv", CsvValue.Enum(atmosphere), CsvValue.Number(advantage), "PPG", ""]);

            return rows;
        });

    private static CsvTab PositionWeights => new(
        "Pesos_Posicao",
        AuthoringOnly: false,
        Importable: false,
        ["position", .. Enum.GetValues<Attr>().Select(attr => attr.ToString()), "soma"],
        world => world.Calibration.PositionWeights.Select(entry =>
        {
            var row = new List<string?> { entry.Key.ToString() };
            double sum = 0;

            foreach (Attr attr in Enum.GetValues<Attr>())
            {
                double weight = entry.Value.TryGetValue(attr, out double value) ? value : 0;
                sum += weight;
                row.Add(CsvValue.Number(weight));
            }

            row.Add(CsvValue.Number(sum));
            return (IReadOnlyList<string?>)row;
        }).ToList());

    /// <summary>
    /// New data that never existed in the workbook: the stadium-capacity distribution per country,
    /// which is what tells the tool whether a stadium is plausible for where it stands. A country
    /// without one cannot be populated (ROADMAP.md Sprint 9).
    /// </summary>
    private static CsvTab Countries => new(
        "Paises",
        AuthoringOnly: false,
        Importable: false,
        ["countryId", "displayName", "capacityMean", "capacitySd", "capacityMin", "capacityMax", "clubes"],
        world =>
        {
            Dictionary<string, string> names = CountryNames(world);
            ILookup<string, ClubIdentity> byCountry = world.Clubs.ToLookup(club => club.Geography.CountryId);

            return world.Calibration.StadiumProfile.Select(entry => (IReadOnlyList<string?>)new string?[]
            {
                entry.Key,
                names.TryGetValue(entry.Key, out string? name) ? name : null,
                CsvValue.Number(entry.Value.Mean),
                CsvValue.Number(entry.Value.Sd),
                CsvValue.Number(entry.Value.Min),
                CsvValue.Number(entry.Value.Max),
                CsvValue.Int(byCountry[entry.Key].Count()),
            }).ToList();
        });

    private static CsvTab Sources => new(
        "Fontes",
        AuthoringOnly: false,
        Importable: true,
        ["tema", "numero", "fonte", "url"],
        world => world.Sources
            .Select(source => (IReadOnlyList<string?>)new string?[] { source.Tema, source.Numero, source.Fonte, source.Url })
            .ToList());

    /// <summary>The crest's colours are a list; the workbook spread them over three columns, and
    /// a club with fewer leaves the rest empty rather than repeating one.</summary>
    private static string? Colour(IReadOnlyList<string> colours, int index) =>
        index < colours.Count ? colours[index] : null;

    /// <summary>
    /// A readable name per ISO country code. The code ("BRA") and the geo node ("geo_bra") are
    /// different identifiers for the same place and nothing links them directly, so the link is
    /// made the way the data actually connects: a club knows both its country code and its node,
    /// and the node's Country-kind ancestor carries the name.
    /// </summary>
    private static Dictionary<string, string> CountryNames(WorldSnapshot world)
    {
        Dictionary<string, GeoNode> nodes = world.GeoNodes.ToDictionary(node => node.GeoNodeId);
        var names = new Dictionary<string, string>();

        foreach (ClubIdentity club in world.Clubs)
        {
            if (names.ContainsKey(club.Geography.CountryId))
                continue;

            GeoNode? node = nodes.GetValueOrDefault(club.Geography.GeoNodeId);
            while (node is not null && node.Kind != GeoNodeKind.Country)
                node = node.ParentId is null ? null : nodes.GetValueOrDefault(node.ParentId);

            if (node is not null)
                names[club.Geography.CountryId] = node.DisplayName;
        }

        return names;
    }
}
