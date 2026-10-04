namespace SoccerSim.Core.World.Competitions;

/// <summary>What a stage produces for a given field size.</summary>
public sealed record CompetitionShape(int Rounds, int Matches);

/// <summary>
/// Rounds and matches are DERIVED from the stage and the number of clubs, never typed (ADR-0007 §2,
/// ADR-0012 §4). A typed round count is a number with no source, and the whole project runs on the
/// opposite rule.
///
/// <para>The pilot league is the proof: 20 clubs in a two-legged league derive to 38 rounds and
/// 380 matches, which is exactly what the authored data said and what the real Brasileirão
/// plays.</para>
/// </summary>
public static class CompetitionStages
{
    /// <summary>
    /// Why this stage cannot be played by this field, or null when it can. Stated once, here, so
    /// that the value, the exception and the audit finding all say the same thing.
    /// </summary>
    public static string? Unplayable(CompetitionStage stage, int clubs)
    {
        if (clubs < 2)
            return $"uma competição precisa de ao menos dois clubes, e esta tem {clubs}";

        if (stage.Legs is not (1 or 2))
            return $"uma liga tem um ou dois turnos, e esta declara {stage.Legs}";

        return null;
    }

    public static CompetitionShape Shape(CompetitionStage stage, int clubs)
    {
        if (Unplayable(stage, clubs) is { } reason)
            throw new ArgumentOutOfRangeException(nameof(clubs), clubs, reason);

        return stage.Kind switch
        {
            StageKind.League => new CompetitionShape(
                RoundRobinRounds(clubs) * stage.Legs,
                clubs * (clubs - 1) / 2 * stage.Legs),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage.Kind, "unknown stage kind"),
        };
    }

    /// <summary>
    /// The competition's shape, or null when it has none: not exactly one stage (STAGE_COUNT) or a
    /// stage its field cannot play (STAGE_UNPLAYABLE). A screen shows a dash and
    /// <see cref="PyramidRules"/> says why; a number invented here would be a fixture list nobody
    /// can build (ADR-0008 §4).
    /// </summary>
    public static CompetitionShape? ShapeOf(Competition competition) =>
        competition.Stages.Count == 1 && Unplayable(competition.Stages[0], competition.ClubCount) is null
            ? Shape(competition.Stages[0], competition.ClubCount)
            : null;

    /// <summary>
    /// A round robin over an EVEN field takes n−1 rounds; over an odd field it takes n, because
    /// one club sits out each round. Getting this wrong is how a fixture list ends up one round
    /// short and nobody notices until the last weekend.
    /// </summary>
    public static int RoundRobinRounds(int clubs) => clubs % 2 == 0 ? clubs - 1 : clubs;

    /// <summary>The label the UI and the register use.</summary>
    public static string Label(CompetitionStage stage) => (stage.Kind, stage.Legs) switch
    {
        (StageKind.League, 1) => "Pontos corridos, turno único",
        (StageKind.League, 2) => "Pontos corridos, turno e returno",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "a league has one or two legs"),
    };

    /// <summary>The single league stage a competition authored on the pyramid screen has.</summary>
    public static IReadOnlyList<CompetitionStage> League(int legs) => [new CompetitionStage(1, StageKind.League, legs)];

    /// <summary>
    /// The league tier float of a season: the mean <see cref="ClubIdentity.World"/> strength of its
    /// participants (ADR-0005 §4, ADR-0012 §7), rounded to nine places and then half-up to two —
    /// the pilot's 0.8605 is 0.86. Null when the season has no participants, which has no tier.
    /// </summary>
    public static double? TierFloat(CompetitionSeason season, IReadOnlyDictionary<string, ClubIdentity> clubs)
    {
        if (season.ParticipantClubIds.Count == 0)
            return null;

        double mean = season.ParticipantClubIds
            .Select(clubId => clubs.TryGetValue(clubId, out ClubIdentity? club)
                ? club.World.ClubStrength
                : throw new InvalidOperationException(
                    $"{season.SeasonId} lists {clubId}, which is not a club in the world."))
            .Average();

        return Math.Round(Math.Round(mean, 9), 2, MidpointRounding.AwayFromZero);
    }
}
