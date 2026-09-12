using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 9: the failures that only appear once a second division exists. None of
/// these could be written while the world had exactly one competition.
/// </summary>
public sealed class PyramidTests
{
    private static Division Div(
        int tier,
        int clubs = 20,
        int promotedIn = 4,
        int relegatedOut = 4,
        CompetitionFormat format = CompetitionFormat.LeagueDouble,
        IReadOnlyList<string>? clubIds = null) =>
        new(
            tier,
            $"div_bra_{tier}",
            $"{tier}ª Divisão",
            format,
            clubs,
            promotedIn,
            relegatedOut,
            clubIds ?? Enumerable.Range(1, clubs).Select(n => $"clb_{tier}_{n:D3}").ToList());

    private static LeaguePyramid Pyramid(params Division[] divisions) => new("BRA", divisions);

    private static IReadOnlyList<string> Codes(LeaguePyramid pyramid) =>
        PyramidRules.Check(pyramid).Select(finding => finding.Code).ToList();

    /// <summary>A pyramid that balances: the top promotes and relegates the same number, and each
    /// division below hands up exactly what it takes down.</summary>
    private static LeaguePyramid Balanced() => Pyramid(
        Div(1, promotedIn: 4, relegatedOut: 4),
        Div(2, promotedIn: 4, relegatedOut: 4),
        Div(3, promotedIn: 0, relegatedOut: 0));

    [Fact]
    public void ABalancedPyramid_PassesEveryCheck() => Assert.Empty(PyramidRules.Check(Balanced()));

    // --------------------------------------------------------------- the flow

