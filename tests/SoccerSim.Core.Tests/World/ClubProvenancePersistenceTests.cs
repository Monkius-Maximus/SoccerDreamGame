using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Sprint 10b: a Regen club in SQLite (sql/0018). The table stores provenance explicitly and the
/// repository holds it against the audit row, so a lost audit is corruption, never a Regen club
/// (ADR-0011 §2). Also covers the stored club profiles and <see cref="ClubCreation"/>, the one
/// write path the CLI and the API share.
/// </summary>
public sealed class ClubProvenancePersistenceTests
{
    private const string AnchoredId = "clb_bra_rio_001";

    private static readonly Lazy<string> ProfilesJson = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "club_profiles.json")));

    private static readonly ClubGenerationRequest Request = new("BRA", PrestigeBand.B4, 0.72, Seed: 11);

    private static async Task<WorldDatabase> WorldWithProfilesAsync()
    {
        WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.ClubProfiles.SaveAsync(ProfilesJson.Value);
        return database;
    }

    // ------------------------------------------------------------------ the column

    [Fact]
    public async Task ImportedClubs_AreStoredAsAnchored()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();

        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Clubs WHERE Provenance = 'Anchored';"));
    }

    /// <summary>What a row written before migration 0018 looks like: the column left out. The
    /// default marks it Anchored, which is true of every club that existed then.</summary>
    [Fact]
    public async Task ARowWrittenWithoutTheColumn_IsAnchored()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();

        database.Execute("CREATE TEMP TABLE Old AS SELECT * FROM Clubs WHERE ClubId = 'clb_bra_rio_001';");
        database.Execute("DELETE FROM Clubs WHERE ClubId = 'clb_bra_rio_001';");
        database.Execute("ALTER TABLE temp.Old DROP COLUMN Provenance;");
        database.Execute("INSERT INTO Clubs (" + OldColumns(database) + ") SELECT * FROM temp.Old;");

        Assert.Equal("Anchored", database.Query("SELECT Provenance FROM Clubs WHERE ClubId = 'clb_bra_rio_001';").Single());
    }

    [Fact]
    public async Task TheColumn_RefusesAValueOutsideTheVocabulary()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();

        Assert.Throws<SqliteException>(() =>
            database.Execute("UPDATE Clubs SET Provenance = 'Invented' WHERE ClubId = 'clb_bra_rio_001';"));
    }

    // ------------------------------------------------------------------ round trip

    [Fact]
    public async Task ARegenClub_RoundTrips_WithNoAuditRow()
    {
        await using WorldDatabase database = await WorldWithProfilesAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity regen = await ClubCreation.PreviewAsync(unitOfWork, Request);
        await unitOfWork.Clubs.AddAsync(regen);

        ClubIdentity? read = await unitOfWork.Clubs.GetAsync(regen.ClubId);
        Assert.NotNull(read);
        Assert.Null(read.Audit);
        Assert.Equal(Provenance.Regen, read.Provenance);
        Assert.Equal(regen.Identity, read.Identity);
        Assert.Equal(regen.Kits, read.Kits);

        Assert.Contains(await unitOfWork.Clubs.ListAsync(), club => club.ClubId == regen.ClubId && club.Audit is null);
        Assert.Equal("Regen", database.Query($"SELECT Provenance FROM Clubs WHERE ClubId = '{regen.ClubId}';").Single());
        Assert.Equal(0, database.Scalar<long>($"SELECT COUNT(*) FROM ClubDeviationAudit WHERE ClubId = '{regen.ClubId}';"));

        // An ordinary edit keeps it Regen and writes no audit row.
        await unitOfWork.Clubs.UpdateAsync(read with { Stadium = read.Stadium with { Name = "Arena Nova" } });
        Assert.Equal(0, database.Scalar<long>($"SELECT COUNT(*) FROM ClubDeviationAudit WHERE ClubId = '{regen.ClubId}';"));
    }

    // ------------------------------------------------------------------ disagreement is corruption

    [Fact]
    public async Task AnAnchoredClubWithoutItsAuditRow_Throws()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        database.Execute($"DELETE FROM ClubDeviationAudit WHERE ClubId = '{AnchoredId}';");

        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        var single = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.GetAsync(AnchoredId));
        Assert.Contains("Anchored but has no deviation audit row", single.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.ListAsync());
    }

    [Fact]
    public async Task ARegenClubWithAnAuditRow_Throws()
    {
        await using WorldDatabase database = await WorldWithProfilesAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity regen = await ClubCreation.PreviewAsync(unitOfWork, Request);
        await unitOfWork.Clubs.AddAsync(regen);

        database.Execute($"CREATE TEMP TABLE Stray AS SELECT * FROM ClubDeviationAudit WHERE ClubId = '{AnchoredId}';");
        database.Execute($"UPDATE temp.Stray SET ClubId = '{regen.ClubId}';");
        database.Execute("INSERT INTO ClubDeviationAudit SELECT * FROM temp.Stray;");

        var single = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.GetAsync(regen.ClubId));
        Assert.Contains("Regen but has a deviation audit row", single.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.ListAsync());
    }

    [Fact]
    public async Task AnUpdate_CannotChangeAClubsProvenance()
    {
        await using WorldDatabase database = await WorldWithProfilesAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity anchored = (await unitOfWork.Clubs.GetAsync(AnchoredId))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.UpdateAsync(anchored with { Audit = null }));

        ClubIdentity regen = await ClubCreation.PreviewAsync(unitOfWork, Request);
        await unitOfWork.Clubs.AddAsync(regen);
        ClubDeviationAudit invented = anchored.Audit! with { ClubId = regen.ClubId };
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.Clubs.UpdateAsync(regen with { Audit = invented }));

        // Both refusals left the rows as they were.
        Assert.Equal(1, database.Scalar<long>($"SELECT COUNT(*) FROM ClubDeviationAudit WHERE ClubId = '{AnchoredId}';"));
        Assert.Equal(0, database.Scalar<long>($"SELECT COUNT(*) FROM ClubDeviationAudit WHERE ClubId = '{regen.ClubId}';"));
    }

    // ------------------------------------------------------------------ club profiles

    [Fact]
    public async Task ClubProfiles_AreStoredPerCountry_AndReadBackThroughTheReader()
    {
        await using WorldDatabase database = WorldDatabase.Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        Assert.Empty(await unitOfWork.ClubProfiles.ListCountriesAsync());
        Assert.Null(await unitOfWork.ClubProfiles.GetAsync("BRA"));

        ClubProfiles saved = await unitOfWork.ClubProfiles.SaveAsync(ProfilesJson.Value);
        await unitOfWork.ClubProfiles.SaveAsync(ProfilesJson.Value); // replaces, never duplicates

        Assert.Equal(["BRA"], await unitOfWork.ClubProfiles.ListCountriesAsync());
        ClubProfiles read = (await unitOfWork.ClubProfiles.GetAsync("BRA"))!;
        Assert.Equal(saved.Cities.Select(city => city.GeoNodeId), read.Cities.Select(city => city.GeoNodeId));
        Assert.Equal(saved.ReservedNames, read.ReservedNames);
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM ClubProfileDocuments;"));
    }

    [Fact]
    public async Task AMalformedClubProfileDocument_WritesNothing()
    {
        await using WorldDatabase database = WorldDatabase.Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await Assert.ThrowsAsync<WorldImportException>(() => unitOfWork.ClubProfiles.SaveAsync("""{"countryId": "BRA"}"""));

        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM ClubProfileDocuments;"));
    }

    // ------------------------------------------------------------------ ClubCreation

    [Fact]
    public async Task Preview_IsDeterministic_AndWritesNothing()
    {
        await using WorldDatabase database = await WorldWithProfilesAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity first = await ClubCreation.PreviewAsync(unitOfWork, Request);
        ClubIdentity second = await ClubCreation.PreviewAsync(unitOfWork, Request);

        Assert.Equal(WorldJsonWriter.Write(Single(first)), WorldJsonWriter.Write(Single(second)));
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    [Fact]
    public async Task Apply_WritesTheRegenClub_AsOneUndoableAct()
    {
        await using WorldDatabase database = await WorldWithProfilesAsync();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity club = await ClubCreation.ApplyAsync(unitOfWork, Request);

        Assert.Equal(Provenance.Regen, (await unitOfWork.Clubs.GetAsync(club.ClubId))!.Provenance);
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
        Assert.Equal(1, database.Scalar<long>($"SELECT COUNT(*) FROM WorldEdits WHERE EntityId = '{club.ClubId}' AND HistoryId IS NOT NULL;"));

        Assert.Equal($"Gerar clube {club.Identity.ShortName}", await WorldHistory.UndoAsync(unitOfWork));
        Assert.Null(await unitOfWork.Clubs.GetAsync(club.ClubId));
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
    }

    [Fact]
    public async Task ACountryWithoutClubProfiles_CannotBeGenerated()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ClubCreation.ApplyAsync(unitOfWork, Request));

        Assert.Contains("BRA has no club profiles", error.Message);
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    /// <summary>A one-club world to compare two generated clubs through the JSON writer, which
    /// compares every field by value (records compare the crest colours by reference).</summary>
    private static WorldSnapshot Single(ClubIdentity club)
    {
        WorldSnapshot pilot = WorldJsonReader.Read(WorldFixture.Json);
        return pilot with { Clubs = [club], Characters = [], Competitions = [] };
    }

    private static string OldColumns(WorldDatabase database) =>
        string.Join(", ", database.Query("SELECT name FROM pragma_table_info('Old', 'temp');"));
}
