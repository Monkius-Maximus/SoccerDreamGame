using SoccerSim.Core.World;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>One case per check in ALGORITHMS.md §5 (checks 1-6), each at the level ("erro" vs
/// "aviso") the algorithm spec calls for.</summary>
public sealed class ClubInvariantsTests
{
    private static readonly WorldCalibration Calibration = new(
        Constants: new Dictionary<string, CalibrationConstant>(),
        AgeMult: [],
        Bands: new Dictionary<PrestigeBand, PrestigeBandCalibration>(),
        HomeAdv: new Dictionary<AtmosphereArchetype, double>(),
        StadiumProfile: new Dictionary<string, StadiumProfileEntry>
        {
            ["BRA"] = new(Mean: 44000, Sd: 19000, Min: 12000, Max: 79000),
        },
        PositionWeights: new Dictionary<Position, IReadOnlyDictionary<Attr, double>>());

    private static ClubIdentity ValidClub() => new(
        ClubId: "clb_test_001",
        DisplayCode: "TST",
        Identity: new ClubIdentityInfo("Test FC", "Test", "Testers", 1900),
        Geography: new ClubGeography("Test City", "TS", "BRA", "geo_test", DistrictArchetype.Affluent),
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
            PolarityRule: "titular escura -> reserva no polo claro da palette"),
        Stadium: new ClubStadium("Test Arena", Capacity: 27000, AtmosphereArchetype.Cauldron, PitchSurface.Pristine),
        AiProfile: new ClubAiProfile(TacticalStyle.HighPress, TacticalStyleProvenance.Derived, HomeAdvantageModifier: 0.17, DerbyRivalClubId: null),
        Audit: new ClubDeviationAudit(
            ClubId: "clb_test_001",
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
            Note: null));

    private static Finding Find(IReadOnlyList<Finding> findings, string code) =>
        findings.Single(f => f.Code == code);

    [Fact]
    public void ValidClub_PassesAllSixChecks()
    {
        var findings = ClubInvariants.Check(ValidClub(), Calibration);

        Assert.Equal(6, findings.Count);
        Assert.All(findings, f => Assert.NotEqual(FindingLevel.Error, f.Level));
    }

    [Fact]
    public void AnchorFactsUnverified_IsError()
    {
        var club = ValidClub() with { Audit = ValidClub().Audit with { AnchorFactsVerified = false } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "ANCHOR_VERIFIED");

        Assert.Equal(FindingLevel.Error, finding.Level);
    }

    [Fact]
    public void PhoneticSimilarity_OutsideWindow_IsError()
    {
        var club = ValidClub() with { Audit = ValidClub().Audit with { PhoneticSimilarity = 0.40 } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "PHONETIC_WINDOW");

        Assert.Equal(FindingLevel.Error, finding.Level);
    }

    [Fact]
    public void PhoneticSimilarity_InsideWindow_IsOk()
    {
        var club = ValidClub() with { Audit = ValidClub().Audit with { PhoneticSimilarity = 0.70 } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "PHONETIC_WINDOW");

        Assert.Equal(FindingLevel.Ok, finding.Level);
    }

    [Fact]
    public void PhoneticSimilarity_Null_IsOk()
    {
        var finding = Find(ClubInvariants.Check(ValidClub(), Calibration), "PHONETIC_WINDOW");

        Assert.Equal(FindingLevel.Ok, finding.Level);
    }

    [Fact]
    public void KitDeltaE_BelowThreshold_IsError()
    {
        var validClub = ValidClub();
        // Near-identical home/away shirts collapse ΔE well under the 25 threshold.
        var club = validClub with { Kits = validClub.Kits with { Away = validClub.Kits.Away with { Shirt = "#D40909" } } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "KIT_DELTA_E");

        Assert.Equal(FindingLevel.Error, finding.Level);
    }

    [Fact]
    public void AwayKitColor_FarFromPalette_IsWarning()
    {
        var validClub = ValidClub();
        // Palette is red/black/white; a pure green away shirt sits far outside 18° of any of them.
        var club = validClub with { Kits = validClub.Kits with { Away = validClub.Kits.Away with { Shirt = "#00FF00" } } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "KIT_NEW_HUE");

        Assert.Equal(FindingLevel.Warning, finding.Level);
    }

    [Fact]
    public void AwayKitColors_WithinPaletteHues_IsOk()
    {
        var finding = Find(ClubInvariants.Check(ValidClub(), Calibration), "KIT_NEW_HUE");

        Assert.Equal(FindingLevel.Ok, finding.Level);
    }

    [Fact]
    public void StadiumCapacity_OutsideCountryProfile_IsError()
    {
        var validClub = ValidClub();
        var club = validClub with { Stadium = validClub.Stadium with { Capacity = 500 } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "STADIUM_CAPACITY");

        Assert.Equal(FindingLevel.Error, finding.Level);
    }

    [Fact]
    public void StadiumCapacity_NoCountryProfile_IsWarning()
    {
        var validClub = ValidClub();
        var club = validClub with { Geography = validClub.Geography with { CountryId = "ZZZ" } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "STADIUM_PROFILE_MISSING");

        Assert.Equal(FindingLevel.Warning, finding.Level);
    }

    [Fact]
    public void EnumField_OutOfRange_IsError()
    {
        var validClub = ValidClub();
        var club = validClub with { World = validClub.World with { PrestigeBand = (PrestigeBand)999 } };

        var finding = Find(ClubInvariants.Check(club, Calibration), "ENUM_CLOSED");

        Assert.Equal(FindingLevel.Error, finding.Level);
        Assert.Contains("world.prestigeBand", finding.Detail);
    }

    [Fact]
    public void AllEnumFields_WithinSchema_IsOk()
    {
        var finding = Find(ClubInvariants.Check(ValidClub(), Calibration), "ENUM_CLOSED");

        Assert.Equal(FindingLevel.Ok, finding.Level);
    }
}
