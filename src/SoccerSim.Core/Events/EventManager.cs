namespace SoccerSim.Core.Events;

/// <inheritdoc cref="IEventManager"/>
public sealed class EventManager : IEventManager
{
    private readonly List<EventDefinition> _definitions;

    public EventManager(IEnumerable<EventDefinition> definitions)
        => _definitions = definitions.OrderBy(d => d.Tier).ToList();

    public IReadOnlyList<EventDefinition> Definitions => _definitions;

    public event Func<EventResolutionRequest, Task<EventResolutionResult>>? ResolutionRequested;

    public GameEvent? RollForDay(DateTime date, EventRollContext context)
    {
        foreach (EventDefinition definition in _definitions)
        {
            double probability = definition.BaseProbability * context.GlobalProbabilityMultiplier;

            // Weight by the player's STATIC personality traits (read-only during the season).
            foreach ((string traitKey, double modifier) in definition.TraitModifiers)
            {
                if (context.TraitWeights.TryGetValue(traitKey, out int weight))
                    probability += (weight / 100.0) * modifier;
            }

            probability = Math.Clamp(probability, 0.0, 1.0);
            if (context.Rng.NextDouble() < probability)
                return new GameEvent(Guid.NewGuid(), definition.Key, definition.Tier, date, context.PlayerId);
        }

        return null;
    }

    public EventResolutionResult ResolveLowStakes(GameEvent gameEvent)
    {
        EventDefinition definition = _definitions.First(d => d.Key == gameEvent.DefinitionKey);
        return new EventResolutionResult(
            gameEvent.Id,
            definition.LowStakesStatDeltas,
            definition.LowStakesResourceDeltas);
    }

    public Task<EventResolutionResult> RequestResolutionAsync(GameEvent gameEvent, Guid resumeToken)
    {
        Func<EventResolutionRequest, Task<EventResolutionResult>>? handler = ResolutionRequested;
        if (handler is null)
        {
            throw new InvalidOperationException(
                $"No resolution handler is registered for {gameEvent.Tier} event '{gameEvent.DefinitionKey}'.");
        }

        return handler(new EventResolutionRequest(gameEvent, resumeToken));
    }
}
