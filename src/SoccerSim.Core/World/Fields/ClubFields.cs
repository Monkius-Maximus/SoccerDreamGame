namespace SoccerSim.Core.World.Fields;

/// <summary>
/// The club's editable surface: the nine groups of the club page, in the order they are shown,
/// each field with the seal it carries and the rule for reading and writing it.
///
/// <para>
/// Fields that <see cref="WorldDerivations"/> owns are listed but NOT editable — ΔE, home
/// luminance, the polarity rule, the home-advantage modifier and the crest colours all follow
/// from other fields and are recomputed on every write. Listing them keeps them visible on the
/// page (a value with no explanation is worse than one marked "calculated"); refusing to patch
/// them keeps the derivation honest. Patch the atmosphere and the home advantage moves; patch
/// the home advantage and you get a 400 telling you which field actually decides it.
/// </para>
/// </summary>
public static class ClubFields
{
    public static IReadOnlyList<WorldFieldGroup> Groups { get; } = BuildGroups();

    /// <summary>Every field by path, for validation and lookup.</summary>
    public static IReadOnlyDictionary<string, WorldField> ByPath { get; } =
        Groups.SelectMany(group => group.Fields).ToDictionary(field => field.Path);

    /// <summary>
    /// Applies one field change, parsing and validating the incoming text first. Throws
    /// <see cref="FieldPatchException"/> — and changes nothing — when the path is unknown, the
    /// field is derived, or the value does not fit.
    /// </summary>
    public static ClubIdentity Apply(ClubIdentity club, string path, string? value)
    {
        if (!Setters.TryGetValue(path, out Func<ClubIdentity, string?, ClubIdentity>? setter))
        {
            throw ByPath.TryGetValue(path, out WorldField? known)
                ? new FieldPatchException(path, $"'{known.Label}' is {known.Provenance.ToString().ToLowerInvariant()} and is recalculated on write")
                : new FieldPatchException(path, "unknown field");
        }

        return setter(club, value);
    }

    /// <summary>The current value of a field as text, for the edit form and the edit log.</summary>
    public static string? Read(ClubIdentity club, string path) =>
        Readers.TryGetValue(path, out Func<ClubIdentity, string?>? reader)
            ? reader(club)
            : throw new FieldPatchException(path, "unknown field");

    // ------------------------------------------------------------------ groups

    private static WorldFieldGroup Group(string name, string id, params WorldField[] fields) =>
        new(name, id, fields);

    private static WorldField Text(string path, string label, FieldProvenance provenance) =>
        new(path, label, FieldKind.Text, provenance);

    private static WorldField Long(string path, string label, FieldProvenance provenance) =>
        new(path, label, FieldKind.LongText, provenance);

    private static WorldField Int(string path, string label, FieldProvenance provenance) =>
        new(path, label, FieldKind.Int, provenance);

    private static WorldField Float(string path, string label, FieldProvenance provenance) =>
        new(path, label, FieldKind.Float, provenance);

    private static WorldField Color(string path, string label, FieldProvenance provenance) =>
        new(path, label, FieldKind.Color, provenance);

    private static WorldField Enum<TEnum>(string path, string label, FieldProvenance provenance)
        where TEnum : struct, System.Enum =>
        new(path, label, FieldKind.Enum, provenance, EnumName: typeof(TEnum).Name);

    private static WorldField Reference(string path, string label, FieldProvenance provenance, string target) =>
        new(path, label, FieldKind.Reference, provenance, EnumName: target);

    /// <summary>A value the tool computes. Shown with its seal, never accepted from a client.</summary>
    private static WorldField Computed(string path, string label, FieldKind kind, FieldProvenance provenance) =>
        new(path, label, kind, provenance, Editable: false);

