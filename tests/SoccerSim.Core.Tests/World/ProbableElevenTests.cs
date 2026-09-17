using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Squad;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 9: the eleven a club would field, derived from the squad it has, marking
/// whoever is out of position. Interesting exactly when it is bad — which over the real batch is
/// never, and that is itself the assertion worth making.
/// </summary>
public sealed class ProbableElevenTests
{
    private static readonly Lazy<WorldSnapshot> Source =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static WorldSnapshot World => Source.Value;

    private static IReadOnlyList<CharacterRecord> SquadOf(string clubId) =>
        [.. World.Characters.Where(player => player.ClubId == clubId)];

    private static ClubIdentity ClubOf(string clubId) =>
        World.Clubs.Single(club => club.ClubId == clubId);

    // ----------------------------------------------------------- the real batch

    /// <summary>
    /// The golden. Every one of the twenty squads fields eleven players in their own primary
    /// position — no secondary, no improvisation, no empty slot. An authored batch that could not
    /// do this would be one nobody can play, and the tool would have had no way to say so.
    /// </summary>
    [Fact]
    public void EveryClubOfTheRealBatch_FieldsAFullyNaturalEleven()
    {
        foreach (ClubIdentity club in World.Clubs)
        {
            ProbableElevenResult eleven = ProbableEleven.For(club, SquadOf(club.ClubId));

            Assert.Equal(11, eleven.Slots.Count);
            Assert.Equal(0, eleven.Unfilled);
            Assert.Equal(0, eleven.Secondary);
            Assert.Equal(0, eleven.Improvised);
            Assert.All(eleven.Slots, slot => Assert.Equal(SlotFit.Natural, slot.Fit));
        }
    }