    /// <summary>
    /// The balance the roadmap names: clubs arriving are those relegated out of the division
    /// above plus those promoted in from below; clubs leaving are those promoted into the
    /// division above plus those relegated out of this one.
    /// </summary>
    [Fact]
    public void ADivisionThatTakesMoreThanItGivesBack_ChangesSizeAndIsRefused()
    {
        // Tier 1 relegates 4 but tier 2 only promotes 3: the second division grows by one a year.
        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 3, relegatedOut: 4),
            Div(2, promotedIn: 4, relegatedOut: 4),
            Div(3, promotedIn: 0, relegatedOut: 0));

        Assert.Contains("PYRAMID_FLOW", Codes(pyramid));
    }

    [Fact]
    public void TheTopDivisionMustPromoteAndRelegateTheSameNumber()
    {
        // Nothing is above the first division, so what it takes in has to equal what it sends
        // down. An imbalance here necessarily unbalances the division below as well, which is
        // why the assertion is about the top's own message rather than about a single finding.
        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 2, relegatedOut: 4),
            Div(2, promotedIn: 0, relegatedOut: 0));

        Finding finding = Assert.Single(
            PyramidRules.Check(pyramid),
            f => f.Code == "PYRAMID_FLOW" && f.Detail.StartsWith("1ª Divisão"));

        Assert.Contains("recebe 2 e perde 4", finding.Detail);
    }

    [Fact]
    public void TheBottomDivisionCannotPromoteOrRelegate()
    {
        // There is nothing below it, so a club going down has nowhere to land.
        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 2, relegatedOut: 2),
            Div(2, promotedIn: 2, relegatedOut: 2));

        Finding finding = Assert.Single(
            PyramidRules.Check(pyramid),
            f => f.Code == "PYRAMID_FLOW" && f.Label == "Base da pirâmide");

        Assert.Contains("2ª Divisão", finding.Detail);
    }

    // -------------------------------------------------------------- the tiers

    [Fact]
    public void ADuplicateTier_IsRefused()
    {
        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 0, relegatedOut: 0),
            Div(1, promotedIn: 0, relegatedOut: 0) with { DivisionId = "div_bra_1b" });

        Assert.Contains("TIER_DUP", Codes(pyramid));
    }

    [Fact]
    public void AGapBetweenTiers_IsRefused()
    {
        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 4, relegatedOut: 4),
            Div(3, promotedIn: 4, relegatedOut: 4),
            Div(4, promotedIn: 0, relegatedOut: 0));

        Finding finding = Assert.Single(PyramidRules.Check(pyramid), f => f.Code == "TIER_GAP");
        Assert.Contains("tier 2", finding.Detail);
    }

    [Fact]
    public void APyramidThatDoesNotStartAtOne_IsRefused()
    {
        LeaguePyramid pyramid = Pyramid(Div(2, promotedIn: 0, relegatedOut: 0));

        Finding finding = Assert.Single(PyramidRules.Check(pyramid), f => f.Code == "TIER_GAP");
        Assert.Contains("começa em 1", finding.Detail);
    }

    [Fact]
    public void AnEmptyPyramid_IsRefused() =>
        Assert.Contains("PYRAMID_EMPTY", Codes(new LeaguePyramid("ARG", [])));

    // ---------------------------------------------------------- the enrolment

    [Fact]
    public void AClubEnrolledInTwoDivisionsOfTheSameCountry_IsAnError()
    {
        var shared = Enumerable.Range(1, 20).Select(n => $"clb_bra_{n:D3}").ToList();

        LeaguePyramid pyramid = Pyramid(
            Div(1, promotedIn: 4, relegatedOut: 4, clubIds: shared),
            Div(2, promotedIn: 0, relegatedOut: 4, clubIds: shared));

        var findings = PyramidRules.Check(pyramid).Where(f => f.Code == "CLUB_TWO_DIVISIONS").ToList();

        Assert.Equal(20, findings.Count);
        Assert.All(findings, finding => Assert.Equal(FindingLevel.Error, finding.Level));
    }

    [Fact]
    public void ADeclaredSizeThatDisagreesWithTheEnrolment_IsAWarning()
    {
        LeaguePyramid pyramid = Pyramid(Div(1, clubs: 20, promotedIn: 0, relegatedOut: 0) with { ClubCount = 18 });

        Finding finding = Assert.Single(PyramidRules.Check(pyramid), f => f.Code == "DIVISION_SIZE");
        Assert.Equal(FindingLevel.Warning, finding.Level);
    }

    [Fact]
    public void AFormatThatCannotUseTheField_IsRefused()
    {
        // Groups of four cannot be made from 18 clubs.
        LeaguePyramid pyramid = Pyramid(
            Div(1, clubs: 18, promotedIn: 0, relegatedOut: 0, format: CompetitionFormat.GroupsKnockout));

        Finding finding = Assert.Single(PyramidRules.Check(pyramid), f => f.Code == "FORMAT_UNPLAYABLE");
        Assert.Contains("18", finding.Detail);
    }

    /// <summary>
    /// The design claim <see cref="PyramidEditor"/> is built on: because a division only ever
    /// enters at the bottom and removing one closes the gap behind it, the two structural rules
    /// cannot be broken from the screen at all. Every intermediate state of a build-up and a
    /// tear-down is checked, not just the ends — a hole that exists for one step is a pyramid the
    /// author can save and walk away from.
    /// </summary>
    [Fact]
    public void NoSequenceOfAddsAndRemovals_CanProduceADuplicateOrMissingTier()
    {
        var pyramid = new LeaguePyramid("BRA", []);
        var structural = new[] { "TIER_DUP", "TIER_GAP" };

        for (int level = 1; level <= 6; level++)
        {
            pyramid = PyramidEditor.AddDivision(
                pyramid, $"div_bra_{level}", $"{level}ª Divisão", CompetitionFormat.LeagueDouble, 20);

            Assert.Equal(Enumerable.Range(1, level), pyramid.Divisions.Select(division => division.Tier));
            Assert.DoesNotContain(Codes(pyramid), code => structural.Contains(code));
        }

        // Out from the middle each time, which is the order that would leave a hole.
        foreach (string divisionId in new[] { "div_bra_3", "div_bra_5", "div_bra_1", "div_bra_2" })
        {
            pyramid = PyramidEditor.RemoveDivision(pyramid, divisionId);

            Assert.Equal(
                Enumerable.Range(1, pyramid.Divisions.Count),
                pyramid.Divisions.Select(division => division.Tier).Order());
            Assert.DoesNotContain(Codes(pyramid), code => structural.Contains(code));
        }
    }
}

