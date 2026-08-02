using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Content.Tests;

/// <summary>
/// Test-owned bundles. Deliberately NOT the repo's real <c>content/dev</c>: unit tests that
/// assert on shipped content turn every content edit into a red build. The shipped bundle gets
/// exactly one dedicated test of its own (<see cref="ShippedContentTests"/>).
/// </summary>
internal static class TestBundles
{
    /// <summary>A small but complete world: one league, two clubs, a squad each, a fixture.</summary>
    public static ContentBundle Minimal()
    {
        var traits = new List<ContentTrait>
        {
            new() { Id = 1, Key = "hot_headed", DisplayName = "Hot-Headed", Aggression = 85, Selfishness = 40, EventWeightBias = 10 },
            new() { Id = 2, Key = "team_player", DisplayName = "Team Player", Aggression = 30, Selfishness = 10, EventWeightBias = -5 },
        };

        var leagues = new List<ContentLeague>
        {
            new() { Id = 1, Key = "top-flight", Name = "Top Flight", Country = "Testland", Tier = SimulationTier.ActiveHuman },
        };

        var teams = new List<ContentTeam>
        {
            new() { Id = 1, Key = "alpha-fc", Name = "Alpha FC", LeagueKey = "top-flight", Budget = 1_000_000, EloRating = 1600 },
            new() { Id = 2, Key = "beta-united", Name = "Beta United", LeagueKey = "top-flight", Budget = 900_000, EloRating = 1550 },
        };

        var players = new List<ContentPlayer>();
        for (int i = 0; i < 22; i++)
        {
            bool alpha = i < 11;
            players.Add(new ContentPlayer
            {
                Id = i + 1,
                Key = $"player-{i + 1:00}",
                FirstName = $"First{i + 1}",
                LastName = $"Last{i + 1}",
                TeamKey = alpha ? "alpha-fc" : "beta-united",
                Attributes = Attributes(10 + (i % 8)),
                TraitKeys = i == 0 ? ["hot_headed", "team_player"] : [],
            });
        }

        return new ContentBundle
        {
            Manifest = new ContentManifest { BuildId = "test", Generator = "tests", ContentHash = "sha256:test" },
            Traits = traits,
            Leagues = leagues,
            Teams = teams,
            Players = players,
            HousingItems =
            [
                new() { Id = 1, Key = "basic_bed", Name = "Basic Bed", Cost = 0, StatKey = "stamina_recovery", YieldMultiplier = 1.0 },
            ],
            World = new ContentWorld
            {
                StartDate = new DateTime(2026, 8, 1),
                Seasons =
                [
                    new()
                    {
                        Id = 1, Key = "top-flight-2026-27", LeagueKey = "top-flight",
                        StartDate = new DateTime(2026, 8, 1), EndDate = new DateTime(2027, 5, 31), IsCurrent = true,
                    },
                ],
                Fixtures =
                [
                    new()
                    {
                        Id = 1, Key = "alpha-fc-vs-beta-united-2026-08-08", SeasonKey = "top-flight-2026-27",
                        HomeTeamKey = "alpha-fc", AwayTeamKey = "beta-united",
                        KickoffDate = new DateTime(2026, 8, 8, 15, 0, 0),
                    },
                ],
                Finances = [new() { PlayerKey = "player-01", Balance = 1000, BaseSalaryWeekly = 500 }],
                HumanPlayerKey = "player-01",
            },
        };
    }

    public static ContentAttributes Attributes(int value) => new()
    {
        Pace = value,
        Stamina = value,
        Strength = value,
        Passing = value,
        Shooting = value,
        Tackling = value,
        Vision = value,
    };
}

/// <summary>A temp directory that cleans itself up.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "soccersim-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked file handle on a test failure must not mask the real assertion failure.
        }
    }
}
