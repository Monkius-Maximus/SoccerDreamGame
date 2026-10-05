using SoccerSim.Core.Domain;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Covers the off-pitch life simulation: role-tuned decay, the cross-effect graph, the derived
/// snapshot every other system reads, and the activity catalogue's role gating.
/// </summary>
public sealed class LifeSimTests
{
    private static readonly DateTime Day = new(2026, 8, 1);

    /// <summary>A roll of exactly 0.5 sits at the midpoint of the variance band, so decay is nominal.</summary>
    private static StubRandom NoVariance => new(0.5);

    private static WellbeingState StateWith(CareerRole role, params (NeedKind Need, double Value)[] overrides)
    {
        WellbeingState state = WellbeingState.CreateDefault(role);
        foreach ((NeedKind need, double value) in overrides)
            state.Set(need, value);
        return state;
    }

    // ── Profiles ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CareerRole.Player)]
    [InlineData(CareerRole.Manager)]
    public void NeedProfile_WeightsSumToOne(CareerRole role)
    {
        NeedProfile profile = NeedProfile.For(role);

        double total = Needs.All.Sum(need => profile[need].Weight);

        // Both profiles normalising to 1.0 is what makes a wellbeing index of 60 mean the same
        // thing in a player career and a manager career.
        Assert.Equal(1.0, total, precision: 9);
    }

    [Fact]
    public void NeedProfile_PlayerAndManager_WeightDifferentAxes()
    {
        // The whole point of one system with two profiles: an athlete's life revolves around
        // conditioning, a manager's around clarity and the dressing room.
        Assert.True(NeedProfile.Player[NeedKind.Fitness].Weight > NeedProfile.Manager[NeedKind.Fitness].Weight);
        Assert.True(NeedProfile.Manager[NeedKind.Focus].Weight > NeedProfile.Player[NeedKind.Focus].Weight);
        Assert.True(NeedProfile.Manager[NeedKind.Social].Weight > NeedProfile.Player[NeedKind.Social].Weight);
    }

    // ── Daily advance ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AdvanceDay_WithoutVariance_AppliesExactProfileDecay()
    {
        WellbeingState state = WellbeingState.CreateDefault(CareerRole.Player);

        new LifeSimulator().AdvanceDay(state, Day, NoVariance);

        Assert.Equal(58.0, state[NeedKind.Energy], precision: 9);     // 70 − 12
        Assert.Equal(60.0, state[NeedKind.Nutrition], precision: 9);  // 70 − 10
        Assert.Equal(64.0, state[NeedKind.Fitness], precision: 9);    // 70 − 6
        Assert.Equal(66.0, state[NeedKind.Morale], precision: 9);     // 70 − 4
        Assert.Equal(63.0, state[NeedKind.Social], precision: 9);     // 70 − 7
        Assert.Equal(62.0, state[NeedKind.Focus], precision: 9);      // 70 − 8
    }

    [Fact]
    public void AdvanceDay_SameNeeds_DrainDifferentlyPerRole()
    {
        WellbeingState player = WellbeingState.CreateDefault(CareerRole.Player);
        WellbeingState manager = WellbeingState.CreateDefault(CareerRole.Manager);
        var simulator = new LifeSimulator();

        simulator.AdvanceDay(player, Day, NoVariance);
        simulator.AdvanceDay(manager, Day, NoVariance);

        // A player loses conditioning faster; a manager loses clarity and rest faster.
        Assert.True(player[NeedKind.Fitness] < manager[NeedKind.Fitness]);
        Assert.True(manager[NeedKind.Focus] < player[NeedKind.Focus]);
        Assert.True(manager[NeedKind.Energy] < player[NeedKind.Energy]);
    }

    [Fact]
    public void AdvanceDay_CriticalNutrition_DragsFitnessDown()
    {
        WellbeingState state = StateWith(CareerRole.Player, (NeedKind.Nutrition, 10.0));

        new LifeSimulator().AdvanceDay(state, Day, NoVariance);

        // 70 − 6 (own decay) − 6 × 1.5 (cross-effect from a bottomed-out Nutrition) = 55.
        Assert.Equal(55.0, state[NeedKind.Fitness], precision: 9);
    }

    [Fact]
    public void AdvanceDay_CrossEffect_JudgesTheBandAtDayStart_NotMidDecay()
    {
        // Nutrition starts at 21 (Low, not Critical) and only falls below 20 through this day's own
        // decay. The cross-effect must NOT fire, otherwise the result would depend on the order the
        // needs happen to be iterated in — and the save would stop replaying from its seed.
        WellbeingState state = StateWith(CareerRole.Player, (NeedKind.Nutrition, 21.0));

        new LifeSimulator().AdvanceDay(state, Day, NoVariance);

        Assert.Equal(11.0, state[NeedKind.Nutrition], precision: 9); // did cross into Critical
        Assert.Equal(64.0, state[NeedKind.Fitness], precision: 9);   // but Fitness took own decay only
    }

