using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Projection;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Sprints 11b and 11c: <see cref="DivisionCreation"/>, the one write path the CLI and the API
/// share for a whole division (ADR-0011 §6, ADR-0012 §9). Preview writes nothing; apply writes
/// every club, squad and season participant as one act; a failure mid-batch writes nothing; undo
/// takes the batch back exactly; and the result projects onto the game tables.
/// </summary>
public sealed class DivisionCreationPersistenceTests
{
    private const string SerieA = "cmp_bra_tier1";
    private const string SerieB = "bra_t2";
    private const int PilotClubs = 20;
    private const int PilotPlayers = 688;

    private static readonly DivisionGenerationRequest Request =
        new("BRA", SerieB, ClubCount: 6, PrestigeBand.B4, StrengthMin: 0.62, StrengthMax: 0.80, Seed: 2026);

    private static string TestData(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    /// <summary>The pilot world with both profile documents: the pilot league at level 1 with its
    /// twenty clubs, and an empty twenty-seat Série B below it exchanging four.</summary>
    private static async Task<WorldDatabase> WorldWithPyramidAsync()
    {
        WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await unitOfWork.GenerationProfiles.SaveAsync(GenerationProfilesReader.Read(TestData("gen_profiles.json")));
        await unitOfWork.ClubProfiles.SaveAsync(TestData("club_profiles.json"));

        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork);
        LeaguePyramid before = await PyramidStore.LoadAsync(unitOfWork, "BRA");
        LeaguePyramid after = PyramidEditor.AddLevel(before, world, SerieB, "Série B", "geo_bra", legs: 2, clubCount: 20, exchange: 4);

        await unitOfWork.BeginTransactionAsync();
        await PyramidStore.WriteAsync(unitOfWork, before, after);
        await unitOfWork.CommitAsync();

        return database;
    }

    /// <summary>The whole stored world and its scale, as the undo stack sees them.</summary>
    private static async Task<string> SnapshotAsync(SqliteWorldUnitOfWork unitOfWork) =>
        WorldJsonWriter.Write(await WorldStore.LoadAsync(unitOfWork))
        + (await WorldScale.LoadAsync(unitOfWork)).ToJson();

    // ------------------------------------------------------------------ preview

    [Fact]
    public async Task Preview_IsDeterministic_AndWritesNothing()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        string before = await SnapshotAsync(unitOfWork);

        DivisionGenerationResult first = await DivisionCreation.PreviewAsync(unitOfWork, Request);
        DivisionGenerationResult second = await DivisionCreation.PreviewAsync(unitOfWork, Request);

        Assert.Equal(6, first.Clubs.Count);
        Assert.Equal(first.Clubs.Select(club => club.ClubId), second.Clubs.Select(club => club.ClubId));
        Assert.Equal(first.Characters.Select(player => player.PlayerId), second.Characters.Select(player => player.PlayerId));
        Assert.Equal(before, await SnapshotAsync(unitOfWork));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    // ------------------------------------------------------------------ apply

    [Fact]
    public async Task Apply_WritesEveryClubAndSquad_Enrolled_AsOneAct()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        DivisionGenerationResult batch = await DivisionCreation.ApplyAsync(unitOfWork, Request);

        Assert.Equal(PilotClubs + 6, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(PilotPlayers + batch.Characters.Count, database.Scalar<long>("SELECT COUNT(*) FROM Characters;"));

        foreach (ClubIdentity club in batch.Clubs)
        {
            ClubIdentity stored = (await unitOfWork.Clubs.GetAsync(club.ClubId))!;
            Assert.Equal(Provenance.Regen, stored.Provenance);
            Assert.Equal(club.World.SquadSize, (await unitOfWork.Characters.ListByClubAsync(club.ClubId)).Count);
        }

        LeaguePyramid pyramid = await PyramidStore.LoadAsync(unitOfWork, "BRA");
        Assert.Equal(batch.Clubs.Select(club => club.ClubId), pyramid.Find(SerieB)!.Season.ParticipantClubIds);
        Assert.Equal(PilotClubs, pyramid.Find(SerieA)!.Season.ParticipantClubIds.Count);

        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
        Assert.Equal("Gerar divisão Série B (6 clubes)", (await unitOfWork.History.PeekAsync())!.Label);
        Assert.Equal(6, database.Scalar<long>("SELECT COUNT(*) FROM WorldEdits;"));
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(DISTINCT HistoryId) FROM WorldEdits WHERE HistoryId IS NOT NULL;"));
    }

