using SoccerSim.Content.Model;
using SoccerSim.Content.Validation;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// One case per rule. The validator is what the authoring tool trusts to say "this is safe to
/// export", so every rule needs a test that proves it actually fires — a rule that silently
/// never triggers is worse than no rule, because it buys false confidence.
/// </summary>
public sealed class ContentValidatorTests
{
    [Fact]
    public void MinimalBundle_IsValid()
    {
        ContentValidationResult result = Validate(TestBundles.Minimal());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void DanglingLeagueReference_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with
        {
            Teams = [bundle.Teams[0] with { LeagueKey = "no-such-league" }, bundle.Teams[1]],
        };

        AssertError(bundle, ContentValidator.Codes.RefDangling);
    }

    [Fact]
    public void DuplicateKey_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with
        {
            Teams = [bundle.Teams[0], bundle.Teams[1] with { Key = bundle.Teams[0].Key }],
        };

        AssertError(bundle, ContentValidator.Codes.KeyDuplicate);
    }

    [Fact]
    public void DuplicateId_IsAnError_BecauseItCorruptsForeignKeys()
    {
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with
        {
            Teams = [bundle.Teams[0], bundle.Teams[1] with { Id = bundle.Teams[0].Id }],
        };

        AssertError(bundle, ContentValidator.Codes.IdDuplicate);
    }

    [Theory]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("1leading-digit")]
    [InlineData("trailing-")]
    [InlineData("")]
    public void MalformedKey_IsAnError(string key)
    {
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with { Teams = [bundle.Teams[0] with { Key = key }, bundle.Teams[1]] };

        AssertError(bundle, ContentValidator.Codes.KeyFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-3)]
    public void AttributeOutsideOneToTwenty_IsAnError(int value)
    {
        ContentBundle bundle = TestBundles.Minimal();
        ContentPlayer first = bundle.Players[0];
        bundle = bundle with
        {
            Players = [first with { Attributes = first.Attributes with { Pace = value } }, .. bundle.Players.Skip(1)],
        };

        AssertError(bundle, ContentValidator.Codes.Range);
    }

    [Fact]
    public void FixtureAgainstItself_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        ContentFixture fixture = bundle.World.Fixtures[0];
        bundle = bundle with
        {
            World = bundle.World with { Fixtures = [fixture with { AwayTeamKey = fixture.HomeTeamKey }] },
        };

        AssertError(bundle, ContentValidator.Codes.FixtureSameTeam);
    }

    [Fact]
    public void SeasonEndingBeforeItStarts_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        ContentSeason season = bundle.World.Seasons[0];
        bundle = bundle with
        {
            World = bundle.World with { Seasons = [season with { EndDate = season.StartDate.AddDays(-1) }] },
        };

        AssertError(bundle, ContentValidator.Codes.SeasonDates);
    }

    [Fact]
    public void HumanPlayerWithoutAClub_IsAnError()
    {
        // SqliteCareerService throws when the human has no team, so this would be a guaranteed
        // crash at startup rather than a content problem the player could work around.
        ContentBundle bundle = TestBundles.Minimal();
        ContentPlayer human = bundle.Players[0];
        bundle = bundle with { Players = [human with { TeamKey = null }, .. bundle.Players.Skip(1)] };

        AssertError(bundle, ContentValidator.Codes.CareerNoTeam);
    }

    [Fact]
    public void DuplicateTraitAssignment_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        ContentPlayer first = bundle.Players[0];
        bundle = bundle with
        {
            Players = [first with { TraitKeys = ["hot_headed", "hot_headed"] }, .. bundle.Players.Skip(1)],
        };

        AssertError(bundle, ContentValidator.Codes.TraitDuplicate);
    }

    [Fact]
    public void TwoCurrentSeasonsInOneLeague_IsAnError()
    {
        ContentBundle bundle = TestBundles.Minimal();
        ContentSeason season = bundle.World.Seasons[0];
        bundle = bundle with
        {
            World = bundle.World with
            {
                Seasons =
                [
                    season,
                    season with { Id = 2, Key = "top-flight-2027-28", StartDate = season.StartDate.AddYears(1), EndDate = season.EndDate.AddYears(1) },
                ],
            },
        };

        AssertError(bundle, ContentValidator.Codes.SeasonCurrentDuplicate);
    }

    [Fact]
    public void ShortTier1Squad_IsAWarning_NotAnError()
    {
        // A half-entered squad must stay exportable, or the tool becomes unusable mid-edit.
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with { Players = bundle.Players.Take(3).ToList() };

        ContentValidationResult result = Validate(bundle);

        Assert.Contains(result.Warnings, i => i.Code == ContentValidator.Codes.SquadTooSmall);
        Assert.DoesNotContain(result.Errors, i => i.Code == ContentValidator.Codes.SquadTooSmall);
    }

    [Fact]
    public void ThrowIfInvalid_ListsEveryError_NotJustTheFirst()
    {
        ContentBundle bundle = TestBundles.Minimal();
        bundle = bundle with
        {
            Teams =
            [
                bundle.Teams[0] with { LeagueKey = "missing-one" },
                bundle.Teams[1] with { LeagueKey = "missing-two" },
            ],
        };

        ContentValidationException error =
            Assert.Throws<ContentValidationException>(() => Validate(bundle).ThrowIfInvalid());

        Assert.Contains("missing-one", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing-two", error.Message, StringComparison.Ordinal);
    }

    private static ContentValidationResult Validate(ContentBundle bundle)
        => ContentValidator.Default.Validate(bundle);

    private static void AssertError(ContentBundle bundle, string code)
    {
        ContentValidationResult result = Validate(bundle);
        Assert.Contains(result.Errors, issue => issue.Code == code);
    }
}
