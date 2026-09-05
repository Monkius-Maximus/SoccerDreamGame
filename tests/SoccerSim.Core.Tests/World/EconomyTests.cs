using System.Text.Json.Nodes;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Economy;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The Sprint 1 hard gate (ROADMAP.md): every one of the 688 players in the source dataset must
/// reproduce its recorded overall/potential/value/salary exactly through the ported formulas.
/// If this doesn't stay green, the calibration was ported wrong and nothing built afterward can
/// be trusted — do not relax these asserts to "close enough".
/// </summary>
public sealed class EconomyTests
{
    private static readonly WorldCalibration Calibration = WorldFixture.BuildCalibration();
    private static readonly Dictionary<string, PrestigeBand> ClubBands = WorldFixture.BuildClubBands();

    public static IEnumerable<object[]> Players() =>
        WorldFixture.Data["players"]!.AsArray().Select(node => new object[] { node!.AsObject() });

    [Theory]
    [MemberData(nameof(Players))]
    public void Overall_MatchesRecordedValue(JsonObject player)
    {
        var attrs = WorldFixture.ParseAttrs(player["attrs"]!.AsObject());
        var position = Enum.Parse<Position>(player["primaryPosition"]!.GetValue<string>());

        int actual = WorldEconomy.Overall(attrs, position, Calibration);

        Assert.Equal(player["overall"]!.GetValue<int>(), actual);
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void PotentialOverall_MatchesRecordedValue(JsonObject player)
    {
        int overall = player["overall"]!.GetValue<int>();
        int potentialGap = player["potentialGap"]!.GetValue<int>();

        int actual = WorldEconomy.PotentialOverall(overall, potentialGap);

        Assert.Equal(player["potentialOverall"]!.GetValue<int>(), actual);
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void MarketValue_MatchesRecordedValue(JsonObject player)
    {
        int overall = player["overall"]!.GetValue<int>();
        int age = player["age"]!.GetValue<int>();
        int potentialGap = player["potentialGap"]!.GetValue<int>();
        var band = ClubBands[player["clubId"]!.GetValue<string>()];

        int actual = WorldEconomy.MarketValueEur(overall, age, band, potentialGap, Calibration);

        Assert.Equal(player["marketValueEUR"]!.GetValue<int>(), actual);
    }

    [Theory]
    [MemberData(nameof(Players))]
    public void Salary_MatchesRecordedValue(JsonObject player)
    {
        int marketValue = player["marketValueEUR"]!.GetValue<int>();

        int actual = WorldEconomy.SalaryMonthlyBrl(marketValue, Calibration);

        Assert.Equal(player["salaryMonthlyBRL"]!.GetValue<int>(), actual);
    }

    [Fact]
    public void Dataset_HasExpectedPlayerCount()
    {
        // Guards against a silently truncated/empty fixture making the gate above vacuously pass.
        Assert.Equal(688, WorldFixture.Data["players"]!.AsArray().Count);
    }
}
