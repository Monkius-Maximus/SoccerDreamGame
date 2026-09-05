namespace SoccerSim.Core.World;

/// <summary>
/// A club's full authored identity — the record that replaces the 11-tab spreadsheet with one
/// page per club (design_handoff_ferramenta_de_mundo/DATA_CONTRACT.md §3, "ClubIdentity v2").
/// Deliberately immutable: the World Builder tool clones-mutate-persists on every edit
/// (ROADMAP.md, "Toda mutação passa por um único ponto").
/// </summary>
public sealed record ClubIdentity(
    string ClubId,
    string DisplayCode,
    ClubIdentityInfo Identity,
    ClubGeography Geography,
    ClubWorldProfile World,
    ClubCrest Crest,
    ClubPalette Palette,
    ClubKits Kits,
    ClubStadium Stadium,
    ClubAiProfile AiProfile,
    ClubDeviationAudit Audit);

public sealed record ClubIdentityInfo(
    string OfficialName,
    string ShortName,
    string Nickname,
    int FoundingYear);

public sealed record ClubGeography(
    string CityName,
    string Uf,
    string CountryId,
    string GeoNodeId,
    DistrictArchetype DistrictArchetype);

/// <summary>Named <c>ClubWorldProfile</c> (not <c>World</c>) to avoid colliding with the
/// enclosing <c>SoccerSim.Core.World</c> namespace and the JSON block it mirrors (<c>world</c>).</summary>
public sealed record ClubWorldProfile(
    PrestigeBand PrestigeBand,
    double ClubStrength,
    int SquadSize,
    NamingRule NamingRule);

public sealed record ClubCrest(
    ShieldShape ShieldShape,
    string CentralCharge,
    string Motto,
    IReadOnlyList<string> Colors);

public sealed record ClubPalette(
    string Primary,
    string Secondary,
    string Tertiary,
    TypographyStyle TypographyStyle);

public sealed record HomeKit(FabricPattern FabricPattern, string Shirt, string Shorts, string Socks, double Luminance);

public sealed record AwayKit(FabricPattern FabricPattern, string Shirt, string Shorts, string Socks);

public sealed record ClubKits(
    CollarStyle CollarStyle,
    FitStyle FitStyle,
    HomeKit Home,
    AwayKit Away,
    double DeltaE,
    double DeltaEThreshold,
    string PolarityRule);

public sealed record ClubStadium(
    string Name,
    int Capacity,
    AtmosphereArchetype AtmosphereArchetype,
    PitchSurface PitchSurface);

public sealed record ClubAiProfile(
    TacticalStyle DefaultTacticalStyle,
    TacticalStyleProvenance TacticalStyleProvenance,
    double HomeAdvantageModifier,
    string? DerbyRivalClubId);

/// <summary>
/// The deviation-from-reality audit trail (25 fields, all required in the batch). This is the
/// "número sem fonte não entra" contract turned into data — see DATA_CONTRACT.md §3.
/// <see cref="AnchorFactsVerified"/> is a hard gate: a club cannot enter the batch without it.
/// </summary>
public sealed record ClubDeviationAudit(
    string ClubId,
    string AnchorClubName,
    string AnchorCityName,
    int AnchorFoundingYear,
    int GeneratedFoundingYear,
    int FoundingDecadePreserved,
    string FoundingSourceCitation,
    string AnchorNickname,
    int NicknameCommercialLevel,
    bool NicknameTrademarked,
    string NicknameEvidence,
    NamingRule NamingRule,
    string NamingRuleReason,
    double? PhoneticSimilarity,
    string CrestOriginalChargeReplaced,
    string CrestSubstituteCharge,
    string CrestSourceCitation,
    string DistrictSourceCitation,
    TacticalStyleProvenance TacticalStyleProvenance,
    string TacticalStyleEvidence,
    string ChromaticPolicy,
    bool AnchorFactsVerified,
    string ReviewedBy,
    string ReviewDate,
    string? Note);
