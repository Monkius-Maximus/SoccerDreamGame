using SoccerSim.Core.Localization;

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
/// <see cref="SoccerSim.Core.Random.SplitMix64Random"/> — a cross-platform deterministic PRNG;
/// the simulation never uses <see cref="System.Random"/>, whose sequence is unspecified.
/// </summary>
public interface IRandom
{
    double NextDouble();

    int Next(int maxExclusive);
}

/// <summary>A single stat modifier, e.g. <c>("morale", -2)</c>. Applied to FormMood/attributes.</summary>
public readonly record struct StatDelta(string StatKey, int Delta);

/// <summary>A single resource modifier, e.g. <c>("money", -5000)</c>.</summary>
public readonly record struct ResourceDelta(string ResourceKey, long Delta);

/// <summary>
/// One selectable outcome of a High/Medium event, presented to the human when the calendar advance
/// is interrupted.
///
/// <para>
/// <see cref="RequiredTraitKey"/> is what makes <see cref="EventTier.Medium"/> a "trait-gated
/// dialogue choice" rather than a plain menu: a hot-headed player is offered a reply a composed one
/// never sees. Gating is data, not code, so new gates need no changes to the presenter.
/// </para>
/// </summary>
public sealed record EventChoice(string Key)
{
    /// <summary>
    /// Localisation key for the button label. Built from the owning definition's key plus this
    /// choice's key, so the simulation never carries a sentence in any language.
    /// </summary>
    public string LabelKey(string definitionKey) => LocKeys.EventChoiceLabel(definitionKey, Key);

    /// <summary>Localisation key for the longer body text explaining the consequence.</summary>
    public string DescriptionKey(string definitionKey) => LocKeys.EventChoiceDescription(definitionKey, Key);

    /// <summary>Stat deltas applied when this choice is taken.</summary>
    public IReadOnlyList<StatDelta> StatDeltas { get; init; } = [];

    /// <summary>Resource deltas applied when this choice is taken.</summary>
    public IReadOnlyList<ResourceDelta> ResourceDeltas { get; init; } = [];

    /// <summary>
    /// Personality dimension that unlocks this choice (an <c>EventRollContext.TraitWeights</c> key),
    /// or null when the choice is always offered.
    /// </summary>
    public string? RequiredTraitKey { get; init; }

    /// <summary>Minimum 0–100 trait weight required when <see cref="RequiredTraitKey"/> is set.</summary>
    public int RequiredTraitWeight { get; init; }

    /// <summary>True when the human's traits unlock this choice.</summary>
    public bool IsAvailableTo(IReadOnlyDictionary<string, int> traitWeights)
    {
        ArgumentNullException.ThrowIfNull(traitWeights);
        return RequiredTraitKey is null
            || (traitWeights.TryGetValue(RequiredTraitKey, out int weight) && weight >= RequiredTraitWeight);
    }
}

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
    /// <summary>Localisation key for the resolution screen's title.</summary>
    public string TitleKey => LocKeys.EventTitle(Key);

    /// <summary>
    /// Localisation key for the situation put to the human when a High/Medium event interrupts the
    /// calendar.
    /// </summary>
    public string PromptKey => LocKeys.EventPrompt(Key);

    /// <summary>
    /// The choices offered for a High/Medium event. Empty means the presenter shows an
    /// acknowledgement with no deltas, which is the correct behaviour for a not-yet-authored event.
    /// </summary>
    public IReadOnlyList<EventChoice> Choices { get; init; } = [];

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
