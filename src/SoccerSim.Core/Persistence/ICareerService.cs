using SoccerSim.Core.Domain;

namespace SoccerSim.Core.Persistence;

/// <summary>
/// Reads the active career / save-state — who the human controls — for this save file.
/// Synchronous to match the composition root's startup path (mirrors <c>IFixtureGateway</c>);
/// returns <c>null</c> when no career has been created yet (e.g. an unseeded database).
/// </summary>
public interface ICareerService
{
    CareerState? GetActiveCareer();
}
