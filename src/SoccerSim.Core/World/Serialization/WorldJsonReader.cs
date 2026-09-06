using System.Globalization;
using System.Text.Json.Nodes;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// Reads the world document exported by the authoring prototype (design_handoff .../data/world.json)
/// into a <see cref="WorldSnapshot"/>. Pure: no database, no I/O beyond the string handed in.
///
/// <para>
/// Two rules shape this class. First, <b>strict</b>: unknown enum values, missing fields and
/// wrong types are rejected, never defaulted — the spreadsheet extraction has already produced
/// a malformed row once (a legend line that became a GeoNode with no kind), and silently
/// storing it is how corrupt data spreads. Second, <b>all-or-nothing</b>: every malformed record
/// is collected and reported together, and a document with any error yields no snapshot at all,
/// so the importer cannot half-write a world.
/// </para>
/// </summary>
public static class WorldJsonReader
{
    public static WorldSnapshot Read(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new WorldImportException(["document root: expected a JSON object"]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new WorldImportException([$"document root: not valid JSON ({ex.Message})"]);
        }

        var errors = new List<string>();
        var cursor = new JsonCursor(root, "$");

        WorldCalibration? calibration = ReadOne(errors, () => ReadCalibration(cursor));
        IReadOnlyList<GeoNode> geoNodes = ReadMany(errors, cursor, "geoNodes", ReadGeoNode);
        IReadOnlyList<ClubIdentity> clubs = ReadMany(errors, cursor, "clubs", ReadClub);
        IReadOnlyList<CharacterRecord> characters = ReadMany(errors, cursor, "players", ReadCharacter);
        IReadOnlyList<Competition> competitions = ReadMany(errors, cursor, "competitions", ReadCompetition);
        IReadOnlyList<WorldSource> sources = ReadMany(errors, cursor, "sources", ReadSource);

        if (errors.Count > 0)
            throw new WorldImportException(errors);

        return new WorldSnapshot(geoNodes, calibration!, clubs, characters, competitions, sources);
    }

    // ---------------------------------------------------------------- sections

    private static IReadOnlyList<T> ReadMany<T>(
        List<string> errors,
        JsonCursor root,
        string section,
        Func<JsonCursor, T> readOne)
    {
        JsonArray array;
        try
        {
            array = root.Array(section);
        }
        catch (WorldFieldException ex)
        {
            errors.Add(ex.Message);
            return [];
        }

        var items = new List<T>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            // One bad record does not stop the scan: the user gets the whole list of problems.
            try
            {
                items.Add(readOne(JsonCursor.ForObject(array[i], $"{section}[{i}]")));
            }
            catch (WorldFieldException ex)
            {
                errors.Add(ex.Message);
            }
        }

