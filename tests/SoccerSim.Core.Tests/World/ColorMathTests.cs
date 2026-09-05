using System.Text.Json.Nodes;
using SoccerSim.Core.World.Color;
using Xunit;

namespace SoccerSim.Core.Tests.World;

public sealed class ColorMathTests
{
    public static IEnumerable<object[]> Clubs() =>
        WorldFixture.Data["clubs"]!.AsArray().Select(node => new object[] { node!.AsObject() });

    [Theory]
    [MemberData(nameof(Clubs))]
    public void DeltaE76_MatchesRecordedKitsDeltaE(JsonObject club)
    {
        var kits = club["kits"]!.AsObject();
        string home = kits["home"]!["shirt"]!.GetValue<string>();
        string away = kits["away"]!["shirt"]!.GetValue<string>();
        double expected = kits["deltaE"]!.GetValue<double>();

        double actual = ColorMath.DeltaE76(home, away);

        // Recorded to 1 decimal; the club example in ALGORITHMS.md §1.2 is 104.6.
        Assert.Equal(expected, Math.Round(actual, 1), precision: 1);
    }

    [Theory]
    [MemberData(nameof(Clubs))]
    public void RelativeLuminance_MatchesRecordedHomeLuminance(JsonObject club)
    {
        var home = club["kits"]!["home"]!.AsObject();
        string shirt = home["shirt"]!.GetValue<string>();
        double expected = home["luminance"]!.GetValue<double>();

        double actual = ColorMath.RelativeLuminance(shirt);

        Assert.Equal(expected, Math.Round(actual, 4), precision: 4);
    }

    [Theory]
    [InlineData("#808080")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void Hue_OfAchromaticColor_IsMinusOne(string hex)
    {
        Assert.Equal(-1, ColorMath.Hue(hex));
    }

    [Fact]
    public void Hue_OfPrimaryRed_IsZero()
    {
        Assert.Equal(0, ColorMath.Hue("#FF0000"), precision: 3);
    }

    [Fact]
    public void HueDistance_WrapsAroundZero()
    {
        // 350° and 10° are 20° apart going the short way around, not 340°.
        Assert.Equal(20, ColorMath.HueDistance(350, 10), precision: 3);
    }

    [Fact]
    public void HueDistance_IsSymmetric()
    {
        Assert.Equal(ColorMath.HueDistance(40, 200), ColorMath.HueDistance(200, 40));
    }
}
