using SoccerSim.Core.World;
using SoccerSim.Core.World.Color;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Derived fields are recomputed on every write, and a value supplied by the caller is discarded.
/// This is what stops the tool — which is the source of truth — from serving numbers that no
/// longer follow from the data they were derived from (ROADMAP.md Sprint 2).
/// </summary>
public sealed class DerivedFieldsTests
{
    [Fact]
    public async Task ChangingTheKit_RecalculatesDeltaE()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        ClubIdentity club = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;
        double before = club.Kits.DeltaE;

        // Red home vs white away (ΔE ≈ 104.6) becomes red home vs near-identical red away.
        await unitOfWork.Clubs.UpdateAsync(club with
        {
            Kits = club.Kits with { Away = club.Kits.Away with { Shirt = "#D40909" } },
        });

        ClubIdentity updated = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;

        Assert.Equal(104.6, before, precision: 1);
        Assert.Equal(
            Math.Round(ColorMath.DeltaE76("#D50A0A", "#D40909"), 1),
            updated.Kits.DeltaE,
            precision: 1);
        Assert.True(updated.Kits.DeltaE < 5, "near-identical shirts must collapse ΔE");
    }

    [Fact]
    public async Task ChangingTheHomeShirt_RecalculatesLuminanceAndPolarityRule()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        ClubIdentity club = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;
        Assert.Equal(WorldDerivations.PolarityDarkHome, club.Kits.PolarityRule);

        // Dark red home shirt becomes white: luminance crosses the 0.35 threshold and the
        // polarity rule flips with it.
        await unitOfWork.Clubs.UpdateAsync(club with
        {
            Kits = club.Kits with { Home = club.Kits.Home with { Shirt = "#FFFFFF" } },
        });

        ClubIdentity updated = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;

        Assert.Equal(1.0, updated.Kits.Home.Luminance, precision: 4);
        Assert.Equal(WorldDerivations.PolarityLightHome, updated.Kits.PolarityRule);
    }

    [Fact]
    public async Task ChangingTheAtmosphere_RewritesHomeAdvantageFromCalibration()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        ClubIdentity club = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;
        Assert.Equal(0.17, club.AiProfile.HomeAdvantageModifier, precision: 4);   // Cauldron

        await unitOfWork.Clubs.UpdateAsync(club with
        {
            Stadium = club.Stadium with { AtmosphereArchetype = AtmosphereArchetype.Apathetic },
        });

        ClubIdentity updated = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;

        Assert.Equal(0, updated.AiProfile.HomeAdvantageModifier, precision: 4);   // Apathetic
    }

    [Fact]
    public async Task DerivedValuesSuppliedByTheCaller_AreDiscarded()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();

        ClubIdentity club = WorldSamples.Club();

        // Every derived field is handed in wrong on purpose.
        await unitOfWork.Clubs.AddAsync(club with
        {
            Kits = club.Kits with
            {
                DeltaE = 1.0,
                PolarityRule = "whatever the client says",
                Home = club.Kits.Home with { Luminance = 0.99 },
            },
            AiProfile = club.AiProfile with { HomeAdvantageModifier = 99 },
            Crest = club.Crest with { Colors = ["#111111", "#222222", "#333333"] },
        });

        ClubIdentity stored = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;

        Assert.Equal(104.6, stored.Kits.DeltaE, precision: 1);
        Assert.Equal(WorldDerivations.PolarityDarkHome, stored.Kits.PolarityRule);
        Assert.Equal(0.1439, stored.Kits.Home.Luminance, precision: 4);
        Assert.Equal(0.17, stored.AiProfile.HomeAdvantageModifier, precision: 4);
        Assert.Equal(new[] { "#D50A0A", "#000000", "#FFFFFF" }, stored.Crest.Colors);
    }

    [Fact]
    public async Task ChangingAnAttribute_RecalculatesOverallValueAndSalary()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());
        await unitOfWork.Characters.AddAsync(WorldSamples.Character(attributeValue: 70));

        CharacterRecord before = (await unitOfWork.Characters.GetAsync("plr_test_0001"))!;

        await unitOfWork.Characters.UpdateAsync(before with { Attrs = WorldSamples.Attributes(85) });

        CharacterRecord after = (await unitOfWork.Characters.GetAsync("plr_test_0001"))!;

        Assert.Equal(70, before.Overall);          // weights sum to 1, so a flat 70 gives OVR 70
        Assert.Equal(85, after.Overall);
        Assert.True(after.MarketValueEur > before.MarketValueEur, "a better player must be worth more");
        Assert.True(after.SalaryMonthlyBrl > before.SalaryMonthlyBrl, "salary follows value");
    }

    [Fact]
    public async Task CharacterDerivedValuesSuppliedByTheCaller_AreDiscarded()
    {
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        CharacterRecord character = WorldSamples.Character(attributeValue: 70);

        await unitOfWork.Characters.AddAsync(character with
        {
            ShirtName = "not-the-surname",
            Overall = 99,
            PotentialOverall = 99,
            MarketValueEur = 1,
            SalaryMonthlyBrl = 1,
        });

        CharacterRecord stored = (await unitOfWork.Characters.GetAsync("plr_test_0001"))!;

        Assert.Equal("PLAYER", stored.ShirtName);
        Assert.Equal(70, stored.Overall);
        Assert.Equal(74, stored.PotentialOverall);          // overall + potentialGap (4)
        Assert.True(stored.MarketValueEur > 1, "market value must be recomputed, not taken from the caller");
        Assert.True(stored.SalaryMonthlyBrl > 1, "salary must be recomputed, not taken from the caller");
    }

    [Fact]
    public async Task RewritingCalibration_ChangesWhatLaterWritesDerive()
    {
        // The calibration cache must not outlive a re-fit: a club written after the change has to
        // pick up the new numbers, not the ones loaded before it.
        await using WorldDatabase database = await WorldDatabase.ReadyForClubs();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Clubs.AddAsync(WorldSamples.Club());

        ClubIdentity before = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;
        Assert.Equal(0.17, before.AiProfile.HomeAdvantageModifier, precision: 4);

        WorldCalibration calibration = WorldFixture.BuildCalibration();
        var refitted = new Dictionary<AtmosphereArchetype, double>(calibration.HomeAdv)
        {
            [AtmosphereArchetype.Cauldron] = 0.25,
        };
        await unitOfWork.Calibration.SaveAsync(calibration with { HomeAdv = refitted });

        await unitOfWork.Clubs.UpdateAsync(before);
        ClubIdentity after = (await unitOfWork.Clubs.GetAsync("clb_test_001"))!;

        Assert.Equal(0.25, after.AiProfile.HomeAdvantageModifier, precision: 4);
    }
}
