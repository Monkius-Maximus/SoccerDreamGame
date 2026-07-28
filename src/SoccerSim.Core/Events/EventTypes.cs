namespace SoccerSim.Core.Events;

/// <summary>Resolution tier for a triggered event (GDD §4 Event &amp; Interruption System).</summary>
public enum EventTier
{
    /// <summary>Abstract mini-game (e.g. contract negotiation); skill/luck dictates outcome.</summary>
    High,

    /// <summary>Trait-gated dialogue choice (e.g. press conference).</summary>
    Medium,

    /// <summary>Passive notification with an unavoidable stat modifier (e.g. flight delay).</summary>
    Low,
}

/// <summary>
/// Minimal seedable randomness seam used across the simulation so every consumer is
/// deterministically testable. The single production implementation is
/// <see cref="SoccerSim.Core.Random.RandomStream"/> — a cross-platform deterministic PRNG.
/// The BCL's own generator is never used: its sequence is an unspecified implementation
/// detail, so it cannot back a replayable save.
/// </summary>
public interface IRandom
{
    double NextDouble();

    int Next(int maxExclusive);

    /// <summary>
    /// An identifier drawn from the stream. Simulation-path entity ids must come from here
    /// rather than the system GUID generator, which is non-deterministic by definition and
    /// would break replay.
    /// </summary>
    Guid NextGuid();
}

/// <summary>A single stat modifier, e.g. <c>("morale", -2)</c>. Applied to FormMood/attributes.</summary>
public readonly record struct StatDelta(string StatKey, int Delta);

/// <summary>A single resource modifier, e.g. <c>("money", -5000)</c>.</summary>
public readonly record struct ResourceDelta(string ResourceKey, long Delta);

/// <summary>
/// Template for an event: its base per-day probability and how static personality
/// traits weight it. Low-stakes events carry their unavoidable, predetermined deltas.
/// </summary>
public sealed record EventDefinition(
    string Key,
    EventTier Tier,
    double BaseProbability,
    IReadOnlyDictionary<string, double> TraitModifiers)
{
    /// <summary>Stat deltas applied automatically when a Low-stakes event resolves.</summary>
    public IReadOnlyList<StatDelta> LowStakesStatDeltas { get; init; } = [];

    /// <summary>Resource deltas applied automatically when a Low-stakes event resolves.</summary>
    public IReadOnlyList<ResourceDelta> LowStakesResourceDeltas { get; init; } = [];
}

/// <summary>A concrete event instance produced by a roll.</summary>
public sealed record GameEvent(
    Guid Id,
    string DefinitionKey,
    EventTier Tier,
    DateTime Date,
    int PlayerId);

/// <summary>
/// Per-roll snapshot. Bundling the player's trait weights + RNG here keeps
/// <see cref="IEventManager"/> pure: it never reaches into persistence or the engine.
/// </summary>
public readonly record struct EventRollContext(
    int PlayerId,
    IReadOnlyDictionary<string, int> TraitWeights,
    double GlobalProbabilityMultiplier,
    IRandom Rng);

/// <summary>Raised to the presentation layer to resolve a High/Medium event.</summary>
public sealed record EventResolutionRequest(GameEvent Event, Guid ResumeToken);

/// <summary>The deltas to apply once an event has been resolved (by UI or inline).</summary>
public sealed record EventResolutionResult(
    Guid EventId,
    IReadOnlyList<StatDelta> StatDeltas,
    IReadOnlyList<ResourceDelta> ResourceDeltas);
