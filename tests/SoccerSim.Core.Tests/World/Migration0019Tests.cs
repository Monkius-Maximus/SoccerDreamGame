using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Migration 0019 (ADR-0012 §11): over a database as Sprint 11 left it — the pilot as a flat
/// competition, divisions beside it — it converts what is unambiguous and refuses the rest, writing
/// nothing when it refuses. The owner's database is the case this exists for: an empty tier-1
/// "Série A" next to the imported league, and a generated Série B at tier 2.
/// </summary>
public sealed class Migration0019Tests
{
    private static readonly Lazy<WorldSnapshot> Pilot = new(() => WorldJsonReader.Read(WorldFixture.Json));

    /// <summary>A database stopped at 0018 holding the pilot's geography, calibration and clubs, the
    /// pilot league as the old flat competition (4 promoted in, 4 relegated out) and the BRA
    /// country row the divisions hang off.</summary>
    private static async Task<WorldDatabase> Before0019Async()
    {
        WorldDatabase database = WorldDatabase.MigratedThrough(18);
        WorldSnapshot pilot = Pilot.Value;

        await using (SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork())
        {
            await unitOfWork.BeginTransactionAsync();
            foreach (GeoNode node in pilot.GeoNodes)
                await unitOfWork.GeoNodes.AddAsync(node);
            await unitOfWork.Calibration.SaveAsync(pilot.Calibration);
            foreach (ClubIdentity club in pilot.Clubs)
                await unitOfWork.Clubs.AddAsync(club);
            await unitOfWork.CommitAsync();
        }

        database.Execute(
            """
            INSERT INTO Countries (CountryId, Currency, EurToLocal, WageFloorMonthly) VALUES ('BRA', 'BRL', 6.195, 15000);
            INSERT INTO Competitions (CompetitionId, Name, Scope, AnchorGeoNodeId, MemberPredicateId, PrestigeBand,
                LeagueTierFloat, Format, ClubCount, Rounds, PromotedIn, RelegatedOut, ContinentalSlots, EditionId, Season)
            VALUES ('cmp_bra_tier1', 'Campeonato Nacional Brasileiro — Primeira Divisão', 'National', 'geo_bra',
                'pred_top20_by_national_ranking', 'B2', 0.86, 'Pontos corridos, turno e returno', 20, 38, 4, 4,
                '6 (Continental) + 6 (Continental secundária)', 'edt_bra_tier1_2026', 2026);
            """);

        IReadOnlyList<string> members = pilot.Seasons.Single().ParticipantClubIds;
        for (int i = 0; i < members.Count; i++)
            database.Execute($"INSERT INTO CompetitionMembers (CompetitionId, ClubId, Ordinal) VALUES ('cmp_bra_tier1', '{members[i]}', {i});");

        // Two snapshots and an edit linked to one, as a database with undo history has.
        database.Execute(
            """
            INSERT INTO WorldHistory (Id, Label, TakenAt, Document, Scale) VALUES (1, 'old act', '2026-10-01T00:00:00Z', '{}', '{}');
            INSERT INTO WorldEdits (EntityType, EntityId, FieldPath, OldValue, NewValue, EditedAt, HistoryId)
            VALUES ('Club', 'clb_bra_rio_001', 'club', NULL, 'x', '2026-10-01T00:00:00Z', 1);
            """);

        return database;
    }

    private static void Division(WorldDatabase database, string id, int tier, string name, int promotedIn = 0,
        int relegatedOut = 0, string format = "LeagueDouble", params string[] clubs)
    {
        database.Execute(
            $"""
            INSERT INTO Divisions (DivisionId, CountryId, Tier, Name, Format, ClubCount, PromotedIn, RelegatedOut)
            VALUES ('{id}', 'BRA', {tier}, '{name}', '{format}', 20, {promotedIn}, {relegatedOut});
            """);

        for (int i = 0; i < clubs.Length; i++)
            database.Execute($"INSERT INTO DivisionClubs (DivisionId, ClubId, Ordinal) VALUES ('{id}', '{clubs[i]}', {i});");
    }

