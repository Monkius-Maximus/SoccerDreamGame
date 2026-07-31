using SoccerSim.Core.LifeSim;

namespace SoccerSim.Core.Persistence;

/// <summary>
/// Persists the off-pitch wellbeing of the active career and the log of what the human did with
/// their days. Synchronous and career-scoped — the same shape as <see cref="ICareerService"/> and
/// <c>IFixtureGateway</c> — so the composition root can load it during Godot's synchronous startup.
/// </summary>
public interface IWellbeingRepository
{
    /// <summary>
    /// The saved need gauges for a career, or <c>null</c> when this career has never been advanced
    /// (a fresh save), in which case the caller seeds <see cref="WellbeingState.CreateDefault"/>.
    /// </summary>
    WellbeingState? Load(int careerId, CareerRole role);

    /// <summary>Write every need gauge for a career, replacing what was there.</summary>
    void Save(int careerId, WellbeingState state);

    /// <summary>
    /// Append a performed activity. The log is what a future "how did I spend this season" screen
    /// and the economy's spending breakdown both read.
    /// </summary>
    void LogActivity(int careerId, DateTime date, ActivityOutcome outcome);
}
