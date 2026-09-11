namespace SoccerSim.Core.World.Serialization;

/// <summary>One uploaded file: which tab it is and what is in it.</summary>
public sealed record CsvTabFile(string Tab, string Content);

/// <summary>
/// The world a set of CSV tabs would produce, and every difference from the world that is loaded.
/// Nothing has been written: this is what the preview shows, and applying it writes exactly this.
/// </summary>
public sealed record WorldImportPlan(
    WorldSnapshot Result,
    IReadOnlyList<WorldChange> Changes,
    IReadOnlyList<string> Tabs)
{
    public int Added => Changes.Count(change => change.Change == ChangeKind.Added);

    public int Changed => Changes.Count(change => change.Change == ChangeKind.Changed);

    public int Removed => Changes.Count(change => change.Change == ChangeKind.Removed);
}

/// <summary>
/// Reads a set of exported tabs back into the world (ROADMAP.md Sprint 7). Strict and
/// all-or-nothing, like the JSON importer: a document with any malformed cell is rejected in
/// full, with one message per cell, and nothing is written.
///
/// <para><b>Primary and auxiliary tabs.</b> A tab that carries an entity's identity — Competicao,
/// Clubes, Jogadores, GeoNodes, Fontes — decides which rows EXIST: a row missing from it is a
/// removal. The rest (Kits_Estadio, Audit_Clubes, Audit_Jogadores) describe part of an entity and
/// can only update rows that exist in the result. So adding a club means importing Clubes,
/// Kits_Estadio and Audit_Clubes together; importing Clubes alone with a new id is refused by
/// name rather than half-built out of defaults.</para>
/// </summary>
public static class WorldCsvImport
{
    /// <summary>Tabs that decide which rows exist, and therefore which rows are gone.</summary>
    public static readonly string[] PrimaryTabs = ["Competicao", "Clubes", "Jogadores", "GeoNodes", "Fontes"];

