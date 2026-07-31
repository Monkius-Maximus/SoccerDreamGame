using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The default <see cref="IWellbeingService"/>: holds the career's state, runs it through the
/// <see cref="ILifeSimulator"/>, writes through to persistence, and caches the derived snapshot so
/// the HUD can read it every frame without recomputing the weighted index.
///
/// <para>
/// Persistence is optional (<c>null</c> repository) so headless tests and a future multiplayer
/// server can run the life-sim without a database behind it.
/// </para>
/// </summary>
public sealed class WellbeingService : IWellbeingService
{
    private readonly ILifeSimulator _simulator;
    private readonly IRandom _rng;
    private readonly IWellbeingRepository? _repository;
    private readonly int _careerId;

    private WellbeingSnapshot _snapshot;

    public WellbeingService(
        WellbeingState state,
        ILifeSimulator simulator,
        IRandom rng,
        IWellbeingRepository? repository = null,
        int careerId = 1)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _repository = repository;
        _careerId = careerId;
        _snapshot = _simulator.Snapshot(State);
    }

    public CareerRole Role => State.Role;

    public WellbeingState State { get; }

    public WellbeingSnapshot Snapshot => _snapshot;

    public double EventProbabilityMultiplier => _snapshot.EventProbabilityMultiplier;

    public event Action<WellbeingSnapshot>? Changed;

    public IReadOnlyList<NeedAlert> AdvanceDay(DateTime date)
    {
        IReadOnlyList<NeedAlert> alerts = _simulator.AdvanceDay(State, date, _rng);
        _repository?.Save(_careerId, State);
        Refresh();
        return alerts;
    }

    public ActivityOutcome Perform(string activityKey, DateTime date)
    {
        LifeActivity activity = LifeActivityCatalogue.ByKey(activityKey);
        ActivityOutcome outcome = _simulator.Perform(State, activity);
        _repository?.Save(_careerId, State);
        _repository?.LogActivity(_careerId, date, outcome);
        Refresh();
        return outcome;
    }

    public void SyncFormMood(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        // FormMood.Neutral.Adjust clamps into −5..+5, the same range the snapshot already produces,
        // so this is an assignment rather than an accumulation — wellbeing sets form, it does not drift it.
        player.FormMood = FormMood.Neutral.Adjust(_snapshot.FormModifier);
    }

    private void Refresh()
    {
        _snapshot = _simulator.Snapshot(State);
        Changed?.Invoke(_snapshot);
    }
}