/// <summary>
/// Rounds and matches derive from the format and the field size — never typed (Sprint 9). With
/// both an even and an odd field, which is where a round-robin count goes wrong quietly.
/// </summary>
public sealed class CompetitionFormatTests
{
    /// <summary>
    /// The pilot league is the proof this is right: the authored data says 20 clubs, 38 rounds,
    /// and the real Brasileirão plays 380 matches.
    /// </summary>
    [Fact]
    public void ThePilotLeague_DerivesToItsOwnAuthoredNumbers()
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.LeagueDouble, 20);

        Assert.Equal(38, shape.Rounds);
        Assert.Equal(380, shape.Matches);
    }

    [Theory]
    // n even: everyone plays every round, so n−1 rounds.
    [InlineData(20, 19, 190)]
    [InlineData(4, 3, 6)]
    // n odd: someone sits out each round, so it takes n.
    [InlineData(19, 19, 171)]
    [InlineData(5, 5, 10)]
    public void LeagueSingle_IsARoundRobin(int clubs, int rounds, int matches)
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.LeagueSingle, clubs);

        Assert.Equal(rounds, shape.Rounds);
        Assert.Equal(matches, shape.Matches);
    }

    [Theory]
    [InlineData(20, 38, 380)]
    [InlineData(19, 38, 342)]
    [InlineData(4, 6, 12)]
    [InlineData(5, 10, 20)]
    public void LeagueDouble_IsTwiceThat(int clubs, int rounds, int matches)
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.LeagueDouble, clubs);

        Assert.Equal(rounds, shape.Rounds);
        Assert.Equal(matches, shape.Matches);
    }

    [Theory]
    [InlineData(16, 4, 15)]
    [InlineData(8, 3, 7)]
    // Not a power of two: the bracket carries byes, and every club but the winner loses once.
    [InlineData(20, 5, 19)]
    [InlineData(7, 3, 6)]
    public void KnockoutOnly_IsOneBracket(int clubs, int rounds, int matches)
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.KnockoutOnly, clubs);

        Assert.Equal(rounds, shape.Rounds);
        Assert.Equal(matches, shape.Matches);
    }

    [Theory]
    // Two legs per tie, one match for the final.
    [InlineData(16, 7, 29)]
    [InlineData(8, 5, 13)]
    [InlineData(7, 5, 11)]
    public void NationalCup_IsTwoLeggedExceptTheFinal(int clubs, int rounds, int matches)
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.NationalCup, clubs);

        Assert.Equal(rounds, shape.Rounds);
        Assert.Equal(matches, shape.Matches);
    }

    [Theory]
    // 8 clubs: 2 groups of 4 (3 rounds, 12 matches) then 4 qualifiers (2 rounds, 3 matches).
    [InlineData(8, 5, 15)]
    // 32 clubs: 8 groups (3 rounds, 48 matches) then 16 qualifiers (4 rounds, 15 matches).
    [InlineData(32, 7, 63)]
    public void GroupsKnockout_IsGroupsThenABracket(int clubs, int rounds, int matches)
    {
        CompetitionShape shape = CompetitionFormats.Shape(CompetitionFormat.GroupsKnockout, clubs);

        Assert.Equal(rounds, shape.Rounds);
        Assert.Equal(matches, shape.Matches);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(7)]
    public void GroupsKnockout_RefusesAFieldThatDoesNotSplit(int clubs) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CompetitionFormats.Shape(CompetitionFormat.GroupsKnockout, clubs));

    [Theory]
    [InlineData(CompetitionFormat.LeagueSingle)]
    [InlineData(CompetitionFormat.LeagueDouble)]
    [InlineData(CompetitionFormat.GroupsKnockout)]
    [InlineData(CompetitionFormat.KnockoutOnly)]
    [InlineData(CompetitionFormat.NationalCup)]
    public void NoFormatCanBePlayedByOneClub(CompetitionFormat format) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CompetitionFormats.Shape(format, 1));

    [Fact]
    public void EveryFormatHasALabel() =>
        Assert.All(Enum.GetValues<CompetitionFormat>(), format => Assert.NotEmpty(CompetitionFormats.Label(format)));
}
