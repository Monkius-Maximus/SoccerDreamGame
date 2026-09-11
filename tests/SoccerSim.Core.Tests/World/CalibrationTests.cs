using SoccerSim.Core.World;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 9: a weight row that does not sum to 1 is flagged, and recalculating the
/// batch leaves ECONOMY_STALE empty in the sweep.
/// </summary>
public sealed class CalibrationTests
{
    private static readonly Lazy<WorldSnapshot> Source =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static WorldSnapshot World => Source.Value;

    // ---------------------------------------------------------------- weights

    [Fact]
    public void TheRealMatrix_Balances()
    {
        CalibrationReview review = CalibrationReviewer.Review(World);

        Assert.Equal(8, review.Weights.Count);
        Assert.Empty(review.UnbalancedWeights);
    }

    [Fact]
    public void AWeightRowThatDoesNotSumToOne_IsFlagged()
    {
        // 0.98 across a row quietly deflates every overall computed from it, by about two points,
        // across the whole batch — and nothing else in the tool would ever say so.
        WorldSnapshot world = WithWeights(Position.ST, weights =>
            weights.ToDictionary(entry => entry.Key, entry => entry.Value * 0.98));

        WeightRowReview row = Assert.Single(CalibrationReviewer.Review(world).UnbalancedWeights);

        Assert.Equal(Position.ST, row.Position);
        Assert.Equal(0.98, row.Sum, precision: 6);
        Assert.False(row.Balanced);
    }

    [Fact]
    public void RoundingInAHandTypedRow_IsNotFlagged()
    {
        // Twelve weights typed to four places will not close exactly; failing that would make the
        // check useless.
        WorldSnapshot world = WithWeights(Position.CM, weights =>
        {
            var adjusted = weights.ToDictionary(entry => entry.Key, entry => entry.Value);
            adjusted[Attr.Vision] += 0.0002;
            return adjusted;
        });

        Assert.Empty(CalibrationReviewer.Review(world).UnbalancedWeights);
    }

    // ------------------------------------------------------------ the seals

    [Theory]
    [InlineData("AJUSTADO por mínimos quadrados contra 4 alvos do Transfermarkt", SourceSeal.Anchored)]
    [InlineData("NÃO ANCORADO — sem fonte pública.", SourceSeal.Unsourced)]
    [InlineData("PROVISÓRIO — §11 #3 NÃO RESOLVIDO", SourceSeal.Provisional)]
    public void TheSealIsReadOffTheNote(string note, SourceSeal expected) =>
        Assert.Equal(expected, CalibrationReviewer.SealOf(note));

    [Fact]
    public void TheRealCalibration_AdmitsWhichNumbersHaveNoSource()
    {
        CalibrationReview review = CalibrationReviewer.Review(World);

        // Three constants say "NÃO ANCORADO" in their own notes, and one says "PROVISÓRIO".
        // The screen colours them; this test is that the tool reads what the data already admits.
        Assert.Equal(3, review.Unsourced);
        Assert.Equal(1, review.Provisional);

        Assert.Contains(review.Constants, entry => entry.Key == "deltaEThreshold" && entry.Seal == SourceSeal.Provisional);
        Assert.Contains(review.Constants, entry => entry.Key == "potentialPremium" && entry.Seal == SourceSeal.Unsourced);
    }

    // ------------------------------------------------------- the age ladder

    [Fact]
    public void EveryPlayerStandsOnExactlyOneRung()
    {
        CalibrationReview review = CalibrationReviewer.Review(World);

        Assert.Equal(World.Characters.Count, review.AgeLadder.Sum(rung => rung.Players));
    }

    // -------------------------------------------------- stadium profiles

    [Fact]
    public void TheStadiumProfile_CountsTheClubsOutsideIt()
    {
        StadiumProfileReview profile = Assert.Single(CalibrationReviewer.Review(World).StadiumProfiles);

        Assert.Equal("BRA", profile.CountryId);
        Assert.Equal(0, profile.ClubsOutside);
    }

    [Fact]
    public void NarrowingTheProfile_CountsTheClubsThatFallOut()
    {
        WorldCalibration calibration = World.Calibration;
        StadiumProfileEntry brazil = calibration.StadiumProfile["BRA"];

        WorldSnapshot world = World with
        {
            Calibration = calibration with
            {
                StadiumProfile = new Dictionary<string, StadiumProfileEntry>
                {
                    ["BRA"] = brazil with { Min = 40000 },
                },
            },
        };

        StadiumProfileReview profile = Assert.Single(CalibrationReviewer.Review(world).StadiumProfiles);

        Assert.Equal(
            World.Clubs.Count(club => club.Stadium.Capacity < 40000),
            profile.ClubsOutside);
        Assert.True(profile.ClubsOutside > 0);
    }

    // ------------------------------------------------------- recalculation

    [Fact]
    public void TheRealBatch_IsNotStale()
    {
        // The Sprint 1 gate proved the formulas reproduce every stored value; this is the count
        // the calibration screen shows, and it is zero.
        Assert.Equal(0, CalibrationReviewer.Review(World).StalePlayers);
    }

    [Fact]
    public void RecalculatingTheBatch_ClearsEveryStaleRowAndTheSweepAgrees()
    {
        // A re-fit constant ages every player at once. Correcting 688 rows one edit at a time is
        // not a thing a person does, so the calibration screen does it in one act.
        WorldSnapshot aged = World with
        {
            Characters = World.Characters.Select(player => player with { Overall = player.Overall + 5 }).ToList(),
        };

        Assert.Equal(World.Characters.Count, CalibrationReviewer.Review(aged).StalePlayers);
        Assert.Contains(BatchAudit.Run(aged).Findings, finding => finding.Code == "ECONOMY_STALE");

        WorldSnapshot fixedWorld = aged with { Characters = CalibrationReviewer.Recalculate(aged) };

        Assert.Equal(0, CalibrationReviewer.Review(fixedWorld).StalePlayers);
        Assert.DoesNotContain(BatchAudit.Run(fixedWorld).Findings, finding => finding.Code == "ECONOMY_STALE");
    }

    [Fact]
    public void RecalculatingIsIdempotent()
    {
        IReadOnlyList<CharacterRecord> once = CalibrationReviewer.Recalculate(World);
        IReadOnlyList<CharacterRecord> twice = CalibrationReviewer.Recalculate(World with { Characters = once });

        Assert.Equal(
            once.Select(player => (player.PlayerId, player.Overall, player.MarketValueEur, player.SalaryMonthlyBrl)),
            twice.Select(player => (player.PlayerId, player.Overall, player.MarketValueEur, player.SalaryMonthlyBrl)));
    }

    private static WorldSnapshot WithWeights(
        Position position,
        Func<IReadOnlyDictionary<Attr, double>, IReadOnlyDictionary<Attr, double>> edit)
    {
        var weights = World.Calibration.PositionWeights.ToDictionary(entry => entry.Key, entry => entry.Value);
        weights[position] = edit(weights[position]);

        return World with { Calibration = World.Calibration with { PositionWeights = weights } };
    }
}
