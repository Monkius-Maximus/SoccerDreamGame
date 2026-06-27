using SoccerSim.Core.Domain;
using SoccerSim.Core.Modes;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class MatchEntryGuardTests
{
    private static Match Fixture(bool played = false, int home = 1, int away = 2) => new()
    {
        Id = 7,
        SeasonId = 1,
        LeagueId = 1,
        HomeTeamId = home,
        AwayTeamId = away,
        KickoffDate = new DateTime(2026, 8, 10),
        Played = played,
    };

    private static bool AllClubsExist(int _) => true;

    [Fact]
    public void Valid_Fixture_DoesNotThrow()
    {
        MatchEntryGuard.Validate(Fixture(), AllClubsExist);
    }

    [Fact]
    public void Null_Fixture_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MatchEntryGuard.Validate(null, AllClubsExist));
    }

    [Fact]
    public void AlreadyPlayed_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => MatchEntryGuard.Validate(Fixture(played: true), AllClubsExist));
    }

    [Fact]
    public void SameHomeAndAwayClub_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => MatchEntryGuard.Validate(Fixture(home: 3, away: 3), AllClubsExist));
    }

    [Fact]
    public void MissingClub_Throws()
    {
        // Away club (2) does not exist in the world.
        Assert.Throws<InvalidOperationException>(
            () => MatchEntryGuard.Validate(Fixture(home: 1, away: 2), id => id == 1));
    }
}