    /// <summary>A generated Série B needs clubs that are not in the pilot: three Regen clubs made
    /// from pilot rows with new ids.</summary>
    private static async Task<string[]> RegenClubsAsync(WorldDatabase database)
    {
        ClubIdentity template = Pilot.Value.Clubs[0];
        string[] ids = ["clb_bra_manaus_001", "clb_bra_belem_002", "clb_bra_natal_001"];

        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.BeginTransactionAsync();
        for (int i = 0; i < ids.Length; i++)
        {
            await unitOfWork.Clubs.AddAsync(template with
            {
                ClubId = ids[i],
                DisplayCode = $"RG{i}",
                Audit = null,
                AiProfile = template.AiProfile with { DerbyRivalClubId = null },
            });
        }
        await unitOfWork.CommitAsync();

        return ids;
    }

    private static long Version(WorldDatabase database) => database.Scalar<long>("SELECT MAX(Version) FROM SchemaVersions;");

    // ------------------------------------------------------------------ the owner's case

    [Fact]
    public async Task TheOwnersCase_KeepsThePilotAtLevelOne_DropsTheEmptySerieA_AndKeepsSerieBAtLevelTwo()
    {
        await using WorldDatabase database = await Before0019Async();
        string[] regens = await RegenClubsAsync(database);
        Division(database, "bra_t1", 1, "Série A");
        Division(database, "bra_t2", 2, "Série B", clubs: regens);

        database.Migrate();

        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork);

        Assert.Equal(2026, world.Meta.CurrentSeason);
        Assert.Equal(["bra_t2", "cmp_bra_tier1"], world.Competitions.Select(c => c.CompetitionId));

        LeaguePyramid pyramid = LeaguePyramid.Of("BRA", 2026, world.Competitions, world.Seasons);
        PyramidLevel top = pyramid.Levels[0];
        PyramidLevel second = pyramid.Levels[1];

        // The pilot: level 1, two legs from its typed 38 rounds, its edition as a season in order.
        Assert.Equal(("cmp_bra_tier1", 1, "BRA"), (top.Competition.CompetitionId, top.Level, top.Competition.CountryId));
        Assert.Equal([new CompetitionStage(1, StageKind.League, 2)], top.Competition.Stages);
        Assert.Equal(new CompetitionShape(38, 380), top.Shape);
        Assert.Equal("edt_bra_tier1_2026", top.Season.SeasonId);
        Assert.Equal(Pilot.Value.Seasons.Single().ParticipantClubIds, top.Season.ParticipantClubIds);
        Assert.Equal(0.86, CompetitionStages.TierFloat(top.Season, world.Clubs.ToDictionary(club => club.ClubId)));

        // Série B: level 2, the pilot's anchor, its clubs as the current season's participants.
        Assert.Equal(("bra_t2", 2, "geo_bra"), (second.Competition.CompetitionId, second.Level, second.Competition.AnchorGeoNodeId));
        Assert.Equal("edt_bra_t2_2026", second.Season.SeasonId);
        Assert.Equal(regens, second.Season.ParticipantClubIds);

