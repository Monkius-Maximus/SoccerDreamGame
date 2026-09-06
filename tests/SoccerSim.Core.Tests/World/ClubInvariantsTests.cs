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

    // The known-good club lives in WorldSamples so the persistence tests break the same
    // record in their own ways without a second copy drifting from this one.
    private static ClubIdentity ValidClub() => WorldSamples.Club();

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
