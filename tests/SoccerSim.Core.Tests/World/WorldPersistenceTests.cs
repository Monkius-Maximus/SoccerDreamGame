using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Sprint 2's "done when": <c>worldbuilder import</c> loads the whole real batch and reading it
/// back loses nothing. The counts (20 clubs, 688 characters, 18 geo nodes, 27 sources) are the
/// contract's, and the field-by-field comparison is what makes "no loss" mean something.
/// </summary>
public sealed class WorldPersistenceTests
{
    [Fact]
    public async Task Import_LoadsTheWholeBatch()
    {
        await using WorldDatabase database = WorldDatabase.Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        WorldImportReport report = await new WorldImporter(unitOfWork).ImportAsync(WorldFixture.Json);

        Assert.Equal(18, report.GeoNodes);
        Assert.Equal(20, report.Clubs);
        Assert.Equal(688, report.Characters);
        Assert.Equal(1, report.Competitions);
        Assert.Equal(27, report.Sources);
    }

    [Fact]
    public async Task Import_ThenRead_ReturnsTheSameCounts()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        Assert.Equal(18, (await unitOfWork.GeoNodes.ListAsync()).Count);
        Assert.Equal(20, (await unitOfWork.Clubs.ListAsync()).Count);
        Assert.Equal(688, (await unitOfWork.Characters.ListAsync()).Count);
        Assert.Single(await unitOfWork.Competitions.ListAsync());
        Assert.Equal(27, (await unitOfWork.Sources.ListAsync()).Count);
        Assert.Equal(688 * 12, database.Scalar<long>("SELECT COUNT(*) FROM CharacterAttributes;"));
    }

    [Fact]
    public async Task Clubs_RoundTrip_WithoutLosingAnyField()
    {
        WorldSnapshot source = WorldJsonReader.Read(WorldFixture.Json);
        WorldCalibration calibration = source.Calibration;

        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        IReadOnlyList<ClubIdentity> stored = await unitOfWork.Clubs.ListAsync();
        Dictionary<string, ClubIdentity> byId = stored.ToDictionary(club => club.ClubId);

        Assert.Equal(source.Clubs.Count, stored.Count);
        foreach (ClubIdentity expected in source.Clubs)
        {
            // The expected value is the source club with its derived fields filled in — the same
            // transformation the write path applies.
            ClubIdentity withDerived = WorldDerivations.Recalculate(expected, calibration);
            ClubIdentity actual = byId[expected.ClubId];

            // Records compare collection members by REFERENCE, so the crest colors are asserted
            // as a sequence first and then aligned; the remaining comparison covers every other
            // field, including the whole nested audit block.
            Assert.Equal(withDerived.Crest.Colors, actual.Crest.Colors);
            Assert.Equal(
                withDerived with { Crest = withDerived.Crest with { Colors = actual.Crest.Colors } },
                actual);
        }
    }

    [Fact]
    public async Task Characters_RoundTrip_WithoutLosingAnyField()
    {
        WorldSnapshot source = WorldJsonReader.Read(WorldFixture.Json);
        Dictionary<string, PrestigeBand> bands = source.Clubs.ToDictionary(
            club => club.ClubId,
            club => club.World.PrestigeBand);

        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        IReadOnlyList<CharacterRecord> stored = await unitOfWork.Characters.ListAsync();
        Dictionary<string, CharacterRecord> byId = stored.ToDictionary(character => character.PlayerId);

        Assert.Equal(source.Characters.Count, stored.Count);
        foreach (CharacterRecord expected in source.Characters)
        {
            CharacterRecord withDerived = WorldDerivations.Recalculate(
                expected, bands[expected.ClubId], source.Calibration);
            CharacterRecord actual = byId[expected.PlayerId];

            // Attrs and SecondaryPositions are collections, which records compare by reference;
            // assert their contents explicitly, then compare every remaining field.
            Assert.Equal(withDerived.Attrs.OrderBy(pair => pair.Key), actual.Attrs.OrderBy(pair => pair.Key));
            Assert.Equal(withDerived.SecondaryPositions, actual.SecondaryPositions);
            Assert.Equal(
                withDerived with { Attrs = actual.Attrs, SecondaryPositions = actual.SecondaryPositions },
                actual);
        }
    }

    [Fact]
    public async Task StoredEconomy_StillMatchesTheRecordedValues()
    {
        // The Sprint 1 gate proved the formulas reproduce the batch. This proves the round trip
        // through SQLite does not disturb them — recalculating on write is only safe because the
        // recalculated values are the recorded ones.
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        Dictionary<string, CharacterRecord> stored =
            (await unitOfWork.Characters.ListAsync()).ToDictionary(character => character.PlayerId);

        foreach (System.Text.Json.Nodes.JsonNode? node in WorldFixture.Data["players"]!.AsArray())
        {
            System.Text.Json.Nodes.JsonObject player = node!.AsObject();
            CharacterRecord actual = stored[player["playerId"]!.GetValue<string>()];

            Assert.Equal(player["overall"]!.GetValue<int>(), actual.Overall);
            Assert.Equal(player["potentialOverall"]!.GetValue<int>(), actual.PotentialOverall);
            Assert.Equal(player["marketValueEUR"]!.GetValue<int>(), actual.MarketValueEur);
            Assert.Equal(player["salaryMonthlyBRL"]!.GetValue<int>(), actual.SalaryMonthlyBrl);
        }
    }

    [Fact]
    public async Task Calibration_RoundTrips()
    {
        WorldSnapshot source = WorldJsonReader.Read(WorldFixture.Json);

        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        WorldCalibration? stored = await unitOfWork.Calibration.GetAsync();

        Assert.NotNull(stored);
        Assert.Equal(source.Calibration.Constants.OrderBy(c => c.Key), stored!.Constants.OrderBy(c => c.Key));
        Assert.Equal(source.Calibration.AgeMult, stored.AgeMult);
        Assert.Equal(source.Calibration.Bands.OrderBy(b => b.Key), stored.Bands.OrderBy(b => b.Key));
        Assert.Equal(source.Calibration.HomeAdv.OrderBy(h => h.Key), stored.HomeAdv.OrderBy(h => h.Key));
        Assert.Equal(source.Calibration.StadiumProfile.OrderBy(s => s.Key), stored.StadiumProfile.OrderBy(s => s.Key));

        foreach (Position position in Enum.GetValues<Position>())
        {
            Assert.Equal(
                source.Calibration.PositionWeights[position].OrderBy(w => w.Key),
                stored.PositionWeights[position].OrderBy(w => w.Key));
        }
    }

    [Fact]
    public async Task Competition_RoundTrips_WithMembersInOrder()
    {
        WorldSnapshot source = WorldJsonReader.Read(WorldFixture.Json);

        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        Competition expected = source.Competitions.Single();
        Competition? actual = await unitOfWork.Competitions.GetAsync(expected.CompetitionId);

        Assert.NotNull(actual);
        Assert.Equal(expected with { MemberClubIds = actual!.MemberClubIds }, actual);
        Assert.Equal(expected.MemberClubIds, actual.MemberClubIds);   // order preserved
    }

    [Fact]
    public async Task Sources_RoundTrip_IncludingTheProseOnlyRow()
    {
        WorldSnapshot source = WorldJsonReader.Read(WorldFixture.Json);

        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        IReadOnlyList<WorldSource> stored = await unitOfWork.Sources.ListAsync();

        Assert.Equal(source.Sources, stored);

        // One of the 27 is a prose cross-reference with no number, publisher or URL. It must
        // survive as a note rather than being dropped or turned into an empty link.
        WorldSource prose = Assert.Single(stored, entry => entry.Url is null);
        Assert.Null(prose.Numero);
        Assert.Null(prose.Fonte);
        Assert.Contains("Audit_Clubes", prose.Tema);
    }

    [Fact]
    public async Task Squads_AreReachableByClub()
    {
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync();
        int total = 0;

        foreach (ClubIdentity club in clubs)
        {
            IReadOnlyList<CharacterRecord> squad = await unitOfWork.Characters.ListByClubAsync(club.ClubId);

            // The authored squad size is the roster count for every club in the batch.
            Assert.Equal(club.World.SquadSize, squad.Count);
            Assert.All(squad, character => Assert.Equal(12, character.Attrs.Count));
            total += squad.Count;
        }

        Assert.Equal(688, total);
    }

    [Fact]
    public async Task DerbyRivalries_SurviveTheirMutualReferences()
    {
        // Derbies point both ways, so no insertion order satisfies the self-referencing foreign
        // key row by row — the import defers foreign keys to commit. This is that guarantee.
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync();
        Dictionary<string, ClubIdentity> byId = clubs.ToDictionary(club => club.ClubId);

        List<ClubIdentity> withRival = clubs.Where(club => club.AiProfile.DerbyRivalClubId is not null).ToList();

        Assert.NotEmpty(withRival);
        Assert.All(withRival, club => Assert.True(
            byId.ContainsKey(club.AiProfile.DerbyRivalClubId!),
            $"{club.ClubId} points at missing rival {club.AiProfile.DerbyRivalClubId}"));
    }
}