        // The pilot's 4 and 4 become the pair (code reading of PromotedIn, ADR-0012 clarifications):
        // RelegatedOut of 1 sends 17–20 down; PromotedIn of 1 brings 1–4 of level 2 up.
        Assert.Equal([new TransitionRule(17, 20, "bra_t2")], top.Competition.Transitions);
        Assert.Equal([new TransitionRule(1, 4, "cmp_bra_tier1")], second.Competition.Transitions);
        Assert.DoesNotContain(PyramidRules.Check(pyramid, world.Competitions), finding => finding.Code == "PYRAMID_FLOW");
    }

    [Fact]
    public async Task TheOldModel_TheStagingTables_AndTheOldUndoSnapshots_AreGone()
    {
        await using WorldDatabase database = await Before0019Async();
        Division(database, "bra_t1", 1, "Série A");

        database.Migrate();

        Assert.Equal(19, Version(database));
        foreach (string table in new[] { "CompetitionMembers", "Divisions", "DivisionClubs", "OldCompetitions", "LevelCounts", "Migration0019Guard" })
            Assert.Equal(0, database.Scalar<long>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));

        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM WorldEdits WHERE HistoryId IS NULL;"));
    }

    [Fact]
    public async Task ThePilotAlone_HasNoRules_BecauseThereIsNoLevelTwo()
    {
        await using WorldDatabase database = await Before0019Async();

        database.Migrate();

        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM TransitionRules;"));
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM Competitions WHERE Level = 1;"));
    }

    [Fact]
    public async Task AnEmptyDatabase_Migrates_AndGetsItsSeasonOnImport()
    {
        await using WorldDatabase database = WorldDatabase.MigratedThrough(18);

        database.Migrate();

        Assert.Equal(19, Version(database));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM WorldSettings WHERE Key = 'currentSeason';"));
    }

    // ------------------------------------------------------------------ refusals

    private static void AssertAborts(WorldDatabase database, string reason)
    {
        var error = Assert.Throws<SqliteException>(database.Migrate);

        Assert.Contains("0019 aborted: " + reason, error.Message);
        Assert.Equal(18, Version(database));
        // Nothing was written: the old model is still there, whole.
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM Competitions WHERE MemberPredicateId IS NOT NULL;"));
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM WorldHistory;"));
    }

    [Fact]
    public async Task ATierOneDivisionWithClubs_NextToTheImportedLeague_Aborts()
    {
        await using WorldDatabase database = await Before0019Async();
        string[] regens = await RegenClubsAsync(database);
        Division(database, "bra_t1", 1, "Série A", clubs: regens);

        AssertAborts(database, "a tier-1 division has clubs in a country whose imported league is level 1");
        Assert.Equal(3, database.Scalar<long>("SELECT COUNT(*) FROM DivisionClubs;"));
    }

    [Fact]
    public async Task ADivisionThatIsNotALeague_Aborts()
    {
        await using WorldDatabase database = await Before0019Async();
        Division(database, "bra_cup", 2, "Copa", format: "NationalCup");

        AssertAborts(database, "a division's format is not a league");
    }

    [Fact]
    public async Task ACompetitionWithMembersInTwoCountries_Aborts()
    {
        await using WorldDatabase database = await Before0019Async();
        database.Execute("UPDATE Clubs SET CountryId = 'ARG' WHERE ClubId = 'clb_bra_rio_001';");

        AssertAborts(database, "a national competition has members in more than one country");
    }

    [Fact]
    public async Task ACompetitionThatIsNotNational_Aborts()
    {
        await using WorldDatabase database = await Before0019Async();
        database.Execute("UPDATE Competitions SET Scope = 'Continental';");

        AssertAborts(database, "a competition that is not National cannot be represented");
    }

    [Fact]
    public async Task RoundsThatAreNeitherOneNorTwoLegs_Abort()
    {
        await using WorldDatabase database = await Before0019Async();
        database.Execute("UPDATE Competitions SET Rounds = 30;");

        AssertAborts(database, "a competition's typed rounds are neither a single nor a double round robin");
    }

    [Fact]
    public async Task ADivisionInACountryWithNoCompetition_HasNoAnchor_AndAborts()
    {
        await using WorldDatabase database = await Before0019Async();
        database.Execute(
            """
            INSERT INTO Countries (CountryId, Currency, EurToLocal, WageFloorMonthly) VALUES ('ARG', 'ARS', 1000, 100);
            INSERT INTO Divisions (DivisionId, CountryId, Tier, Name, Format, ClubCount, PromotedIn, RelegatedOut)
            VALUES ('arg_t1', 'ARG', 1, 'Primera', 'LeagueDouble', 20, 0, 0);
            """);

        AssertAborts(database, "a division is in a country with no national competition to take its geo anchor from");
    }
}
