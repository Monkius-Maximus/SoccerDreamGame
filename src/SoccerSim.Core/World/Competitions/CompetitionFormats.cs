namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// How a competition is actually played. The old <c>Competition.Format</c> is authored prose
/// ("Pontos corridos, turno e returno"); this is the closed vocabulary the tool computes from.
/// </summary>
public enum CompetitionFormat
{
    /// <summary>Single round robin — everyone plays everyone once.</summary>
    LeagueSingle,

    /// <summary>Double round robin — home and away. What the pilot league plays.</summary>
    LeagueDouble,

    /// <summary>Groups of four, single round robin, then a knockout among the top two.</summary>
    GroupsKnockout,

    /// <summary>One-legged bracket, byes where the field is not a power of two.</summary>
    KnockoutOnly,

    /// <summary>Two-legged ties except the final — the shape a national cup is played in.</summary>
    NationalCup,
}

/// <summary>What a format produces for a given field size.</summary>
public sealed record CompetitionShape(int Rounds, int Matches);

/// <summary>
/// Rounds and matches are DERIVED from the format and the number of clubs, never typed
/// (ROADMAP.md Sprint 9). A typed round count is a number with no source, and the whole project
/// runs on the opposite rule.
///
/// <para>The pilot league is the proof: 20 clubs playing <see cref="CompetitionFormat.LeagueDouble"/>
/// derive to 38 rounds and 380 matches, which is exactly what the authored data says and exactly
/// what the real Brasileirão plays.</para>
/// </summary>
public static class CompetitionFormats
{
    /// <summary>Groups are of four. Smaller groups do not produce a meaningful table; larger ones
    /// are a league with extra steps.</summary>
    public const int GroupSize = 4;

    /// <summary>How many qualify from each group.</summary>
    public const int QualifiersPerGroup = 2;

    /// <summary>
    /// Why this field size cannot be played in this format, or null when it can. Stated once, here,
    /// so that the value, the exception and the audit finding all say the same thing: an
    /// authoring tool has to explain a refusal, and a rule written twice drifts.
    /// </summary>
    public static string? Unplayable(CompetitionFormat format, int clubs)
    {
        if (clubs < 2)
            return $"uma competição precisa de ao menos dois clubes, e esta tem {clubs}";

        if (format == CompetitionFormat.GroupsKnockout && clubs % GroupSize != 0)
            return $"grupos de {GroupSize} não dividem {clubs} clubes";

        return null;
    }

    public static CompetitionShape Shape(CompetitionFormat format, int clubs)
    {
        if (Unplayable(format, clubs) is { } reason)
            throw new ArgumentOutOfRangeException(nameof(clubs), clubs, reason);

        return format switch
        {
            CompetitionFormat.LeagueSingle => new CompetitionShape(RoundRobinRounds(clubs), clubs * (clubs - 1) / 2),
            CompetitionFormat.LeagueDouble => new CompetitionShape(RoundRobinRounds(clubs) * 2, clubs * (clubs - 1)),
            CompetitionFormat.GroupsKnockout => GroupsKnockout(clubs),
            CompetitionFormat.KnockoutOnly => new CompetitionShape(BracketRounds(clubs), clubs - 1),
            // Every tie over two legs except the final, which is one match.
            CompetitionFormat.NationalCup => new CompetitionShape(
                (BracketRounds(clubs) * 2) - 1,
                ((clubs - 1) * 2) - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown competition format"),
        };
    }

    /// <summary>
    /// A round robin over an EVEN field takes n−1 rounds; over an odd field it takes n, because
    /// one club sits out each round. Getting this wrong is how a fixture list ends up one round
    /// short and nobody notices until the last weekend.
    /// </summary>
    public static int RoundRobinRounds(int clubs) => clubs % 2 == 0 ? clubs - 1 : clubs;

    /// <summary>Rounds in a single-elimination bracket, counting the byes a non-power-of-two
    /// field forces.</summary>
    public static int BracketRounds(int clubs) => (int)Math.Ceiling(Math.Log2(clubs));

    private static CompetitionShape GroupsKnockout(int clubs)
    {
        int groups = clubs / GroupSize;
        int qualifiers = groups * QualifiersPerGroup;

        int groupRounds = RoundRobinRounds(GroupSize);
        int groupMatches = groups * GroupSize * (GroupSize - 1) / 2;

        return new CompetitionShape(
            groupRounds + BracketRounds(qualifiers),
            groupMatches + qualifiers - 1);
    }

    /// <summary>The label the UI and the CSV use.</summary>
    public static string Label(CompetitionFormat format) => format switch
    {
        CompetitionFormat.LeagueSingle => "Pontos corridos, turno único",
        CompetitionFormat.LeagueDouble => "Pontos corridos, turno e returno",
        CompetitionFormat.GroupsKnockout => "Grupos + mata-mata",
        CompetitionFormat.KnockoutOnly => "Mata-mata, jogo único",
        CompetitionFormat.NationalCup => "Copa: ida e volta, final única",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown competition format"),
    };
}