    private static IReadOnlyList<WorldFieldGroup> BuildGroups() =>
    [
        Group("Identidade", "identity",
            Text("identity.officialName", "Nome oficial", FieldProvenance.Authored),
            Text("identity.shortName", "Nome curto", FieldProvenance.Authored),
            Text("identity.nickname", "Alcunha", FieldProvenance.Authored),
            Int("identity.foundingYear", "Ano de fundação", FieldProvenance.Derived),
            Text("displayCode", "Código de exibição", FieldProvenance.Authored)),

        Group("Geografia", "geography",
            Text("geography.cityName", "Cidade", FieldProvenance.Authored),
            Text("geography.uf", "UF", FieldProvenance.Authored),
            Text("geography.countryId", "País", FieldProvenance.Authored),
            Reference("geography.geoNodeId", "Nó geográfico", FieldProvenance.Derived, "geoNode"),
            Enum<DistrictArchetype>("geography.districtArchetype", "Arquétipo de distrito", FieldProvenance.Sampled)),

        Group("Mundo", "world",
            Enum<PrestigeBand>("world.prestigeBand", "Banda de prestígio", FieldProvenance.Derived),
            Float("world.clubStrength", "Força do clube", FieldProvenance.Derived),
            Int("world.squadSize", "Tamanho do elenco", FieldProvenance.Derived),
            Enum<NamingRule>("world.namingRule", "Regra de nomeação", FieldProvenance.Derived)),

        Group("Escudo", "crest",
            Enum<ShieldShape>("crest.shieldShape", "Forma do escudo", FieldProvenance.Sampled),
            Long("crest.centralCharge", "Carga central", FieldProvenance.Authored),
            Text("crest.motto", "Mote", FieldProvenance.Authored),
            // Mirrors the palette by definition (ALGORITHMS.md §4.3): edit the palette instead.
            Computed("crest.colors", "Cores do escudo", FieldKind.Text, FieldProvenance.Derived)),

        Group("Paleta", "palette",
            Color("palette.primary", "Primária", FieldProvenance.Authored),
            Color("palette.secondary", "Secundária", FieldProvenance.Authored),
            Color("palette.tertiary", "Terciária", FieldProvenance.Authored),
            Enum<TypographyStyle>("palette.typographyStyle", "Estilo tipográfico", FieldProvenance.Sampled)),

        Group("Uniformes", "kits",
            Enum<CollarStyle>("kits.collarStyle", "Gola", FieldProvenance.Sampled),
            Enum<FitStyle>("kits.fitStyle", "Caimento", FieldProvenance.Sampled),
            Enum<FabricPattern>("kits.home.fabricPattern", "Padrão titular", FieldProvenance.Sampled),
            Color("kits.home.shirt", "Camisa titular", FieldProvenance.Derived),
            Color("kits.home.shorts", "Calção titular", FieldProvenance.Derived),
            Color("kits.home.socks", "Meião titular", FieldProvenance.Derived),
            Enum<FabricPattern>("kits.away.fabricPattern", "Padrão reserva", FieldProvenance.Sampled),
            Color("kits.away.shirt", "Camisa reserva", FieldProvenance.Derived),
            Color("kits.away.shorts", "Calção reserva", FieldProvenance.Derived),
            Color("kits.away.socks", "Meião reserva", FieldProvenance.Derived),
            Float("kits.deltaEThreshold", "Limite de ΔE", FieldProvenance.Authored),
            Computed("kits.deltaE", "ΔE titular × reserva", FieldKind.Float, FieldProvenance.Calculated),
            Computed("kits.home.luminance", "Luminância titular", FieldKind.Float, FieldProvenance.Calculated),
            Computed("kits.polarityRule", "Regra de polaridade", FieldKind.Text, FieldProvenance.Derived)),

        Group("Estádio", "stadium",
            Text("stadium.name", "Nome do estádio", FieldProvenance.Authored),
            Int("stadium.capacity", "Capacidade", FieldProvenance.Sampled),
            Enum<AtmosphereArchetype>("stadium.atmosphereArchetype", "Atmosfera", FieldProvenance.Sampled),
            Enum<PitchSurface>("stadium.pitchSurface", "Gramado", FieldProvenance.Sampled)),

        Group("Perfil de IA", "aiProfile",
            Enum<TacticalStyle>("aiProfile.defaultTacticalStyle", "Estilo tático", FieldProvenance.Derived),
            Enum<TacticalStyleProvenance>("aiProfile.tacticalStyleProvenance", "Proveniência do estilo", FieldProvenance.Authored),
            Reference("aiProfile.derbyRivalClubId", "Rival de clássico", FieldProvenance.Authored, "club"),
            // Rewritten from calibration whenever the atmosphere changes (ALGORITHMS.md §4.3).
            Computed("aiProfile.homeAdvantageModifier", "Vantagem de casa", FieldKind.Float, FieldProvenance.Derived)),

        Group("Auditoria de desvio", "audit",
            Text("audit.anchorClubName", "Clube âncora", FieldProvenance.Authored),
            Text("audit.anchorCityName", "Cidade da âncora", FieldProvenance.Authored),
            Int("audit.anchorFoundingYear", "Fundação da âncora", FieldProvenance.Authored),
            Int("audit.foundingDecadePreserved", "Década preservada", FieldProvenance.Derived),
            Long("audit.foundingSourceCitation", "Fonte da fundação", FieldProvenance.Authored),
            Text("audit.anchorNickname", "Alcunha da âncora", FieldProvenance.Authored),
            Int("audit.nicknameCommercialLevel", "Nível comercial da alcunha", FieldProvenance.Authored),
            Int("audit.nicknameTrademarked", "Alcunha registrada (1/0)", FieldProvenance.Authored),
            Long("audit.nicknameEvidence", "Evidência da alcunha", FieldProvenance.Authored),
            Long("audit.namingRuleReason", "Razão da regra", FieldProvenance.Derived),
            Float("audit.phoneticSimilarity", "Similaridade fonética", FieldProvenance.Calculated),
            Long("audit.crestOriginalChargeReplaced", "Carga original substituída", FieldProvenance.Authored),
            Long("audit.crestSubstituteCharge", "Carga substituta", FieldProvenance.Authored),
            Long("audit.crestSourceCitation", "Fonte da carga", FieldProvenance.Authored),
            Long("audit.districtSourceCitation", "Fonte do distrito", FieldProvenance.Authored),
            Long("audit.tacticalStyleEvidence", "Evidência do estilo tático", FieldProvenance.Authored),
            Text("audit.chromaticPolicy", "Política cromática", FieldProvenance.Authored),
            Int("audit.anchorFactsVerified", "Fatos da âncora verificados (1/0)", FieldProvenance.Authored),
            Text("audit.reviewedBy", "Revisado por", FieldProvenance.Authored),
            Text("audit.reviewDate", "Data da revisão", FieldProvenance.Authored),
            Long("audit.note", "Nota", FieldProvenance.Authored)),
    ];