    [Fact]
    public async Task AFailureMidBatch_WritesNothing()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        string before = await SnapshotAsync(unitOfWork);

        // Lets a few squads in, then refuses: the batch is well past its first club when it fails.
        database.Execute(
            $"""
            CREATE TRIGGER fail_mid_batch BEFORE INSERT ON Characters
            WHEN (SELECT COUNT(*) FROM Characters) >= {PilotPlayers + 60}
            BEGIN SELECT RAISE(ABORT, 'mid-batch failure'); END;
            """);

        var error = await Assert.ThrowsAsync<SqliteException>(() => DivisionCreation.ApplyAsync(unitOfWork, Request));
        Assert.Contains("mid-batch failure", error.Message);

        database.Execute("DROP TRIGGER fail_mid_batch;");
        Assert.Equal(before, await SnapshotAsync(unitOfWork));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldEdits;"));
    }

    [Fact]
    public async Task Undo_RestoresThePreviousWorldExactly()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        string before = await SnapshotAsync(unitOfWork);

        await DivisionCreation.ApplyAsync(unitOfWork, Request);
        Assert.NotEqual(before, await SnapshotAsync(unitOfWork));

        Assert.Equal("Gerar divisão Série B (6 clubes)", await WorldHistory.UndoAsync(unitOfWork));
        Assert.Equal(before, await SnapshotAsync(unitOfWork));
    }

    /// <summary>
    /// What Sprint 11 could not do and ADR-0012 §8 makes true: after a division is generated, the
    /// world projects onto the game tables — the pilot and the new Série B as two leagues, every
    /// club a team — the same way `worldbuilder project` does it.
    /// </summary>
    [Fact]
    public async Task AfterApply_TheWorldProjects()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        DivisionGenerationResult batch = await DivisionCreation.ApplyAsync(unitOfWork, Request);

        LegacyWorld legacy = WorldToLegacyProjection.Project(
            await unitOfWork.Clubs.ListAsync(),
            await unitOfWork.Characters.ListAsync(),
            await unitOfWork.Competitions.ListAsync(),
            await unitOfWork.Seasons.ListAsync(),
            await unitOfWork.GeoNodes.ListAsync(),
            await WorldStore.CurrentSeasonAsync(unitOfWork));

        using (SqliteConnection connection = database.Factory.Open())
            new LegacyProjectionWriter(connection).Write(legacy);

        Assert.Equal(["Campeonato Nacional Brasileiro — Primeira Divisão", "Série B"], legacy.Leagues.Select(league => league.Name).Order());
        Assert.Equal(2, database.Scalar<long>("SELECT COUNT(*) FROM Leagues;"));
        Assert.Equal(PilotClubs + batch.Clubs.Count, database.Scalar<long>("SELECT COUNT(*) FROM Teams;"));
        Assert.Equal(PilotPlayers + batch.Characters.Count, database.Scalar<long>("SELECT COUNT(*) FROM Players;"));
    }

    // ------------------------------------------------------------------ what generation needs

    [Fact]
    public async Task WithoutSquadProfiles_NothingIsGenerated()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.ClubProfiles.SaveAsync(TestData("club_profiles.json"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DivisionCreation.ApplyAsync(unitOfWork, Request));

        Assert.Contains("import-profiles", error.Message);
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    [Fact]
    public async Task WithoutClubProfiles_NothingIsGenerated()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DivisionCreation.ApplyAsync(unitOfWork, Request));

        Assert.Contains("BRA has no club profiles", error.Message);
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    [Fact]
    public async Task ACountryWithoutAProfile_IsRefusedByName()
    {
        await using WorldDatabase database = await WorldWithPyramidAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Countries.DeleteAsync("BRA");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DivisionCreation.PreviewAsync(unitOfWork, Request));

        Assert.Contains("BRA has no country profile", error.Message);
    }
}
