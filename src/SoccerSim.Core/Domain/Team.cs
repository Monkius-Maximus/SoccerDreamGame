namespace SoccerSim.Core.Domain;

public sealed class Team
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int LeagueId { get; set; }

    public long Budget { get; set; }

    /// <summary>Aggregate strength used by the Tier 2 Elo resolver.</summary>
    public int EloRating { get; set; } = 1500;
}