    /// <summary>Nobody plays twice, which is the failure a greedy fill makes silently.</summary>
    [Fact]
    public void NoPlayerTakesTwoSlots()
    {
        foreach (ClubIdentity club in World.Clubs)
        {
            ProbableElevenResult eleven = ProbableEleven.For(club, SquadOf(club.ClubId));
            string[] ids = [.. eleven.Slots.Select(slot => slot.Player!.PlayerId)];

            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
    }

    /// <summary>
    /// A cross-check against a number derived somewhere else entirely: the least-squares fit of
    /// ALGORITHMS.md §6.4, <c>XI ≈ 39.6 + 41.35 × clubStrength</c>. Measured over the batch the
    /// worst club is 5 off and the mean is −1.3, so six is the band. If the selection ever starts
    /// picking badly, this is what notices.
    /// </summary>
    [Fact]
    public void TheDrawnElevenTracksTheOverallTheClubStrengthImplies()
    {
        foreach (ClubIdentity club in World.Clubs)
        {
            ProbableElevenResult eleven = ProbableEleven.For(club, SquadOf(club.ClubId));
            int expected = SquadShape.TargetOverallFor(club.World.ClubStrength);

            Assert.True(
                Math.Abs(eleven.Overall - expected) <= 6,
                $"{club.Identity.ShortName}: XI {eleven.Overall}, fit {expected}.");
        }
    }

    [Fact]
    public void TheFormationComesFromTheTacticalStyle()
    {
        ClubIdentity club = ClubOf("clb_bra_rio_001");

        Assert.Equal(
            SquadShape.DefaultFormationFor(club.AiProfile.DefaultTacticalStyle),
            ProbableEleven.For(club, SquadOf(club.ClubId)).Formation);
    }

    [Fact]
    public void TheSameSquadAlwaysDrawsTheSameEleven()
    {
        IReadOnlyList<CharacterRecord> squad = SquadOf("clb_bra_rio_001");
        ClubIdentity club = ClubOf("clb_bra_rio_001");

        Assert.Equal(
            ProbableEleven.For(club, squad).Slots.Select(slot => slot.Player!.PlayerId),
            // Reversed, because a selection that depends on the order the rows arrive in is a
            // selection that changes when a query's ORDER BY does.
            ProbableEleven.For(club, [.. squad.Reverse()]).Slots.Select(slot => slot.Player!.PlayerId));
    }

    // ---------------------------------------------------- the squads that cannot

    /// <summary>The reason the screen exists: the hole no average reveals.</summary>
    [Fact]
    public void AClubWithNoKeeper_ImprovisesOne_AndSaysSo()
    {
        IReadOnlyList<CharacterRecord> withoutKeepers =
            [.. SquadOf("clb_bra_rio_001").Where(player => player.PrimaryPosition != Position.GK)];

        ProbableElevenResult eleven = ProbableEleven.For(Formation.F442, withoutKeepers);
        ElevenSlot keeper = eleven.Slots.Single(slot => slot.Position == Position.GK);

        Assert.Equal(SlotFit.Improvised, keeper.Fit);
        Assert.NotNull(keeper.Player);
        Assert.NotEqual(Position.GK, keeper.Player!.PrimaryPosition);
        Assert.Equal(1, eleven.Improvised);
        Assert.Equal(0, eleven.Unfilled);
    }

    /// <summary>
    /// A player who lists the position is not improvising — they are out of their best role, which
    /// is a different and milder thing, and the screen distinguishes them.
    /// </summary>
    [Fact]
    public void APlayerWhoseSecondaryCoversTheSlot_IsMarkedSecondaryRatherThanImprovised()
    {
        // Cleared first: half this squad already lists DM as a secondary, so without that the
        // test would only be asserting which of several equally eligible players came out on top.
        List<CharacterRecord> squad =
        [
            .. SquadOf("clb_bra_rio_001")
                .Where(player => player.PrimaryPosition != Position.DM)
                .Select(player => player with { SecondaryPositions = [] }),
        ];

        // The WEAKEST centre-mid, not the first one to hand: 4-3-3 starts two of them, and a top-two
        // midfielder is taken by his own slot in the natural pass before the holding slot is ever
        // offered him. The player who covers a hole is the one nobody else wanted.
        CharacterRecord cover = squad
            .Where(player => player.PrimaryPosition == Position.CM)
            .OrderBy(player => player.Overall)
            .First();

        int index = squad.FindIndex(player => player.PlayerId == cover.PlayerId);
        squad[index] = cover with { SecondaryPositions = [Position.DM] };

        ProbableElevenResult eleven = ProbableEleven.For(Formation.F433, squad);
        ElevenSlot holding = eleven.Slots.Single(slot => slot.Position == Position.DM);

        Assert.Equal(SlotFit.Secondary, holding.Fit);
        Assert.Equal(squad[index].PlayerId, holding.Player!.PlayerId);
        Assert.Equal(1, eleven.Secondary);
        Assert.Equal(0, eleven.Improvised);
    }

    /// <summary>
    /// Why the fill is three passes and not one sweep. A single pass in slot order gives the
    /// centre-back slots the only player who can also play full-back, and then reports an
    /// improvisation at full-back that the squad did not actually have.
    /// </summary>
    [Fact]
    public void AVersatileDefenderIsNotSpentOnASlotThatHadANaturalAnyway()
    {
        var squad = new List<CharacterRecord>();
        int shirt = 1;

        CharacterRecord Player(Position primary, int overall, IReadOnlyList<Position>? also = null) =>
            WorldSamples.Character($"plr_{shirt:D3}", "clb_x") with
            {
                ShirtNumber = shirt++,
                PrimaryPosition = primary,
                SecondaryPositions = also ?? [],
                Overall = overall,
            };

        // Two centre-backs, one of whom is the best player in the squad and also plays full-back.
        squad.Add(Player(Position.CB, 90, [Position.FB]));
        squad.Add(Player(Position.CB, 60));
        squad.Add(Player(Position.FB, 70));
        squad.Add(Player(Position.FB, 70));

        foreach (Position position in new[] { Position.GK, Position.CM, Position.CM, Position.WG,
                                              Position.WG, Position.ST, Position.ST })
        {
            squad.Add(Player(position, 70));
        }

        ProbableElevenResult eleven = ProbableEleven.For(Formation.F442, squad);

        // The versatile one plays centre-back, where he is natural, and both full-back slots are
        // filled by their own naturals. Nothing is improvised.
        Assert.Equal(0, eleven.Improvised);
        Assert.Equal(0, eleven.Secondary);
        Assert.All(eleven.Slots, slot => Assert.Equal(SlotFit.Natural, slot.Fit));
    }

    /// <summary>A squad too small to field eleven leaves slots empty rather than inventing a
    /// player, and the count says how many.</summary>
    [Fact]
    public void ASquadSmallerThanAnEleven_LeavesTheRestEmpty()
    {
        IReadOnlyList<CharacterRecord> seven = [.. SquadOf("clb_bra_rio_001").Take(7)];

        ProbableElevenResult eleven = ProbableEleven.For(Formation.F433, seven);

        Assert.Equal(11, eleven.Slots.Count);
        Assert.Equal(4, eleven.Unfilled);
        Assert.Equal(7, eleven.Slots.Count(slot => slot.Player is not null));
    }

    [Fact]
    public void AnEmptySquad_DrawsElevenEmptySlotsAndNoOverall()
    {
        ProbableElevenResult eleven = ProbableEleven.For(Formation.F433, []);

        Assert.Equal(11, eleven.Unfilled);
        Assert.Equal(0, eleven.Overall);
    }

    /// <summary>Every formation draws exactly eleven, and the lines the screen stacks them into
    /// always come to a keeper plus ten.</summary>
    [Theory]
    [InlineData(Formation.F433)]
    [InlineData(Formation.F4231)]
    [InlineData(Formation.F442)]
    [InlineData(Formation.F352)]
    public void EveryFormationDrawsElevenAcrossFourLines(Formation formation)
    {
        ProbableElevenResult eleven = ProbableEleven.For(formation, SquadOf("clb_bra_rio_001"));

        Assert.Equal(11, eleven.Slots.Count);
        Assert.Equal(1, eleven.Slots.Count(slot => slot.Line == 0));
        Assert.Equal([0, 1, 2, 3], eleven.Slots.Select(slot => slot.Line).Distinct().Order());
    }
}