        return items;
    }

    private static T? ReadOne<T>(List<string> errors, Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (WorldFieldException ex)
        {
            errors.Add(ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- records

    private static GeoNode ReadGeoNode(JsonCursor node) => new(
        node.String("geoNodeId"),
        node.Enum<GeoNodeKind>("kind"),
        node.OptionalString("parentId"),
        node.String("displayName"));

    private static WorldSource ReadSource(JsonCursor node) => new(
        node.String("tema"),
        node.OptionalString("numero"),
        node.OptionalString("fonte"),
        node.OptionalString("url"));

    private static Competition ReadCompetition(JsonCursor node)
    {
        var members = new List<string>();
        JsonArray memberIds = node.Array("memberClubIds");
        for (int i = 0; i < memberIds.Count; i++)
        {
            string? clubId = memberIds[i]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(clubId))
                throw new WorldFieldException($"{node.Path}.memberClubIds[{i}]", "expected a club id");
            members.Add(clubId);
        }

        return new Competition(
            node.String("competitionId"),
            node.String("name"),
            node.Enum<CompetitionScope>("scope"),
            node.String("anchorGeoNodeId"),
            node.String("memberPredicateId"),
            node.Enum<PrestigeBand>("prestigeBand"),
            node.Double("leagueTierFloat"),
            node.String("format"),
            node.Int("clubCount"),
            node.Int("rounds"),
            node.Int("promotedIn"),
            node.Int("relegatedOut"),
            node.String("continentalSlots"),
            node.String("editionId"),
            node.Int("season"),
            members);
    }

    private static ClubIdentity ReadClub(JsonCursor node)
    {
        JsonCursor identity = node.Object("identity");
        JsonCursor geography = node.Object("geography");
        JsonCursor world = node.Object("world");
        JsonCursor crest = node.Object("crest");
        JsonCursor palette = node.Object("palette");
        JsonCursor kits = node.Object("kits");
        JsonCursor home = kits.Object("home");
        JsonCursor away = kits.Object("away");
        JsonCursor stadium = node.Object("stadium");
        JsonCursor ai = node.Object("aiProfile");

        return new ClubIdentity(
            ClubId: node.String("clubId"),
            DisplayCode: node.String("displayCode"),
            Identity: new ClubIdentityInfo(
                identity.String("officialName"),
                identity.String("shortName"),
                identity.String("nickname"),
                identity.Int("foundingYear")),
            Geography: new ClubGeography(
                geography.String("cityName"),
                geography.String("uf"),
                geography.String("countryId"),
                geography.String("geoNodeId"),
                geography.Enum<DistrictArchetype>("districtArchetype")),
            World: new ClubWorldProfile(
                world.Enum<PrestigeBand>("prestigeBand"),
                world.Double("clubStrength"),
                world.Int("squadSize"),
                world.Enum<NamingRule>("namingRule")),
            Crest: new ClubCrest(
                crest.Enum<ShieldShape>("shieldShape"),
                crest.String("centralCharge"),
                crest.String("motto"),
                // Colors mirror the palette and are recomputed by WorldDerivations; whatever the
                // document says here is not trusted.
                []),
            Palette: new ClubPalette(
                palette.String("primary"),
                palette.String("secondary"),
                palette.String("tertiary"),
                palette.Enum<TypographyStyle>("typographyStyle")),
            Kits: new ClubKits(
                kits.Enum<CollarStyle>("collarStyle"),
                kits.Enum<FitStyle>("fitStyle"),
                Home: new HomeKit(
                    home.Enum<FabricPattern>("fabricPattern"),
                    home.String("shirt"),
                    home.String("shorts"),
                    home.String("socks"),
                    Luminance: 0),          // derived
                Away: new AwayKit(
                    away.Enum<FabricPattern>("fabricPattern"),
                    away.String("shirt"),
                    away.String("shorts"),
                    away.String("socks")),
                DeltaE: 0,                  // derived
                DeltaEThreshold: kits.Double("deltaEThreshold"),
                PolarityRule: string.Empty),// derived
            Stadium: new ClubStadium(
                stadium.String("name"),
                stadium.Int("capacity"),
                stadium.Enum<AtmosphereArchetype>("atmosphereArchetype"),
                stadium.Enum<PitchSurface>("pitchSurface")),
            AiProfile: new ClubAiProfile(
                ai.Enum<TacticalStyle>("defaultTacticalStyle"),
                ai.Enum<TacticalStyleProvenance>("tacticalStyleProvenance"),
                HomeAdvantageModifier: 0,   // derived
                DerbyRivalClubId: ai.OptionalString("derbyRivalClubId")),
            Audit: ReadClubAudit(node.Object("audit")));
    }

    private static ClubDeviationAudit ReadClubAudit(JsonCursor audit) => new(
        ClubId: audit.String("clubId"),
        AnchorClubName: audit.String("anchorClubName"),
        AnchorCityName: audit.String("anchorCityName"),
        AnchorFoundingYear: audit.Int("anchorFoundingYear"),
        GeneratedFoundingYear: audit.Int("generatedFoundingYear"),
        FoundingDecadePreserved: audit.Int("foundingDecadePreserved"),
        FoundingSourceCitation: audit.String("foundingSourceCitation"),
        AnchorNickname: audit.String("anchorNickname"),
        NicknameCommercialLevel: audit.Int("nicknameCommercialLevel"),
        NicknameTrademarked: audit.Flag("nicknameTrademarked"),
        NicknameEvidence: audit.String("nicknameEvidence"),
        NamingRule: audit.Enum<NamingRule>("namingRule"),
        NamingRuleReason: audit.String("namingRuleReason"),
        PhoneticSimilarity: audit.OptionalDouble("phoneticSimilarity"),
        CrestOriginalChargeReplaced: audit.String("crest_originalChargeReplaced"),
        CrestSubstituteCharge: audit.String("crest_substituteCharge"),
        CrestSourceCitation: audit.String("crest_sourceCitation"),
        DistrictSourceCitation: audit.String("districtSourceCitation"),
        TacticalStyleProvenance: audit.Enum<TacticalStyleProvenance>("tacticalStyleProvenance"),
        TacticalStyleEvidence: audit.String("tacticalStyleEvidence"),
        ChromaticPolicy: audit.String("chromaticPolicy"),
        AnchorFactsVerified: audit.Flag("anchorFactsVerified"),
        ReviewedBy: audit.String("reviewedBy"),
        ReviewDate: audit.String("reviewDate"),
        Note: audit.OptionalString("note"));

    private static CharacterRecord ReadCharacter(JsonCursor node)
    {
        JsonCursor auditNode = node.Object("audit");

        return new CharacterRecord(
            PlayerId: node.String("playerId"),
            ClubId: node.String("clubId"),
            ShirtNumber: node.Int("shirtNumber"),
            FirstName: node.String("firstName"),
            LastName: node.String("lastName"),
            ShirtName: string.Empty,        // derived
            Nationality: node.String("nationality"),
            SecondNationality: node.OptionalString("secondNationality"),
            DateOfBirth: ReadDate(node, "dateOfBirth"),
            Age: node.Int("age"),
            Phase: node.Enum<Phase>("phase", NormalizePhase),
            SquadRole: node.Enum<SquadRole>("squadRole", NormalizeSquadRole),
            PrimaryPosition: node.Enum<Position>("primaryPosition"),
            SecondaryPositions: ReadSecondaryPositions(node),
            PreferredFoot: node.Enum<PreferredFoot>("preferredFoot"),
            WeakFootRating: node.Int("weakFootRating"),
            SkillMovesRating: node.Int("skillMovesRating"),
            Height: node.Int("height"),
            BuildType: node.Enum<BuildType>("buildType"),
            Attrs: ReadAttrs(node.Object("attrs")),
            PotentialGap: node.Int("potentialGap"),
            Provenance: node.Enum<Provenance>("provenance"),
            Overall: 0,                     // derived
            PotentialOverall: 0,            // derived
            MarketValueEur: 0,              // derived
            SalaryMonthlyBrl: 0,            // derived
            Audit: new CharacterDeviationAudit(
                auditNode.OptionalString("anchorPlayerName"),
                auditNode.OptionalString("anchorNationality"),
                auditNode.OptionalString("deviationFromSurname"),
                auditNode.OptionalString("generatedSurname"),
                auditNode.OptionalDouble("phoneticSimilarity"),
                auditNode.OptionalString("deviationMethod"),
                auditNode.Flag("anchorFactsVerified")));
    }

    private static IReadOnlyDictionary<Attr, int> ReadAttrs(JsonCursor attrs)
    {
        var values = new Dictionary<Attr, int>();
        foreach (Attr attr in System.Enum.GetValues<Attr>())
            values[attr] = attrs.Int(attr.ToString());

        return values;
    }

    private static IReadOnlyList<Position> ReadSecondaryPositions(JsonCursor node)
    {
        // Pipe-separated in the source ("AM|ST"); absent or null when the player has none.
        string? raw = node.OptionalString("secondaryPositions");
        if (raw is null)
            return [];

        var positions = new List<Position>();
        foreach (string part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Enum.TryParse(part, ignoreCase: false, out Position position) || !System.Enum.IsDefined(position))
                throw new WorldFieldException($"{node.Path}.secondaryPositions", $"'{part}' is not a valid Position");
            positions.Add(position);
        }

        return positions;
    }

    private static DateOnly ReadDate(JsonCursor node, string name)
    {
        string raw = node.String(name);
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            throw new WorldFieldException($"{node.Path}.{name}", $"'{raw}' is not a date in yyyy-MM-dd form");
        return date;
    }

    /// <summary>"Prospect 15-20" → "Prospect": the source carries the age band as a suffix
    /// (DATA_CONTRACT.md §2). The band is documentation, not part of the enum.</summary>
    private static string NormalizePhase(string raw)
    {
        int space = raw.IndexOf(' ');
        return space < 0 ? raw : raw[..space];
    }

    /// <summary>The source spells this role in Portuguese with a cedilla. Only the known spelling
    /// is mapped — anything else is rejected rather than guessed at.</summary>
    private static string NormalizeSquadRole(string raw) => raw == "Rotação" ? nameof(SquadRole.Rotacao) : raw;

    // ------------------------------------------------------------ calibration

    private static WorldCalibration ReadCalibration(JsonCursor root)
    {
        JsonCursor calibration = root.Object("calibration");

        var constants = new Dictionary<string, CalibrationConstant>();
        JsonCursor constantsNode = calibration.Object("constants");
        foreach (string key in ConstantKeys)
        {
            JsonCursor constant = constantsNode.Object(key);
            constants[key] = new CalibrationConstant(
                constant.Double("value"),
                constant.String("unit"),
                constant.String("note"));
        }

        var ageMult = new List<AgeMultStep>();
        JsonArray ladder = calibration.Array("ageMult");
        for (int i = 0; i < ladder.Count; i++)
        {
            if (ladder[i] is not JsonArray pair || pair.Count != 2)
                throw new WorldFieldException($"{calibration.Path}.ageMult[{i}]", "expected an [age, multiplier] pair");
            ageMult.Add(new AgeMultStep(pair[0]!.GetValue<int>(), pair[1]!.GetValue<double>()));
        }

        var bands = new Dictionary<PrestigeBand, PrestigeBandCalibration>();
        JsonCursor bandsNode = calibration.Object("bands");
        foreach (PrestigeBand band in System.Enum.GetValues<PrestigeBand>())
        {
            JsonCursor entry = bandsNode.Object(band.ToString());
            bands[band] = new PrestigeBandCalibration(
                entry.Double("valueMult"),
                entry.Double("capMean"),
                entry.Double("capSd"),
                entry.Int("n"));
        }

        var homeAdv = new Dictionary<AtmosphereArchetype, double>();
        JsonCursor homeAdvNode = calibration.Object("homeAdv");
        foreach (AtmosphereArchetype atmosphere in System.Enum.GetValues<AtmosphereArchetype>())
            homeAdv[atmosphere] = homeAdvNode.Double(atmosphere.ToString());

        var stadiumProfile = new Dictionary<string, StadiumProfileEntry>();
        foreach ((string countryId, JsonCursor profile) in calibration.ObjectEntries("stadiumProfile"))
        {
            stadiumProfile[countryId] = new StadiumProfileEntry(
                profile.Double("mean"),
                profile.Double("sd"),
                profile.Double("min"),
                profile.Double("max"));
        }

        var positionWeights = new Dictionary<Position, IReadOnlyDictionary<Attr, double>>();
        JsonCursor weightsNode = root.Object("positionWeights");
        foreach (Position position in System.Enum.GetValues<Position>())
        {
            JsonCursor row = weightsNode.Object(position.ToString());
            var weights = new Dictionary<Attr, double>();
            foreach (Attr attr in System.Enum.GetValues<Attr>())
                weights[attr] = row.Double(attr.ToString());
            positionWeights[position] = weights;
        }

        return new WorldCalibration(
            constants,
            ageMult.OrderBy(step => step.Age).ToList(),
            bands,
            homeAdv,
            stadiumProfile,
            positionWeights);
    }

    /// <summary>The calibration constants the economy and kit rules require. Listing them
    /// explicitly means a document missing one is rejected at import instead of throwing a
    /// KeyNotFoundException deep inside a value calculation later.</summary>
    private static readonly string[] ConstantKeys =
    [
        "valueBase",
        "valuePivot",
        "valueDoublingStep",
        "potentialPremium",
        "eurToBrl",
        "wageRateMonthly",
        "wageFloorBrl",
        "valueFloorEur",
        "deltaEThreshold",
    ];
}