    // ----------------------------------------------------------------- setters

    private static readonly Dictionary<string, Func<ClubIdentity, string?, ClubIdentity>> Setters = new()
    {
        ["displayCode"] = (club, value) => club with { DisplayCode = FieldValue.RequireText("displayCode", value).ToUpperInvariant() },

        ["identity.officialName"] = (club, value) => club with { Identity = club.Identity with { OfficialName = FieldValue.RequireText("identity.officialName", value) } },
        ["identity.shortName"] = (club, value) => club with { Identity = club.Identity with { ShortName = FieldValue.RequireText("identity.shortName", value) } },
        ["identity.nickname"] = (club, value) => club with { Identity = club.Identity with { Nickname = FieldValue.RequireText("identity.nickname", value) } },
        ["identity.foundingYear"] = (club, value) => club with { Identity = club.Identity with { FoundingYear = FieldValue.Int("identity.foundingYear", value) } },

        ["geography.cityName"] = (club, value) => club with { Geography = club.Geography with { CityName = FieldValue.RequireText("geography.cityName", value) } },
        ["geography.uf"] = (club, value) => club with { Geography = club.Geography with { Uf = FieldValue.RequireText("geography.uf", value) } },
        ["geography.countryId"] = (club, value) => club with { Geography = club.Geography with { CountryId = FieldValue.RequireText("geography.countryId", value) } },
        ["geography.geoNodeId"] = (club, value) => club with { Geography = club.Geography with { GeoNodeId = FieldValue.RequireText("geography.geoNodeId", value) } },
        ["geography.districtArchetype"] = (club, value) => club with { Geography = club.Geography with { DistrictArchetype = FieldValue.Enum<DistrictArchetype>("geography.districtArchetype", value) } },

        ["world.prestigeBand"] = (club, value) => club with { World = club.World with { PrestigeBand = FieldValue.Enum<PrestigeBand>("world.prestigeBand", value) } },
        ["world.clubStrength"] = (club, value) => club with { World = club.World with { ClubStrength = FieldValue.Float("world.clubStrength", value) } },
        ["world.squadSize"] = (club, value) => club with { World = club.World with { SquadSize = FieldValue.IntInRange("world.squadSize", value, 28, 40) } },
        ["world.namingRule"] = (club, value) => club with { World = club.World with { NamingRule = FieldValue.Enum<NamingRule>("world.namingRule", value) } },

        ["crest.shieldShape"] = (club, value) => club with { Crest = club.Crest with { ShieldShape = FieldValue.Enum<ShieldShape>("crest.shieldShape", value) } },
        ["crest.centralCharge"] = (club, value) => club with { Crest = club.Crest with { CentralCharge = FieldValue.RequireText("crest.centralCharge", value) } },
        ["crest.motto"] = (club, value) => club with { Crest = club.Crest with { Motto = FieldValue.RequireText("crest.motto", value) } },

        ["palette.primary"] = (club, value) => club with { Palette = club.Palette with { Primary = FieldValue.Color("palette.primary", value) } },
        ["palette.secondary"] = (club, value) => club with { Palette = club.Palette with { Secondary = FieldValue.Color("palette.secondary", value) } },
        ["palette.tertiary"] = (club, value) => club with { Palette = club.Palette with { Tertiary = FieldValue.Color("palette.tertiary", value) } },
        ["palette.typographyStyle"] = (club, value) => club with { Palette = club.Palette with { TypographyStyle = FieldValue.Enum<TypographyStyle>("palette.typographyStyle", value) } },

        ["kits.collarStyle"] = (club, value) => club with { Kits = club.Kits with { CollarStyle = FieldValue.Enum<CollarStyle>("kits.collarStyle", value) } },
        ["kits.fitStyle"] = (club, value) => club with { Kits = club.Kits with { FitStyle = FieldValue.Enum<FitStyle>("kits.fitStyle", value) } },
        ["kits.deltaEThreshold"] = (club, value) => club with { Kits = club.Kits with { DeltaEThreshold = FieldValue.Float("kits.deltaEThreshold", value) } },
        ["kits.home.fabricPattern"] = (club, value) => club with { Kits = club.Kits with { Home = club.Kits.Home with { FabricPattern = FieldValue.Enum<FabricPattern>("kits.home.fabricPattern", value) } } },
        ["kits.home.shirt"] = (club, value) => club with { Kits = club.Kits with { Home = club.Kits.Home with { Shirt = FieldValue.Color("kits.home.shirt", value) } } },
        ["kits.home.shorts"] = (club, value) => club with { Kits = club.Kits with { Home = club.Kits.Home with { Shorts = FieldValue.Color("kits.home.shorts", value) } } },
        ["kits.home.socks"] = (club, value) => club with { Kits = club.Kits with { Home = club.Kits.Home with { Socks = FieldValue.Color("kits.home.socks", value) } } },
        ["kits.away.fabricPattern"] = (club, value) => club with { Kits = club.Kits with { Away = club.Kits.Away with { FabricPattern = FieldValue.Enum<FabricPattern>("kits.away.fabricPattern", value) } } },
        ["kits.away.shirt"] = (club, value) => club with { Kits = club.Kits with { Away = club.Kits.Away with { Shirt = FieldValue.Color("kits.away.shirt", value) } } },
        ["kits.away.shorts"] = (club, value) => club with { Kits = club.Kits with { Away = club.Kits.Away with { Shorts = FieldValue.Color("kits.away.shorts", value) } } },
        ["kits.away.socks"] = (club, value) => club with { Kits = club.Kits with { Away = club.Kits.Away with { Socks = FieldValue.Color("kits.away.socks", value) } } },

        ["stadium.name"] = (club, value) => club with { Stadium = club.Stadium with { Name = FieldValue.RequireText("stadium.name", value) } },
        ["stadium.capacity"] = (club, value) => club with { Stadium = club.Stadium with { Capacity = FieldValue.IntInRange("stadium.capacity", value, 1, 500_000) } },
        ["stadium.atmosphereArchetype"] = (club, value) => club with { Stadium = club.Stadium with { AtmosphereArchetype = FieldValue.Enum<AtmosphereArchetype>("stadium.atmosphereArchetype", value) } },
        ["stadium.pitchSurface"] = (club, value) => club with { Stadium = club.Stadium with { PitchSurface = FieldValue.Enum<PitchSurface>("stadium.pitchSurface", value) } },

        ["aiProfile.defaultTacticalStyle"] = (club, value) => club with { AiProfile = club.AiProfile with { DefaultTacticalStyle = FieldValue.Enum<TacticalStyle>("aiProfile.defaultTacticalStyle", value) } },
        ["aiProfile.tacticalStyleProvenance"] = (club, value) => club with { AiProfile = club.AiProfile with { TacticalStyleProvenance = FieldValue.Enum<TacticalStyleProvenance>("aiProfile.tacticalStyleProvenance", value) } },
        // Null clears the rivalry — a club without a derby is a normal state, not a missing value.
        ["aiProfile.derbyRivalClubId"] = (club, value) => club with { AiProfile = club.AiProfile with { DerbyRivalClubId = FieldValue.OptionalText(value) } },

        ["audit.anchorClubName"] = (club, value) => club with { Audit = club.Audit with { AnchorClubName = FieldValue.RequireText("audit.anchorClubName", value) } },
        ["audit.anchorCityName"] = (club, value) => club with { Audit = club.Audit with { AnchorCityName = FieldValue.RequireText("audit.anchorCityName", value) } },
        ["audit.anchorFoundingYear"] = (club, value) => club with { Audit = club.Audit with { AnchorFoundingYear = FieldValue.Int("audit.anchorFoundingYear", value) } },
        ["audit.foundingDecadePreserved"] = (club, value) => club with { Audit = club.Audit with { FoundingDecadePreserved = FieldValue.Int("audit.foundingDecadePreserved", value) } },
        ["audit.foundingSourceCitation"] = (club, value) => club with { Audit = club.Audit with { FoundingSourceCitation = FieldValue.RequireText("audit.foundingSourceCitation", value) } },
        ["audit.anchorNickname"] = (club, value) => club with { Audit = club.Audit with { AnchorNickname = FieldValue.RequireText("audit.anchorNickname", value) } },
        ["audit.nicknameCommercialLevel"] = (club, value) => club with { Audit = club.Audit with { NicknameCommercialLevel = FieldValue.IntInRange("audit.nicknameCommercialLevel", value, 0, 2) } },
        ["audit.nicknameTrademarked"] = (club, value) => club with { Audit = club.Audit with { NicknameTrademarked = FieldValue.Flag("audit.nicknameTrademarked", value) } },
        ["audit.nicknameEvidence"] = (club, value) => club with { Audit = club.Audit with { NicknameEvidence = FieldValue.RequireText("audit.nicknameEvidence", value) } },
        ["audit.namingRuleReason"] = (club, value) => club with { Audit = club.Audit with { NamingRuleReason = FieldValue.RequireText("audit.namingRuleReason", value) } },
        // Nullable: the window only applies when the naming rule is Phonetic.
        ["audit.phoneticSimilarity"] = (club, value) => club with { Audit = club.Audit with { PhoneticSimilarity = FieldValue.OptionalFloat("audit.phoneticSimilarity", value) } },
        ["audit.crestOriginalChargeReplaced"] = (club, value) => club with { Audit = club.Audit with { CrestOriginalChargeReplaced = FieldValue.RequireText("audit.crestOriginalChargeReplaced", value) } },
        ["audit.crestSubstituteCharge"] = (club, value) => club with { Audit = club.Audit with { CrestSubstituteCharge = FieldValue.RequireText("audit.crestSubstituteCharge", value) } },
        ["audit.crestSourceCitation"] = (club, value) => club with { Audit = club.Audit with { CrestSourceCitation = FieldValue.RequireText("audit.crestSourceCitation", value) } },
        ["audit.districtSourceCitation"] = (club, value) => club with { Audit = club.Audit with { DistrictSourceCitation = FieldValue.RequireText("audit.districtSourceCitation", value) } },
        ["audit.tacticalStyleEvidence"] = (club, value) => club with { Audit = club.Audit with { TacticalStyleEvidence = FieldValue.RequireText("audit.tacticalStyleEvidence", value) } },
        ["audit.chromaticPolicy"] = (club, value) => club with { Audit = club.Audit with { ChromaticPolicy = FieldValue.RequireText("audit.chromaticPolicy", value) } },
        ["audit.anchorFactsVerified"] = (club, value) => club with { Audit = club.Audit with { AnchorFactsVerified = FieldValue.Flag("audit.anchorFactsVerified", value) } },
        ["audit.reviewedBy"] = (club, value) => club with { Audit = club.Audit with { ReviewedBy = FieldValue.RequireText("audit.reviewedBy", value) } },
        ["audit.reviewDate"] = (club, value) => club with { Audit = club.Audit with { ReviewDate = FieldValue.RequireText("audit.reviewDate", value) } },
        ["audit.note"] = (club, value) => club with { Audit = club.Audit with { Note = FieldValue.OptionalText(value) } },
    };

