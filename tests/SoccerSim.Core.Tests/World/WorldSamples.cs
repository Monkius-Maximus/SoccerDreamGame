using SoccerSim.Core.World;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Hand-built world records for tests that need a known-good starting point to break in exactly
/// one way. Every factory returns a record that passes both the database constraints and the club
/// invariants, so a failing assertion points at the single field the test changed.
/// </summary>
internal static class WorldSamples
{
    public const string GeoNodeId = "geo_test_city";

    public static GeoNode GeoNode(string geoNodeId = GeoNodeId, string? parentId = null) =>
        new(geoNodeId, GeoNodeKind.City, parentId, "Test City");

    public static ClubIdentity Club(
        string clubId = "clb_test_001",
        string displayCode = "TST",
        string geoNodeId = GeoNodeId) => new(
        ClubId: clubId,
        DisplayCode: displayCode,
        Identity: new ClubIdentityInfo("Test FC", "Test", "Testers", 1900),
        Geography: new ClubGeography("Test City", "TS", "BRA", geoNodeId, DistrictArchetype.Affluent),
        World: new ClubWorldProfile(PrestigeBand.B2, ClubStrength: 0.8, SquadSize: 30, NamingRule.Toponymic),
        Crest: new ClubCrest(ShieldShape.Round, "Test Charge", "Test Motto", ["#D50A0A", "#000000", "#FFFFFF"]),
        Palette: new ClubPalette("#D50A0A", "#000000", "#FFFFFF", TypographyStyle.ModernSans),
        Kits: new ClubKits(
            CollarStyle.Crew,
            FitStyle.Slim,
            Home: new HomeKit(FabricPattern.Solid, "#D50A0A", "#000000", "#000000", Luminance: 0.1439),
            Away: new AwayKit(FabricPattern.Solid, "#FFFFFF", "#FFFFFF", "#D50A0A"),
            DeltaE: 104.6,
            DeltaEThreshold: 25,
            PolarityRule: WorldDerivations.PolarityDarkHome),
        Stadium: new ClubStadium("Test Arena", Capacity: 27000, AtmosphereArchetype.Cauldron, PitchSurface.Pristine),
        AiProfile: new ClubAiProfile(
            TacticalStyle.HighPress,
            TacticalStyleProvenance.Derived,
            HomeAdvantageModifier: 0.17,
            DerbyRivalClubId: null),
        Audit: ClubAudit(clubId));

    public static ClubDeviationAudit ClubAudit(string clubId = "clb_test_001") => new(
        ClubId: clubId,
        AnchorClubName: "Anchor FC",
        AnchorCityName: "Test City",
        AnchorFoundingYear: 1898,
        GeneratedFoundingYear: 1900,
        FoundingDecadePreserved: 1890,
        FoundingSourceCitation: "https://example.com/founding",
        AnchorNickname: "Anchor",
        NicknameCommercialLevel: 0,
        NicknameTrademarked: true,
        NicknameEvidence: "https://example.com/nickname",
        NamingRule: NamingRule.Toponymic,
        NamingRuleReason: "Cond.1 FALSA · Cond.2 FALSA",
        PhoneticSimilarity: null,
        CrestOriginalChargeReplaced: "não",
        CrestSubstituteCharge: "Test Charge",
        CrestSourceCitation: "https://example.com/crest",
        DistrictSourceCitation: "https://example.com/district",
        TacticalStyleProvenance: TacticalStyleProvenance.Derived,
        TacticalStyleEvidence: "https://example.com/tactics",
        ChromaticPolicy: "policy",
        AnchorFactsVerified: true,
        ReviewedBy: "tester",
        ReviewDate: "2026-01-01",
        Note: null);

    public static CharacterRecord Character(
        string playerId = "plr_test_0001",
        string clubId = "clb_test_001",
        int shirtNumber = 10,
        int attributeValue = 70) => new(
        PlayerId: playerId,
        ClubId: clubId,
        ShirtNumber: shirtNumber,
        FirstName: "Test",
        LastName: "Player",
        ShirtName: "PLAYER",
        Nationality: "BRA",
        SecondNationality: null,
        DateOfBirth: new DateOnly(1998, 6, 15),
        Age: 27,
        Phase: Phase.Prime,
        SquadRole: SquadRole.Titular,
        PrimaryPosition: Position.CM,
        SecondaryPositions: [Position.DM, Position.AM],
        PreferredFoot: PreferredFoot.Right,
        WeakFootRating: 3,
        SkillMovesRating: 3,
        Height: 180,
        BuildType: BuildType.Balanced,
        Attrs: Attributes(attributeValue),
        PotentialGap: 4,
        Provenance: Provenance.Anchored,
        Overall: 0,              // derived on write
        PotentialOverall: 0,     // derived on write
        MarketValueEur: 0,       // derived on write
        SalaryMonthlyBrl: 0,     // derived on write
        Audit: new CharacterDeviationAudit(
            AnchorPlayerName: "Anchor Player",
            AnchorNationality: "BRA",
            DeviationFromSurname: "Anchor",
            GeneratedSurname: "Player",
            PhoneticSimilarity: 0.7,
            DeviationMethod: "Auto",
            AnchorFactsVerified: false));

    public static Dictionary<Attr, int> Attributes(int value = 70) =>
        Enum.GetValues<Attr>().ToDictionary(attr => attr, _ => value);
}
