using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Validation;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// What the schema refuses to store, and — just as importantly — what it deliberately accepts.
///
/// <para>
/// The dividing line: structural corruption (a duplicate code, an attribute of 150, an enum value
/// that isn't in the schema) is rejected by the database. An authoring invariant that the batch
/// currently violates is NOT, because the tool has to be able to hold a work-in-progress state and
/// the audit sweep has to be able to see the bad row in order to report it.
/// </para>
/// </summary>
public sealed class WorldConstraintTests
{
    [Fact]
    public async Task DuplicateDisplayCode_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await unitOfWork.Clubs.AddAsync(WorldSamples.Club(clubId: "clb_a", displayCode: "TST"));

        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Clubs.AddAsync(WorldSamples.Club(clubId: "clb_b", displayCode: "TST")));
    }

    [Fact]
    public async Task DuplicateShirtNumberInTheSameClub_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        await unitOfWork.Characters.AddAsync(WorldSamples.Character(playerId: "plr_a", shirtNumber: 10));

        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Characters.AddAsync(WorldSamples.Character(playerId: "plr_b", shirtNumber: 10)));
    }

    [Fact]
    public async Task SameShirtNumberInDifferentClubs_IsAllowed()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club(clubId: "clb_a", displayCode: "AAA"));
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club(clubId: "clb_b", displayCode: "BBB"));

        await unitOfWork.Characters.AddAsync(WorldSamples.Character(playerId: "plr_a", clubId: "clb_a", shirtNumber: 10));
        await unitOfWork.Characters.AddAsync(WorldSamples.Character(playerId: "plr_b", clubId: "clb_b", shirtNumber: 10));

        Assert.Equal(2, (await unitOfWork.Characters.ListAsync()).Count);
    }

    [Fact]
    public async Task AttributeOutsideOneToNinetyNine_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        CharacterRecord character = WorldSamples.Character();
        var attrs = new Dictionary<Attr, int>(character.Attrs) { [Attr.Finishing] = 150 };

        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Characters.AddAsync(character with { Attrs = attrs }));
    }

    [Fact]
    public async Task EnumValueOutsideTheSchema_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity club = WorldSamples.Club();

        // An out-of-range enum stringifies to its number ("999"), which no CHECK list contains.
        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Clubs.AddAsync(club with { Stadium = club.Stadium with { PitchSurface = (PitchSurface)999 } }));
    }

    [Fact]
    public async Task ClubReferencingAMissingGeoNode_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Clubs.AddAsync(WorldSamples.Club(geoNodeId: "geo_does_not_exist")));
    }

    [Fact]
    public async Task SquadSizeOutsideTwentyEightToForty_IsRejected()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity club = WorldSamples.Club();

        await Assert.ThrowsAsync<SqliteException>(() =>
            unitOfWork.Clubs.AddAsync(club with { World = club.World with { SquadSize = 12 } }));
    }

    [Fact]
    public async Task PhoneticSimilarityOutsideTheWindow_IsStored_AndReportedAsAnInvariantError()
    {
        // DATA_CONTRACT.md §7 proposes CHECK (PhoneticSimilarity BETWEEN 0.55 AND 0.80). It is
        // deliberately NOT applied: the real batch contains clb_bra_bel_001 (NamingRule =
        // Phonetic, similarity 0.867). A CHECK would make that club unimportable, and therefore
        // invisible in the very tool whose job is to surface and fix it. The window is enforced
        // as an invariant instead — loud, but not blocking.
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity club = WorldSamples.Club();
        ClubIdentity outsideWindow = club with
        {
            World = club.World with { NamingRule = NamingRule.Phonetic },
            Audit = club.Audit with { NamingRule = NamingRule.Phonetic, PhoneticSimilarity = 0.867 },
        };

        await unitOfWork.Clubs.AddAsync(outsideWindow);
        ClubIdentity? stored = await unitOfWork.Clubs.GetAsync(outsideWindow.ClubId);

        Assert.NotNull(stored);
        Assert.Equal(0.867, stored!.Audit.PhoneticSimilarity);

        Finding phonetic = ClubInvariants
            .Check(stored, WorldFixture.BuildCalibration())
            .Single(finding => finding.Code == "PHONETIC_WINDOW");
        Assert.Equal(FindingLevel.Error, phonetic.Level);
    }

    [Fact]
    public async Task TheRealBatchesKnownViolation_SurvivesImportAndIsReported()
    {
        // The same rule, proved against the real data rather than a fixture: the offending club
        // is in the database after import, and the invariant sweep flags it.
        await using WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity? remolar = await unitOfWork.Clubs.GetAsync("clb_bra_bel_001");
        WorldCalibration calibration = (await unitOfWork.Calibration.GetAsync())!;

        Assert.NotNull(remolar);
        Assert.Equal(NamingRule.Phonetic, remolar!.World.NamingRule);

        Finding phonetic = ClubInvariants
            .Check(remolar, calibration)
            .Single(finding => finding.Code == "PHONETIC_WINDOW");
        Assert.Equal(FindingLevel.Error, phonetic.Level);
    }
}
