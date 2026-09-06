namespace SoccerSim.Core.World;

/// <summary>
/// The aggregate numbers a squad produces: what the club page's metric strip shows, what the
/// competition table sorts by, and what the comparison grid puts side by side. Computed in Core
/// so every surface agrees — the browser never adds up a squad itself.
/// </summary>
public sealed record SquadMetrics(
    int PlayerCount,
    int Overall,
    long TotalMarketValueEur,
    long TotalMonthlyWageBrl,
    double AverageAge,
    IReadOnlyDictionary<Position, int> CountByPosition,
    IReadOnlyDictionary<SquadRole, int> CountByRole,
    int AnchoredCount)
{
    /// <summary>
    /// A club's overall is the mean of its ELEVEN best players, not of the whole squad: a 40-man
    /// roster padded with youth would otherwise look weaker than a thin strong one. An empty
    /// squad has an overall of 0 — a club with no players cannot be rated, and the club page says
    /// so rather than showing a plausible-looking number.
    /// </summary>
    public static SquadMetrics For(IReadOnlyList<CharacterRecord> squad)
    {
        if (squad.Count == 0)
            return Empty;

        int overall = (int)Math.Round(
            squad.OrderByDescending(player => player.Overall).Take(11).Average(player => player.Overall),
            MidpointRounding.AwayFromZero);

        return new SquadMetrics(
            PlayerCount: squad.Count,
            Overall: overall,
            TotalMarketValueEur: squad.Sum(player => (long)player.MarketValueEur),
            TotalMonthlyWageBrl: squad.Sum(player => (long)player.SalaryMonthlyBrl),
            AverageAge: squad.Average(player => player.Age),
            CountByPosition: Enum.GetValues<Position>().ToDictionary(
                position => position,
                position => squad.Count(player => player.PrimaryPosition == position)),
            CountByRole: Enum.GetValues<SquadRole>().ToDictionary(
                role => role,
                role => squad.Count(player => player.SquadRole == role)),
            AnchoredCount: squad.Count(player => player.Provenance == Provenance.Anchored));
    }

    public static SquadMetrics Empty { get; } = new(
        PlayerCount: 0,
        Overall: 0,
        TotalMarketValueEur: 0,
        TotalMonthlyWageBrl: 0,
        AverageAge: 0,
        CountByPosition: Enum.GetValues<Position>().ToDictionary(position => position, _ => 0),
        CountByRole: Enum.GetValues<SquadRole>().ToDictionary(role => role, _ => 0),
        AnchoredCount: 0);
}
