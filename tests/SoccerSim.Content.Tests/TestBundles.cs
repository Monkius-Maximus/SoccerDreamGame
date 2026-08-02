using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.Tactics;

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
            new()
            {
                Id = 1, Key = "top-flight", Name = "Top Flight", Country = "Testland",
                Tier = SimulationTier.ActiveHuman, CompetitionKey = "test-league",
                NationKey = "testland", PyramidLevel = 1, RelegationSlots = 2,
            },
        };

        var teams = new List<ContentTeam>
        {
            new()
            {
                Id = 1, Key = "alpha-fc", Name = "Alpha FC", LeagueKey = "top-flight",
                Budget = 1_000_000, EloRating = 1600, ShortName = "ALP", NationKey = "testland",
                StadiumKey = "alpha-park", FoundedYear = 1901, Reputation = 65,
            },
            // Deliberately leaves the optional references null, so the round-trip covers both
            // the populated and the absent case for every nullable foreign key.
            new()
            {
                Id = 2, Key = "beta-united", Name = "Beta United", LeagueKey = "top-flight",
                Budget = 900_000, EloRating = 1550,
            },
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
                // The first player of each side keeps every optional field populated; the rest
                // leave them at their defaults, so both paths round-trip.
                PrimaryRole = i % 11 == 0 ? PlayerRole.Goalkeeper : PlayerRole.Midfielder,
                Flank = i % 3 == 0 ? Flank.Left : Flank.Centre,
                PreferredFoot = i % 3 == 0 ? PreferredFoot.Left : PreferredFoot.Right,
                DateOfBirth = i == 0 ? new DateTime(1998, 5, 6) : null,
                NationKey = i == 0 ? "testland" : null,
                SquadNumber = i == 0 ? 1 : null,
                HeightCm = i == 0 ? 188 : null,
            });
        }

        var coaches = new List<ContentCoach>
        {
            new()
            {
                Id = 1, Key = "gaffer-one", FirstName = "Gaffer", LastName = "One",
                TeamKey = "alpha-fc", NationKey = "testland", Role = CoachRole.HeadCoach,
                DateOfBirth = new DateTime(1975, 4, 1), Coaching = 15, TacticalKnowledge = 14,
                ManManagement = 13, Fitness = 12, Scouting = 11, PreferredMentality = "Attacking",
            },
            new()
            {
                Id = 2, Key = "gaffer-two", FirstName = "Gaffer", LastName = "Two",
                TeamKey = "beta-united", Role = CoachRole.GoalkeepingCoach,
            },
        };

        return new ContentBundle
        {
            Manifest = new ContentManifest { BuildId = "test", Generator = "tests", ContentHash = "sha256:test" },
            Nations =
            [
                new() { Id = 1, Key = "testland", Name = "Testland", Code = "TST", Adjective = "Testish", Confederation = "TestConf", Reputation = 60 },
            ],
            Stadiums =
            [
                new()
                {
                    Id = 1, Key = "alpha-park", Name = "Alpha Park", NationKey = "testland",
                    City = "Alphaville", Capacity = 20_000, PitchLengthM = 104, PitchWidthM = 67,
                    Surface = PitchSurface.Hybrid, YearBuilt = 1950,
                },
            ],
            Competitions =
            [
                new()
                {
                    Id = 1, Key = "test-league", Name = "Test League", ShortName = "TL",
                    NationKey = "testland", Format = CompetitionFormat.League,
                    Scope = CompetitionScope.Domestic, Reputation = 55,
                },
            ],
            Traits = traits,
            Leagues = leagues,
            Teams = teams,
            Players = players,
            Coaches = coaches,
            Contracts =
            [
                // Keys are numbered so ordinal key order matches id order. The JSON writer sorts
                // by key and the SQLite reader returns rows by id; keeping the two orders aligned
                // is what lets the round-trip assertions stay a strict string comparison.
                new()
                {
                    Id = 1, Key = "contract-01-player", PlayerKey = "player-01", TeamKey = "alpha-fc",
                    StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2029, 6, 30),
                    WeeklyWage = 5000, SigningBonus = 20_000, ReleaseClause = 1_000_000,
                    SquadStatus = SquadStatus.Key,
                },
                new()
                {
                    Id = 2, Key = "contract-02-coach", CoachKey = "gaffer-one", TeamKey = "alpha-fc",
                    StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2028, 6, 30),
                    WeeklyWage = 9000, SquadStatus = SquadStatus.FirstTeam,
                },
            ],
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
