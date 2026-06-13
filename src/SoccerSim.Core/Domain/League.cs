using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.Domain;

public sealed class League
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Country { get; init; }

    /// <summary>Simulation level-of-detail tier; controls how matches are resolved.</summary>
    public SimulationTier Tier { get; init; }

    public int? CurrentSeasonId { get; set; }
}
