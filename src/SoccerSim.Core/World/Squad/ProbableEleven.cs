using SoccerSim.Core.World.Generation;

namespace SoccerSim.Core.World.Squad;

/// <summary>Why a player is in this slot — and the whole point of the screen is the last two.</summary>
public enum SlotFit
{
    /// <summary>It is their primary position.</summary>
    Natural,

    /// <summary>They list it as a secondary position: out of their best role, but they play it.</summary>
    Secondary,

    /// <summary>Nobody in the squad plays this position and somebody had to. This is the finding.</summary>
    Improvised,
}

/// <summary>One slot of the drawn eleven.</summary>
public sealed record ElevenSlot(
    Position Position,
    /// <summary>Which of the four drawn lines this sits on (SquadShape.LineOf).</summary>
    int Line,
    /// <summary>Null when the squad has nobody left to put here at all.</summary>
    CharacterRecord? Player,
    SlotFit Fit);

/// <summary>The eleven a club would field, and what it cost to field it.</summary>
public sealed record ProbableElevenResult(
    Formation Formation,
    string FormationLabel,
    IReadOnlyList<ElevenSlot> Slots,
    /// <summary>Mean overall of the filled slots, rounded. Empty slots are not counted as zero —
    /// they are counted as missing, which is what <see cref="Unfilled"/> says.</summary>
    int Overall,
    int Secondary,
    int Improvised,
    int Unfilled);

/// <summary>
/// The eleven a club would actually field, derived from the squad it actually has
/// (ROADMAP.md Sprint 9). The club page shows a squad list and a set of averages; neither
/// answers the question an author is really asking — can this club put a team on the pitch?
///
/// <para>The answer is interesting exactly when it is bad. A club whose only left-back is a
/// centre-back playing out of position has a hole that no average reveals, and the
/// <see cref="SlotFit.Improvised"/> mark is the whole reason this screen exists.</para>
///
/// <para>Derived, never stored: it is a function of the squad and the tactical style, and both are
/// edited on the same page. A stored XI would be wrong one edit later.</para>
/// </summary>
public static class ProbableEleven
{
    /// <summary>The order slots are offered players in. Scarcest and most specialised first: a
    /// keeper cannot be improvised from an attacker in any useful sense, and a centre-back is
    /// harder to fake than a midfielder.</summary>
    private static readonly Position[] FillOrder =
    [
        Position.GK, Position.CB, Position.FB, Position.DM,
        Position.CM, Position.AM, Position.WG, Position.ST,
    ];

    public static ProbableElevenResult For(ClubIdentity club, IReadOnlyList<CharacterRecord> squad) =>
        For(SquadShape.DefaultFormationFor(club.AiProfile.DefaultTacticalStyle), squad);

    public static ProbableElevenResult For(Formation formation, IReadOnlyList<CharacterRecord> squad)
    {
        List<ElevenSlot> slots = BuildSlots(formation);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Three passes rather than one greedy sweep. A single pass in slot order lets an early
        // slot take the one player a later slot could have filled naturally, and then reports an
        // improvisation the squad did not actually have.
        Fill(slots, squad, taken, SlotFit.Natural,
            (player, position) => player.PrimaryPosition == position);

        Fill(slots, squad, taken, SlotFit.Secondary,
            (player, position) => player.SecondaryPositions.Contains(position));

        Fill(slots, squad, taken, SlotFit.Improvised, (_, _) => true);

        List<CharacterRecord> fielded = [.. slots.Select(slot => slot.Player).OfType<CharacterRecord>()];

        return new ProbableElevenResult(
            formation,
            SquadShape.Label(formation),
            slots,
            fielded.Count == 0
                ? 0
                : (int)Math.Round(fielded.Average(player => player.Overall), MidpointRounding.AwayFromZero),
            slots.Count(slot => slot.Fit == SlotFit.Secondary && slot.Player is not null),
            slots.Count(slot => slot.Fit == SlotFit.Improvised && slot.Player is not null),
            slots.Count(slot => slot.Player is null));
    }

    private static List<ElevenSlot> BuildSlots(Formation formation)
    {
        IReadOnlyDictionary<Position, int> starters = SquadShape.Starters(formation);
        var slots = new List<ElevenSlot>();

        foreach (Position position in FillOrder)
        {
            if (!starters.TryGetValue(position, out int count))
                continue;

            for (int i = 0; i < count; i++)
                slots.Add(new ElevenSlot(position, SquadShape.LineOf(position), null, SlotFit.Improvised));
        }

        return slots;
    }

    /// <summary>
    /// One pass over the empty slots, handing each the best unused player the test accepts. In
    /// slot order, which is <see cref="FillOrder"/>: when two slots want the same player, the
    /// scarcer position gets them.
    /// </summary>
    private static void Fill(
        List<ElevenSlot> slots,
        IReadOnlyList<CharacterRecord> squad,
        HashSet<string> taken,
        SlotFit fit,
        Func<CharacterRecord, Position, bool> eligible)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Player is not null)
                continue;

            CharacterRecord? best = squad
                .Where(player => !taken.Contains(player.PlayerId) && eligible(player, slots[i].Position))
                // The shirt number breaks ties, so the same squad always draws the same eleven.
                .OrderByDescending(player => player.Overall)
                .ThenBy(player => player.ShirtNumber)
                .FirstOrDefault();

            if (best is null)
                continue;

            taken.Add(best.PlayerId);
            slots[i] = slots[i] with { Player = best, Fit = fit };
        }
    }
}