    [Fact]
    public void AdvanceDay_ClampsAtZero()
    {
        WellbeingState state = StateWith(CareerRole.Player, (NeedKind.Energy, 3.0));

        new LifeSimulator().AdvanceDay(state, Day, NoVariance);

        Assert.Equal(0.0, state[NeedKind.Energy]);
    }

    [Fact]
    public void AdvanceDay_ReturnsAlertsForDegradedNeedsOnly()
    {
        WellbeingState state = StateWith(CareerRole.Player,
            (NeedKind.Energy, 15.0),    // -> Critical
            (NeedKind.Social, 45.0));   // -> Low

        IReadOnlyList<NeedAlert> alerts = new LifeSimulator().AdvanceDay(state, Day, NoVariance);

        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Need == NeedKind.Energy && a.Band == NeedBand.Critical);
        Assert.Contains(alerts, a => a.Need == NeedKind.Social && a.Band == NeedBand.Low);
        Assert.All(alerts, a => Assert.Equal(Day, a.Date));
    }

    [Fact]
    public void AdvanceDay_WithSameSeed_ReplaysIdentically()
    {
        WellbeingState first = WellbeingState.CreateDefault(CareerRole.Player);
        WellbeingState second = WellbeingState.CreateDefault(CareerRole.Player);
        var simulator = new LifeSimulator();
        IDeterministicRandom rngA = DeterministicRng.Create(0xC0FFEE);
        IDeterministicRandom rngB = DeterministicRng.Create(0xC0FFEE);

        for (int day = 0; day < 10; day++)
        {
            simulator.AdvanceDay(first, Day.AddDays(day), rngA);
            simulator.AdvanceDay(second, Day.AddDays(day), rngB);
        }

        foreach (NeedKind need in Needs.All)
            Assert.Equal(first[need], second[need], precision: 12);
    }

    [Fact]
    public void AdvanceDay_VarianceStaysWithinBand()
    {
        WellbeingState state = WellbeingState.CreateDefault(CareerRole.Player);

        // A maximal roll (1.0) yields the widest possible decay: nominal × (1 + band).
        new LifeSimulator().AdvanceDay(state, Day, new StubRandom(1.0));

        double expected = 70.0 - 12.0 * (1.0 + LifeSimulator.DailyVarianceBand);
        Assert.Equal(expected, state[NeedKind.Energy], precision: 9);
    }

    // ── Snapshot: the derived read model ────────────────────────────────────────────────

    [Fact]
    public void Snapshot_Index_IsTheWeightedAverage()
    {
        WellbeingSnapshot snapshot = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, 70.0));

        Assert.Equal(70.0, snapshot.Index, precision: 9);
    }

    [Theory]
    [InlineData(100.0, 5)]
    [InlineData(70.0, 2)]
    [InlineData(50.0, 0)]
    [InlineData(30.0, -2)]
    [InlineData(0.0, -5)]
    public void Snapshot_FormModifier_MapsIndexIntoFormMoodRange(double allNeeds, int expected)
    {
        WellbeingSnapshot snapshot =
            WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, allNeeds));

        Assert.Equal(expected, snapshot.FormModifier);
    }

    [Fact]
    public void Snapshot_EventProbabilityMultiplier_IsNeutralAtTheAnchorAndRisesAsWellbeingFalls()
    {
        double thriving = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, 100.0))
            .EventProbabilityMultiplier;
        double neutral = WellbeingSnapshot.From(
                WellbeingState.CreateDefault(CareerRole.Player, WellbeingSnapshot.NeutralEventPressureIndex))
            .EventProbabilityMultiplier;
        double falling = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, 0.0))
            .EventProbabilityMultiplier;

        Assert.Equal(WellbeingSnapshot.MinEventMultiplier, thriving, precision: 9);
        Assert.Equal(1.0, neutral, precision: 9);
        Assert.Equal(WellbeingSnapshot.MaxEventMultiplier, falling, precision: 9);
    }

    [Fact]
    public void Snapshot_InjuryRisk_RisesAsConditioningAndRestFall()
    {
        double healthy = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, 100.0)).InjuryRisk;
        double wrecked = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Player, 0.0)).InjuryRisk;

        Assert.True(healthy < 0.05);
        Assert.True(wrecked > 0.7);
        Assert.True(wrecked > healthy);
    }

    [Fact]
    public void Snapshot_ManagerFields_TrackClarityRestAndMood()
    {
        WellbeingState sharp = StateWith(CareerRole.Manager,
            (NeedKind.Focus, 90.0), (NeedKind.Energy, 90.0), (NeedKind.Morale, 90.0));
        WellbeingState frazzled = StateWith(CareerRole.Manager,
            (NeedKind.Focus, 10.0), (NeedKind.Energy, 10.0), (NeedKind.Morale, 10.0));

        WellbeingSnapshot sharpSnapshot = WellbeingSnapshot.From(sharp);
        WellbeingSnapshot frazzledSnapshot = WellbeingSnapshot.From(frazzled);

        Assert.True(sharpSnapshot.DecisionQuality > frazzledSnapshot.DecisionQuality);
        Assert.True(frazzledSnapshot.BurnoutRisk > sharpSnapshot.BurnoutRisk);
    }

    [Fact]
    public void Snapshot_ListsCriticalNeeds()
    {
        WellbeingState state = StateWith(CareerRole.Player,
            (NeedKind.Energy, 5.0), (NeedKind.Focus, 12.0));

        WellbeingSnapshot snapshot = WellbeingSnapshot.From(state);

        Assert.True(snapshot.HasCriticalNeed);
        Assert.Equal(new[] { NeedKind.Energy, NeedKind.Focus }, snapshot.CriticalNeeds);
    }

    [Fact]
    public void Snapshot_HealthyState_HasNoCriticalNeeds()
    {
        WellbeingSnapshot snapshot = WellbeingSnapshot.From(WellbeingState.CreateDefault(CareerRole.Manager));

        Assert.False(snapshot.HasCriticalNeed);
        Assert.Empty(snapshot.CriticalNeeds);
    }

    // ── Activities ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Perform_AppliesDeltas_AndReportsTheClampedAmount()
    {
        WellbeingState state = StateWith(CareerRole.Player, (NeedKind.Energy, 70.0));

        ActivityOutcome outcome = new LifeSimulator().Perform(state, LifeActivityCatalogue.ByKey("sleep"));

        // Sleep nominally grants +45 Energy but the gauge ceilings at 100, so only +30 landed.
        Assert.Equal(100.0, state[NeedKind.Energy], precision: 9);
        Assert.Equal(30.0, outcome.AppliedDeltas.Single(d => d.Need == NeedKind.Energy).Delta, precision: 9);
    }

    [Fact]
    public void Perform_RoleExclusiveActivity_ThrowsForTheWrongRole()
    {
        WellbeingState manager = WellbeingState.CreateDefault(CareerRole.Manager);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new LifeSimulator().Perform(manager, LifeActivityCatalogue.ByKey("gym_session")));

        Assert.Contains("gym_session", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Perform_TradesOneNeedAgainstAnother()
    {
        WellbeingState state = WellbeingState.CreateDefault(CareerRole.Player);

        new LifeSimulator().Perform(state, LifeActivityCatalogue.ByKey("individual_training"));

        // No activity is purely positive — training buys conditioning with rest and nutrition.
        Assert.True(state[NeedKind.Fitness] > 70.0);
        Assert.True(state[NeedKind.Energy] < 70.0);
        Assert.True(state[NeedKind.Nutrition] < 70.0);
    }

    [Fact]
    public void Catalogue_ForRole_ReturnsSharedSpinePlusThatRolesLeavesOnly()
    {
        IReadOnlyList<string> player = LifeActivityCatalogue.For(CareerRole.Player).Select(a => a.Key).ToArray();
        IReadOnlyList<string> manager = LifeActivityCatalogue.For(CareerRole.Manager).Select(a => a.Key).ToArray();

        Assert.Contains("sleep", player);           // shared spine
        Assert.Contains("sleep", manager);
        Assert.Contains("gym_session", player);     // player leaf
        Assert.DoesNotContain("gym_session", manager);
        Assert.Contains("film_study", manager);     // manager leaf
        Assert.DoesNotContain("film_study", player);
    }

    [Fact]
    public void Catalogue_UnknownKey_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => LifeActivityCatalogue.ByKey("teleport"));

    // ── State validation ────────────────────────────────────────────────────────────────

    [Fact]
    public void FromValues_MissingNeed_Throws()
    {
        var partial = new Dictionary<NeedKind, double> { [NeedKind.Energy] = 50.0 };

        Assert.Throws<InvalidOperationException>(() => WellbeingState.FromValues(CareerRole.Player, partial));
    }

    [Fact]
    public void FromValues_RoundTripsEveryNeed()
    {
        WellbeingState original = StateWith(CareerRole.Manager, (NeedKind.Focus, 33.5));

        WellbeingState restored = WellbeingState.FromValues(CareerRole.Manager, original.ToDictionary());

        foreach (NeedKind need in Needs.All)
            Assert.Equal(original[need], restored[need], precision: 12);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        WellbeingState original = WellbeingState.CreateDefault(CareerRole.Player);
        WellbeingState copy = original.Clone();

        copy.Set(NeedKind.Morale, 5.0);

        Assert.Equal(70.0, original[NeedKind.Morale], precision: 9);
    }
}