    public static WorldImportPlan Plan(WorldSnapshot current, IReadOnlyList<CsvTabFile> files)
    {
        var errors = new List<string>();
        var parsed = new Dictionary<string, IReadOnlyList<CsvRow>>();

        foreach (CsvTabFile file in files)
        {
            CsvTab tab;
            try
            {
                tab = WorldCsv.Tab(file.Tab);
            }
            catch (ArgumentException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            if (!tab.Importable)
            {
                errors.Add($"{tab.FileName}: this tab is read-only. "
                    + "Leia-me is generated, and Calibracao, Pesos_Posicao and Paises are edited on "
                    + "the calibration screen, where a change is shown against everything it moves.");
                continue;
            }

            IReadOnlyList<CsvRow>? rows = ParseRows(tab, file.Content, errors);
            if (rows is not null)
                parsed[tab.Name] = rows;
        }

        if (errors.Count > 0)
            throw new WorldImportException(errors);

        WorldSnapshot proposed = Build(current, parsed, errors);

        if (errors.Count > 0)
            throw new WorldImportException(errors);

        return new WorldImportPlan(
            proposed,
            WorldCsvDiff.Compare(current, proposed, parsed.Keys),
            parsed.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList());
    }

    // ------------------------------------------------------------------ parsing

    private static IReadOnlyList<CsvRow>? ParseRows(CsvTab tab, string content, List<string> errors)
    {
        IReadOnlyList<IReadOnlyList<string>> lines = Csv.Read(content);

        if (lines.Count == 0)
        {
            errors.Add($"{tab.FileName}: the file is empty; a tab needs at least its header row.");
            return null;
        }

        IReadOnlyList<string> header = lines[0];
        var missing = tab.Columns.Where(column => !header.Contains(column)).ToList();

        if (missing.Count > 0)
        {
            errors.Add($"{tab.FileName}:1 · header is missing {missing.Count} column(s): {string.Join(", ", missing)}");
            return null;
        }

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
            columns.TryAdd(header[i], i);

        var rows = new List<CsvRow>(lines.Count - 1);
        for (int i = 1; i < lines.Count; i++)
        {
            // A row shorter than the header is a truncated line, not an empty cell — say so on
            // the line it happened, which is the only way to find it in a 688-row file.
            if (lines[i].Count < header.Count)
            {
                errors.Add($"{tab.FileName}:{i + 1} · has {lines[i].Count} cells, the header has {header.Count}");
                continue;
            }

            rows.Add(new CsvRow(tab.Name, i + 1, columns, lines[i]));
        }

        return rows;
    }

    // ----------------------------------------------------------------- building

    private static WorldSnapshot Build(
        WorldSnapshot current,
        IReadOnlyDictionary<string, IReadOnlyList<CsvRow>> tabs,
        List<string> errors)
    {
        IReadOnlyList<GeoNode> geoNodes = tabs.TryGetValue("GeoNodes", out IReadOnlyList<CsvRow>? geoRows)
            ? Collect(geoRows, errors, ReadGeoNode)
            : current.GeoNodes;

        IReadOnlyList<WorldSource> sources = tabs.TryGetValue("Fontes", out IReadOnlyList<CsvRow>? sourceRows)
            ? Collect(sourceRows, errors, ReadSource)
            : current.Sources;

        IReadOnlyList<Competition> competitions = tabs.TryGetValue("Competicao", out IReadOnlyList<CsvRow>? compRows)
            ? Collect(compRows, errors, ReadCompetition)
            : current.Competitions;

        IReadOnlyList<ClubIdentity> clubs = BuildClubs(current, tabs, errors);

        // A club that failed to parse drops out of the result, and then every one of its players
        // looks like an orphan. Reporting 688 consequences of one cause buries the cause, so the
        // clubs have to be clean before the players are read at all.
        IReadOnlyList<CharacterRecord> characters = errors.Count > 0
            ? current.Characters
            : BuildCharacters(current, clubs, tabs, errors);

        return new WorldSnapshot(geoNodes, current.Calibration, clubs, characters, competitions, sources, current.Meta);
    }

    private static IReadOnlyList<T> Collect<T>(
        IReadOnlyList<CsvRow> rows,
        List<string> errors,
        Func<CsvRow, T> read)
    {
        var items = new List<T>(rows.Count);

        foreach (CsvRow row in rows)
        {
            // One bad row does not stop the scan: the user gets the whole list in one pass.
            try
            {
                items.Add(read(row));
            }
            catch (WorldFieldException ex)
            {
                errors.Add(ex.Message);
            }
        }

        return items;
    }

    private static IReadOnlyList<ClubIdentity> BuildClubs(
        WorldSnapshot current,
        IReadOnlyDictionary<string, IReadOnlyList<CsvRow>> tabs,
        List<string> errors)
    {
        Dictionary<string, ClubIdentity> existing = current.Clubs.ToDictionary(club => club.ClubId);
        Dictionary<string, CsvRow> kits = KeyedBy(tabs, "Kits_Estadio", "clubId", errors);
        Dictionary<string, CsvRow> audits = KeyedBy(tabs, "Audit_Clubes", "clubId", errors);

        // Without the Clubes tab the club list is unchanged; the auxiliary tabs then patch it.
        List<(string Id, CsvRow? Row)> order = tabs.TryGetValue("Clubes", out IReadOnlyList<CsvRow>? clubRows)
            ? clubRows.Select(row => (row.Raw("clubId") ?? string.Empty, (CsvRow?)row)).ToList()
            : current.Clubs.Select(club => (club.ClubId, (CsvRow?)null)).ToList();

        var clubs = new List<ClubIdentity>(order.Count);

        foreach ((string clubId, CsvRow? row) in order)
        {
            existing.TryGetValue(clubId, out ClubIdentity? previous);

            if (previous is null && !(kits.ContainsKey(clubId) && audits.ContainsKey(clubId)))
            {
                errors.Add(
                    $"Clubes.csv · {clubId} is a new club, so Kits_Estadio and Audit_Clubes must be "
                    + "imported with it — a club cannot be half-built out of defaults.");
                continue;
            }

            try
            {
                ClubIdentity club = previous ?? Skeleton(clubId);

                if (row is not null)
                    club = ApplyClub(club, row);
                if (kits.TryGetValue(clubId, out CsvRow? kitRow))
                    club = ApplyKits(club, kitRow);
                if (audits.TryGetValue(clubId, out CsvRow? auditRow))
                    club = club with { Audit = ReadClubAudit(auditRow) };

                // The same recalculation the repository applies on write, so the preview shows the
                // ΔE a colour change actually produces rather than the one the file carried.
                clubs.Add(WorldDerivations.Recalculate(club, current.Calibration));
            }
            catch (WorldFieldException ex)
            {
                errors.Add(ex.Message);
            }
        }

        ReportOrphans(kits, clubs.Select(club => club.ClubId), "Kits_Estadio", "club", errors);
        ReportOrphans(audits, clubs.Select(club => club.ClubId), "Audit_Clubes", "club", errors);

        return clubs;
    }

    private static IReadOnlyList<CharacterRecord> BuildCharacters(
        WorldSnapshot current,
        IReadOnlyList<ClubIdentity> clubs,
        IReadOnlyDictionary<string, IReadOnlyList<CsvRow>> tabs,
        List<string> errors)
    {
        Dictionary<string, CharacterRecord> existing = current.Characters.ToDictionary(player => player.PlayerId);
        Dictionary<string, CsvRow> audits = KeyedBy(tabs, "Audit_Jogadores", "playerId", errors);
        Dictionary<string, PrestigeBand> bands = clubs.ToDictionary(club => club.ClubId, club => club.World.PrestigeBand);

        List<(string Id, CsvRow? Row)> order = tabs.TryGetValue("Jogadores", out IReadOnlyList<CsvRow>? playerRows)
            ? playerRows.Select(row => (row.Raw("playerId") ?? string.Empty, (CsvRow?)row)).ToList()
            : current.Characters.Select(player => (player.PlayerId, (CsvRow?)null)).ToList();

        var characters = new List<CharacterRecord>(order.Count);

        foreach ((string playerId, CsvRow? row) in order)
        {
            existing.TryGetValue(playerId, out CharacterRecord? previous);

            if (previous is null && !audits.ContainsKey(playerId))
            {
                errors.Add(
                    $"Jogadores.csv · {playerId} is a new player, so Audit_Jogadores must be imported "
                    + "with it — a player without a deviation record cannot state where it came from.");
                continue;
            }

            try
            {
                CharacterRecord player = previous is null
                    ? ReadNewCharacter(row!, audits[playerId])
                    : ApplyCharacter(previous, row);

                if (audits.TryGetValue(playerId, out CsvRow? auditRow))
                    player = player with { Audit = ReadCharacterAudit(auditRow), Provenance = auditRow.Enum<Provenance>("provenance") };

                if (!bands.TryGetValue(player.ClubId, out PrestigeBand band))
                {
                    // The row that named the club, whichever tab it came from — the player may be
                    // moving clubs in Jogadores, or arriving with only an audit row.
                    CsvRow? source = row ?? (audits.TryGetValue(playerId, out CsvRow? auditOnly) ? auditOnly : null);
                    errors.Add($"{source?.Where("clubId") ?? $"Jogadores.csv · {playerId}"}: "
                        + $"'{player.ClubId}' is not a club in this world");
                    continue;
                }

                characters.Add(WorldDerivations.Recalculate(player, band, current.Calibration));
            }
            catch (WorldFieldException ex)
            {
                errors.Add(ex.Message);
            }
        }

        ReportOrphans(audits, characters.Select(player => player.PlayerId), "Audit_Jogadores", "player", errors);

        return characters;
    }

    private static Dictionary<string, CsvRow> KeyedBy(
        IReadOnlyDictionary<string, IReadOnlyList<CsvRow>> tabs,
        string tabName,
        string keyColumn,
        List<string> errors)
    {
        var keyed = new Dictionary<string, CsvRow>();

        if (!tabs.TryGetValue(tabName, out IReadOnlyList<CsvRow>? rows))
            return keyed;

        foreach (CsvRow row in rows)
        {
            string? key = row.Raw(keyColumn);
            if (key is null)
            {
                errors.Add($"{row.Where(keyColumn)} · must not be empty");
                continue;
            }

            if (!keyed.TryAdd(key, row))
                errors.Add($"{row.Where(keyColumn)} · '{key}' appears more than once in this tab");
        }

        return keyed;
    }

    /// <summary>A row in an auxiliary tab whose entity is not in the result describes nothing —
    /// usually a stale export, which is exactly the mistake worth naming.</summary>
    private static void ReportOrphans(
        Dictionary<string, CsvRow> rows,
        IEnumerable<string> known,
        string tabName,
        string noun,
        List<string> errors)
    {
        var ids = known.ToHashSet();

        foreach ((string id, CsvRow row) in rows)
        {
            if (!ids.Contains(id))
                errors.Add($"{tabName}.csv:{row.Line} · '{id}' is not a {noun} in this import");
        }
    }

    // ------------------------------------------------------------------ records

    private static GeoNode ReadGeoNode(CsvRow row) => new(
        row.String("geoNodeId"),
        row.Enum<GeoNodeKind>("kind"),
        row.OptionalString("parentId"),
        row.String("displayName"));

    private static WorldSource ReadSource(CsvRow row) => new(
        row.String("tema"),
        row.OptionalString("numero"),
        row.OptionalString("fonte"),
        row.OptionalString("url"));

    private static Competition ReadCompetition(CsvRow row) => new(
        row.String("competitionId"),
        row.String("name"),
        row.Enum<CompetitionScope>("scope"),
        row.String("anchorGeoNodeId"),
        row.String("memberPredicateId"),
        row.Enum<PrestigeBand>("prestigeBand"),
        row.Double("leagueTierFloat"),
        row.String("format"),
        row.Int("clubCount"),
        row.Int("rounds"),
        row.Int("promotedIn"),
        row.Int("relegatedOut"),
        row.String("continentalSlots"),
        row.String("editionId"),
        row.Int("season"),
        row.StringList("memberClubIds"));

    private static ClubIdentity ApplyClub(ClubIdentity club, CsvRow row) => club with
    {
        DisplayCode = row.String("displayCode"),
        Identity = new ClubIdentityInfo(
            row.String("officialName"),
            row.String("shortName"),
            row.String("nickname"),
            row.Int("foundingYear")),
        Geography = new ClubGeography(
            row.String("cityName"),
            row.String("uf"),
            row.String("countryId"),
            row.String("geoNodeId"),
            row.Enum<DistrictArchetype>("districtArchetype")),
        World = new ClubWorldProfile(
            row.Enum<PrestigeBand>("prestigeBand"),
            row.Double("clubStrength"),
            row.Int("squadSize"),
            row.Enum<NamingRule>("namingRule")),
        Crest = new ClubCrest(
            row.Enum<ShieldShape>("shieldShape"),
            row.String("centralCharge"),
            row.String("motto"),
            // Recalculated from the palette on write; read here so a hand-edited file still parses.
            [.. new[] { "crestColor1", "crestColor2", "crestColor3" }
                .Select(row.OptionalString)
                .Where(colour => colour is not null)
                .Select(colour => colour!)]),
        Palette = new ClubPalette(
            row.String("palettePrimary"),
            row.String("paletteSecondary"),
            row.String("paletteTertiary"),
            row.Enum<TypographyStyle>("typographyStyle")),
        Stadium = new ClubStadium(
            row.String("stadiumName"),
            row.Int("stadiumCapacity"),
            row.Enum<AtmosphereArchetype>("atmosphereArchetype"),
            row.Enum<PitchSurface>("pitchSurface")),
        AiProfile = new ClubAiProfile(
            row.Enum<TacticalStyle>("defaultTacticalStyle"),
            row.Enum<TacticalStyleProvenance>("tacticalStyleProvenance"),
            // Derived from the atmosphere on write; the column is informational.
            club.AiProfile.HomeAdvantageModifier,
            row.OptionalString("derbyRivalClubId")),
    };

    private static ClubIdentity ApplyKits(ClubIdentity club, CsvRow row) => club with
    {
        Kits = new ClubKits(
            row.Enum<CollarStyle>("collarStyle"),
            row.Enum<FitStyle>("fitStyle"),
            new HomeKit(
                row.Enum<FabricPattern>("homePattern"),
                row.String("homeShirt"),
                row.String("homeShorts"),
                row.String("homeSocks"),
                // Derived on write from the shirt colour.
                club.Kits.Home.Luminance),
            new AwayKit(
                row.Enum<FabricPattern>("awayPattern"),
                row.String("awayShirt"),
                row.String("awayShorts"),
                row.String("awaySocks")),
            DeltaE: club.Kits.DeltaE,                       // derived
            DeltaEThreshold: row.Double("deltaE_threshold"),
            PolarityRule: club.Kits.PolarityRule),          // derived
    };

    private static ClubDeviationAudit ReadClubAudit(CsvRow row) => new(
        row.String("clubId"),
        row.String("anchorClubName"),
        row.String("anchorCityName"),
        row.Int("anchorFoundingYear"),
        row.Int("generatedFoundingYear"),
        row.Int("foundingDecadePreserved"),
        row.String("foundingSourceCitation"),
        row.String("anchorNickname"),
        row.Int("nicknameCommercialLevel"),
        row.Flag("nicknameTrademarked"),
        row.String("nicknameEvidence"),
        row.Enum<NamingRule>("namingRule"),
        row.String("namingRuleReason"),
        row.OptionalDouble("phoneticSimilarity"),
        row.String("crest_originalChargeReplaced"),
        row.String("crest_substituteCharge"),
        row.String("crest_sourceCitation"),
        row.String("districtSourceCitation"),
        row.Enum<TacticalStyleProvenance>("tacticalStyleProvenance"),
        row.String("tacticalStyleEvidence"),
        row.String("chromaticPolicy"),
        row.Flag("anchorFactsVerified"),
        row.String("reviewedBy"),
        row.String("reviewDate"),
        row.OptionalString("note"));

    private static CharacterDeviationAudit ReadCharacterAudit(CsvRow row) => new(
        row.OptionalString("anchorPlayerName"),
        row.OptionalString("anchorNationality"),
        row.OptionalString("deviationFromSurname"),
        row.OptionalString("generatedSurname"),
        row.OptionalDouble("phoneticSimilarity"),
        row.OptionalString("deviationMethod"),
        row.Flag("anchorFactsVerified"));

    private static CharacterRecord ApplyCharacter(CharacterRecord player, CsvRow? row)
    {
        if (row is null)
            return player;

        var attrs = new Dictionary<Attr, int>();
        foreach (Attr attr in Enum.GetValues<Attr>())
            attrs[attr] = row.Int(attr.ToString());

        return player with
        {
            ClubId = row.String("clubId"),
            ShirtNumber = row.Int("shirtNumber"),
            FirstName = row.String("firstName"),
            LastName = row.String("lastName"),
            Nationality = row.String("nationality"),
            SecondNationality = row.OptionalString("secondNationality"),
            DateOfBirth = row.Date("dateOfBirth"),
            Age = row.Int("age"),
            Phase = row.Enum<Phase>("phase"),
            SquadRole = row.Enum<SquadRole>("squadRole"),
            PrimaryPosition = row.Enum<Position>("primaryPosition"),
            SecondaryPositions = row.EnumList<Position>("secondaryPositions"),
            PreferredFoot = row.Enum<PreferredFoot>("preferredFoot"),
            WeakFootRating = row.Int("weakFootRating"),
            SkillMovesRating = row.Int("skillMovesRating"),
            Height = row.Int("height"),
            BuildType = row.Enum<BuildType>("buildType"),
            Attrs = attrs,
            PotentialGap = row.Int("potentialGap"),
            Provenance = row.Enum<Provenance>("provenance"),
        };
    }

    private static CharacterRecord ReadNewCharacter(CsvRow row, CsvRow audit) =>
        ApplyCharacter(
            new CharacterRecord(
                PlayerId: row.String("playerId"),
                ClubId: string.Empty,
                ShirtNumber: 0,
                FirstName: string.Empty,
                LastName: string.Empty,
                ShirtName: string.Empty,
                Nationality: string.Empty,
                SecondNationality: null,
                DateOfBirth: default,
                Age: 0,
                Phase: Phase.Prime,
                SquadRole: SquadRole.Reserva,
                PrimaryPosition: Position.CM,
                SecondaryPositions: [],
                PreferredFoot: PreferredFoot.Right,
                WeakFootRating: 1,
                SkillMovesRating: 1,
                Height: 0,
                BuildType: BuildType.Balanced,
                Attrs: new Dictionary<Attr, int>(),
                PotentialGap: 0,
                Provenance: Provenance.Regen,
                Overall: 0,
                PotentialOverall: 0,
                MarketValueEur: 0,
                SalaryMonthlyBrl: 0,
                Audit: ReadCharacterAudit(audit)),
            row);

    /// <summary>
    /// The shell a brand-new club is filled into. Every field it holds is overwritten from the
    /// three tabs the import demands for a new club, so none of these values can survive — the
    /// record simply has no other way to be constructed.
    /// </summary>
    private static ClubIdentity Skeleton(string clubId) => new(
        clubId,
        DisplayCode: string.Empty,
        Identity: new ClubIdentityInfo(string.Empty, string.Empty, string.Empty, 0),
        Geography: new ClubGeography(string.Empty, string.Empty, string.Empty, string.Empty, DistrictArchetype.HistoricCenter),
        World: new ClubWorldProfile(PrestigeBand.B3, 0, 0, NamingRule.Phonetic),
        Crest: new ClubCrest(ShieldShape.Round, string.Empty, string.Empty, []),
        Palette: new ClubPalette("#000000", "#000000", "#000000", TypographyStyle.ModernSans),
        Kits: new ClubKits(
            CollarStyle.Crew,
            FitStyle.Regular,
            new HomeKit(FabricPattern.Solid, "#000000", "#000000", "#000000", 0),
            new AwayKit(FabricPattern.Solid, "#FFFFFF", "#FFFFFF", "#FFFFFF"),
            0,
            0,
            string.Empty),
        Stadium: new ClubStadium(string.Empty, 0, AtmosphereArchetype.Apathetic, PitchSurface.Pristine),
        AiProfile: new ClubAiProfile(TacticalStyle.Possession, TacticalStyleProvenance.Derived, 0, null),
        Audit: new ClubDeviationAudit(
            clubId, string.Empty, string.Empty, 0, 0, 0, string.Empty, string.Empty, 0, false,
            string.Empty, NamingRule.Phonetic, string.Empty, null, string.Empty, string.Empty,
            string.Empty, string.Empty, TacticalStyleProvenance.Derived, string.Empty, string.Empty,
            false, string.Empty, string.Empty, null));
}
