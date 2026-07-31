using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.LifeSim;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Round-trips the life-sim through the 0006 migration, and pins the career-role column that
/// selects which need profile a save runs under.
/// </summary>
public sealed class WellbeingPersistenceTests
{
    private static readonly DateTime Day = new(2026, 8, 1);

    // A shared in-memory DB lives only while at least one connection is open, so each test holds a
    // keep-alive connection for its duration (same pattern as CareerTests).
    private static SqliteConnection NewMigratedDb(bool includeSeeds = true)
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds);
        return keepAlive;
    }

    [Fact]
    public void Load_FreshCareer_ReturnsNullSoTheCallerSeedsADefault()
    {
        using SqliteConnection connection = NewMigratedDb();

        Assert.Null(new SqliteWellbeingRepository(connection).Load(1, CareerRole.Player));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryNeed()
    {
        using SqliteConnection connection = NewMigratedDb();
        var repository = new SqliteWellbeingRepository(connection);
        WellbeingState saved = WellbeingState.CreateDefault(CareerRole.Player);
        saved.Set(NeedKind.Energy, 12.5);
        saved.Set(NeedKind.Focus, 88.25);

        repository.Save(1, saved);
        WellbeingState? loaded = repository.Load(1, CareerRole.Player);

        Assert.NotNull(loaded);
        foreach (NeedKind need in Needs.All)
            Assert.Equal(saved[need], loaded![need], precision: 9);
    }

    [Fact]
    public void Save_IsIdempotent_AndOverwritesInPlace()
    {
        using SqliteConnection connection = NewMigratedDb();
        var repository = new SqliteWellbeingRepository(connection);
        WellbeingState state = WellbeingState.CreateDefault(CareerRole.Manager);

        repository.Save(1, state);
        state.Set(NeedKind.Morale, 41.0);
        repository.Save(1, state);

        Assert.Equal(41.0, repository.Load(1, CareerRole.Manager)![NeedKind.Morale], precision: 9);
        Assert.Equal(Needs.All.Length, CountWellbeingRows(connection));
    }

    [Fact]
    public void Save_RestoresUnderTheRoleAsked_SoTheProfileFollowsTheCareer()
    {
        using SqliteConnection connection = NewMigratedDb();
        var repository = new SqliteWellbeingRepository(connection);
        repository.Save(1, WellbeingState.CreateDefault(CareerRole.Player));

        WellbeingState asManager = repository.Load(1, CareerRole.Manager)!;

        // Same gauges, manager physics — the shared table is what lets one save shape serve both.
        Assert.Equal(CareerRole.Manager, asManager.Role);
        Assert.Equal(NeedProfile.Manager[NeedKind.Focus].Weight, asManager.Profile[NeedKind.Focus].Weight);
    }

    [Fact]
    public void LogActivity_AppendsWithDateAndCost()
    {
        using SqliteConnection connection = NewMigratedDb();
        var repository = new SqliteWellbeingRepository(connection);

        repository.LogActivity(1, Day, new ActivityOutcome("physio", Array.Empty<NeedDelta>(), 1_200));
        repository.LogActivity(1, Day.AddDays(1), new ActivityOutcome("leisure", Array.Empty<NeedDelta>(), 2_000));

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT ActivityKey, Cost FROM LifeActivityLog WHERE CareerId = 1 ORDER BY Date;";
        using SqliteDataReader reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("physio", reader.GetString(0));
        Assert.Equal(1_200, reader.GetInt64(1));
        Assert.True(reader.Read());
        Assert.Equal("leisure", reader.GetString(0));
    }

    [Fact]
    public void Load_PartiallySavedState_ThrowsRatherThanReadingAPhantomDeficit()
    {
        using SqliteConnection connection = NewMigratedDb();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO CareerWellbeing (CareerId, NeedKey, Value) VALUES (1, 'Energy', 50);";
            command.ExecuteNonQuery();
        }

        // A missing gauge must not default to zero and read as a critical need the human never earned.
        Assert.Throws<InvalidOperationException>(
            () => new SqliteWellbeingRepository(connection).Load(1, CareerRole.Player));
    }

    [Fact]
    public void Load_UnknownNeedKey_Throws()
    {
        using SqliteConnection connection = NewMigratedDb();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO CareerWellbeing (CareerId, NeedKey, Value) VALUES (1, 'Vibes', 50);";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(
            () => new SqliteWellbeingRepository(connection).Load(1, CareerRole.Player));
    }

    [Fact]
    public void Migration_DefaultsExistingCareersToThePlayerRole()
    {
        using SqliteConnection connection = NewMigratedDb();

        CareerState? career = new SqliteCareerService(connection).GetActiveCareer();

        Assert.NotNull(career);
        Assert.Equal(CareerRole.Player, career!.Role);
    }

    [Fact]
    public void CareerService_UnknownRole_Throws()
    {
        using SqliteConnection connection = NewMigratedDb();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Career SET Role = 'Chairman' WHERE Id = 1;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => new SqliteCareerService(connection).GetActiveCareer());
    }

    [Fact]
    public void ManagerCareer_LoadsWithTheManagerProfile()
    {
        using SqliteConnection connection = NewMigratedDb();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Career SET Role = 'Manager' WHERE Id = 1;";
            command.ExecuteNonQuery();
        }

        CareerState career = new SqliteCareerService(connection).GetActiveCareer()!;

        Assert.Equal(CareerRole.Manager, career.Role);
        Assert.Equal(NeedProfile.Manager[NeedKind.Fitness].Weight,
            NeedProfile.For(career.Role)[NeedKind.Fitness].Weight);
    }

    [Fact]
    public void WellbeingGauge_OutOfRangeValue_IsRejectedByTheSchema()
    {
        using SqliteConnection connection = NewMigratedDb();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO CareerWellbeing (CareerId, NeedKey, Value) VALUES (1, 'Energy', 140);";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    private static long CountWellbeingRows(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CareerWellbeing;";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
