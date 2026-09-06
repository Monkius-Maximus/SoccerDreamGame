using System.Text.Json.Nodes;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// "Reject a malformed record with a message, never write it" (ROADMAP.md Sprint 2). The reader
/// is the gate: a document with any bad record produces no snapshot, so the importer has nothing
/// to write and the database is untouched.
/// </summary>
public sealed class WorldJsonReaderTests
{
    /// <summary>The real document with one mutation applied, so each test breaks exactly one thing.</summary>
    private static string Mutate(Action<JsonObject> mutation)
    {
        var document = (JsonObject)JsonNode.Parse(WorldFixture.Json)!;
        mutation(document);
        return document.ToJsonString();
    }

    [Fact]
    public void RealDocument_ParsesCompletely()
    {
        WorldSnapshot snapshot = WorldJsonReader.Read(WorldFixture.Json);

        Assert.Equal(18, snapshot.GeoNodes.Count);
        Assert.Equal(20, snapshot.Clubs.Count);
        Assert.Equal(688, snapshot.Characters.Count);
        Assert.Single(snapshot.Competitions);
        Assert.Equal(27, snapshot.Sources.Count);
    }

    [Fact]
    public void GeoNodeWithoutKind_IsRejectedByPath()
    {
        // This is the historical failure: the spreadsheet extraction turned a legend row into a
        // GeoNode with no kind and no display name. It must be named and refused, not stored.
        string json = Mutate(document =>
            document["geoNodes"]!.AsArray().Add(new JsonObject
            {
                ["geoNodeId"] = "geo_legend_row",
                ["parentId"] = "geo_bra",
                // no "kind", no "displayName"
            }));

        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(json));

        string error = Assert.Single(exception.Errors);
        Assert.Contains("geoNodes[18]", error);
        Assert.Contains("kind", error);
    }

    [Fact]
    public void UnknownEnumValue_IsRejected_AndListsWhatIsAllowed()
    {
        string json = Mutate(document =>
            document["clubs"]![0]!["stadium"]!["pitchSurface"] = "Astroturf");

        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(json));

        string error = Assert.Single(exception.Errors);
        Assert.Contains("clubs[0].stadium.pitchSurface", error);
        Assert.Contains("Astroturf", error);
        Assert.Contains("Pristine", error);   // the allowed values are spelled out
    }

    [Fact]
    public void MissingRequiredField_IsRejected()
    {
        string json = Mutate(document => document["clubs"]![2]!.AsObject().Remove("displayCode"));

        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(json));

        Assert.Contains("clubs[2].displayCode", Assert.Single(exception.Errors));
    }

    [Fact]
    public void EveryMalformedRecord_IsReportedTogether()
    {
        // One bad row at a time turns a five-minute data fix into twenty round trips.
        string json = Mutate(document =>
        {
            document["clubs"]![0]!.AsObject().Remove("displayCode");
            document["clubs"]![5]!["world"]!["prestigeBand"] = "B9";
            document["players"]![3]!.AsObject().Remove("shirtNumber");
        });

        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(json));

        Assert.Equal(3, exception.Errors.Count);
        Assert.Contains(exception.Errors, error => error.Contains("clubs[0].displayCode"));
        Assert.Contains(exception.Errors, error => error.Contains("clubs[5].world.prestigeBand"));
        Assert.Contains(exception.Errors, error => error.Contains("players[3].shirtNumber"));
        Assert.Contains("Nothing was written", exception.Message);
    }

    [Fact]
    public void MalformedAttributeBlock_IsRejected()
    {
        string json = Mutate(document => document["players"]![0]!["attrs"]!.AsObject().Remove("Reflexes"));

        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(json));

        Assert.Contains("players[0].attrs.Reflexes", Assert.Single(exception.Errors));
    }

    [Fact]
    public void NotJsonAtAll_IsRejectedWithoutThrowingAParserError()
    {
        var exception = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read("{ not json"));

        Assert.Contains("not valid JSON", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ImportingAMalformedDocument_WritesNothing()
    {
        string json = Mutate(document => document["clubs"]![0]!.AsObject().Remove("displayCode"));

        await using WorldDatabase database = WorldDatabase.Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await Assert.ThrowsAsync<WorldImportException>(() => new WorldImporter(unitOfWork).ImportAsync(json));

        // Not "some clubs" — none, and no geo nodes or calibration either.
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM GeoNodes;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM Characters;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM CalibrationConstants;"));
    }

    [Fact]
    public async Task AFailureMidWrite_RollsBackTheWholeImport()
    {
        // The document parses, but a character references a club that isn't in it. The write fails
        // partway through, and the transaction must leave nothing behind.
        WorldSnapshot snapshot = WorldJsonReader.Read(WorldFixture.Json);
        CharacterRecord orphan = snapshot.Characters[0] with
        {
            PlayerId = "plr_orphan",
            ClubId = "clb_does_not_exist",
        };
        WorldSnapshot broken = snapshot with { Characters = [.. snapshot.Characters, orphan] };

        await using WorldDatabase database = WorldDatabase.Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorldImporter(unitOfWork).ImportAsync(broken));

        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM Characters;"));
        Assert.Equal(0, database.Scalar<long>("SELECT COUNT(*) FROM GeoNodes;"));
    }

    [Fact]
    public async Task ImportingIntoADatabaseThatAlreadyHasAWorld_IsRefusedClearly()
    {
        // Import writes a world; it does not merge two. Without this guard the user gets a UNIQUE
        // constraint error naming a geo node, which says nothing about what went wrong.
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        var exception = await Assert.ThrowsAsync<WorldImportException>(() =>
            new WorldImporter(unitOfWork).ImportAsync(WorldFixture.Json));

        Assert.Contains("already loaded", Assert.Single(exception.Errors));

        // And the world that was there is untouched.
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(688, database.Scalar<long>("SELECT COUNT(*) FROM Characters;"));
    }

    [Fact]
    public void SquadRoleAndPhase_AreNormalisedFromTheSourceSpelling()
    {
        WorldSnapshot snapshot = WorldJsonReader.Read(WorldFixture.Json);

        // "Rotação" in the document becomes the schema's Rotacao; the phase's age-band suffix
        // ("Prospect 15-20") is documentation and is dropped.
        Assert.Contains(snapshot.Characters, character => character.SquadRole == SquadRole.Rotacao);
        Assert.All(snapshot.Characters, character => Assert.True(Enum.IsDefined(character.Phase)));
    }

    [Fact]
    public void SecondaryPositions_AreSplitOnThePipe()
    {
        WorldSnapshot snapshot = WorldJsonReader.Read(WorldFixture.Json);

        Assert.Contains(snapshot.Characters, character => character.SecondaryPositions.Count == 2);
        Assert.Contains(snapshot.Characters, character => character.SecondaryPositions.Count == 1);
        Assert.Contains(snapshot.Characters, character => character.SecondaryPositions.Count == 0);
    }
}