    // ----------------------------------------------------------------- readers

    private static readonly Dictionary<string, Func<ClubIdentity, string?>> Readers = new()
    {
        ["displayCode"] = club => club.DisplayCode,

        ["identity.officialName"] = club => club.Identity.OfficialName,
        ["identity.shortName"] = club => club.Identity.ShortName,
        ["identity.nickname"] = club => club.Identity.Nickname,
        ["identity.foundingYear"] = club => FieldValue.Format(club.Identity.FoundingYear),

        ["geography.cityName"] = club => club.Geography.CityName,
        ["geography.uf"] = club => club.Geography.Uf,
        ["geography.countryId"] = club => club.Geography.CountryId,
        ["geography.geoNodeId"] = club => club.Geography.GeoNodeId,
        ["geography.districtArchetype"] = club => club.Geography.DistrictArchetype.ToString(),

        ["world.prestigeBand"] = club => club.World.PrestigeBand.ToString(),
        ["world.clubStrength"] = club => FieldValue.Format(club.World.ClubStrength),
        ["world.squadSize"] = club => FieldValue.Format(club.World.SquadSize),
        ["world.namingRule"] = club => club.World.NamingRule.ToString(),

        ["crest.shieldShape"] = club => club.Crest.ShieldShape.ToString(),
        ["crest.centralCharge"] = club => club.Crest.CentralCharge,
        ["crest.motto"] = club => club.Crest.Motto,
        ["crest.colors"] = club => string.Join(", ", club.Crest.Colors),

        ["palette.primary"] = club => club.Palette.Primary,
        ["palette.secondary"] = club => club.Palette.Secondary,
        ["palette.tertiary"] = club => club.Palette.Tertiary,
        ["palette.typographyStyle"] = club => club.Palette.TypographyStyle.ToString(),

        ["kits.collarStyle"] = club => club.Kits.CollarStyle.ToString(),
        ["kits.fitStyle"] = club => club.Kits.FitStyle.ToString(),
        ["kits.deltaEThreshold"] = club => FieldValue.Format(club.Kits.DeltaEThreshold),
        ["kits.deltaE"] = club => FieldValue.Format(club.Kits.DeltaE),
        ["kits.polarityRule"] = club => club.Kits.PolarityRule,
        ["kits.home.fabricPattern"] = club => club.Kits.Home.FabricPattern.ToString(),
        ["kits.home.shirt"] = club => club.Kits.Home.Shirt,
        ["kits.home.shorts"] = club => club.Kits.Home.Shorts,
        ["kits.home.socks"] = club => club.Kits.Home.Socks,
        ["kits.home.luminance"] = club => FieldValue.Format(club.Kits.Home.Luminance),
        ["kits.away.fabricPattern"] = club => club.Kits.Away.FabricPattern.ToString(),
        ["kits.away.shirt"] = club => club.Kits.Away.Shirt,
        ["kits.away.shorts"] = club => club.Kits.Away.Shorts,
        ["kits.away.socks"] = club => club.Kits.Away.Socks,

        ["stadium.name"] = club => club.Stadium.Name,
        ["stadium.capacity"] = club => FieldValue.Format(club.Stadium.Capacity),
        ["stadium.atmosphereArchetype"] = club => club.Stadium.AtmosphereArchetype.ToString(),
        ["stadium.pitchSurface"] = club => club.Stadium.PitchSurface.ToString(),

        ["aiProfile.defaultTacticalStyle"] = club => club.AiProfile.DefaultTacticalStyle.ToString(),
        ["aiProfile.tacticalStyleProvenance"] = club => club.AiProfile.TacticalStyleProvenance.ToString(),
        ["aiProfile.homeAdvantageModifier"] = club => FieldValue.Format(club.AiProfile.HomeAdvantageModifier),
        ["aiProfile.derbyRivalClubId"] = club => club.AiProfile.DerbyRivalClubId,

        ["audit.anchorClubName"] = club => club.Audit.AnchorClubName,
        ["audit.anchorCityName"] = club => club.Audit.AnchorCityName,
        ["audit.anchorFoundingYear"] = club => FieldValue.Format(club.Audit.AnchorFoundingYear),
        ["audit.foundingDecadePreserved"] = club => FieldValue.Format(club.Audit.FoundingDecadePreserved),
        ["audit.foundingSourceCitation"] = club => club.Audit.FoundingSourceCitation,
        ["audit.anchorNickname"] = club => club.Audit.AnchorNickname,
        ["audit.nicknameCommercialLevel"] = club => FieldValue.Format(club.Audit.NicknameCommercialLevel),
        ["audit.nicknameTrademarked"] = club => FieldValue.Format(club.Audit.NicknameTrademarked),
        ["audit.nicknameEvidence"] = club => club.Audit.NicknameEvidence,
        ["audit.namingRuleReason"] = club => club.Audit.NamingRuleReason,
        ["audit.phoneticSimilarity"] = club => club.Audit.PhoneticSimilarity is { } value ? FieldValue.Format(value) : null,
        ["audit.crestOriginalChargeReplaced"] = club => club.Audit.CrestOriginalChargeReplaced,
        ["audit.crestSubstituteCharge"] = club => club.Audit.CrestSubstituteCharge,
        ["audit.crestSourceCitation"] = club => club.Audit.CrestSourceCitation,
        ["audit.districtSourceCitation"] = club => club.Audit.DistrictSourceCitation,
        ["audit.tacticalStyleEvidence"] = club => club.Audit.TacticalStyleEvidence,
        ["audit.chromaticPolicy"] = club => club.Audit.ChromaticPolicy,
        ["audit.anchorFactsVerified"] = club => FieldValue.Format(club.Audit.AnchorFactsVerified),
        ["audit.reviewedBy"] = club => club.Audit.ReviewedBy,
        ["audit.reviewDate"] = club => club.Audit.ReviewDate,
        ["audit.note"] = club => club.Audit.Note,
    };
}
