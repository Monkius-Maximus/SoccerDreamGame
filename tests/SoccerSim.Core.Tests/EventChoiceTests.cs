using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Covers the trait gating that turns a Medium event from a plain menu into the
/// "trait-gated dialogue choice" its tier promises: a hot-headed character is offered replies a
/// composed one never sees.
/// </summary>
public sealed class EventChoiceTests
{
    private static readonly IReadOnlyDictionary<string, int> HotHeaded =
        PlayerTraitWeights.From(new[] { new PlayerTrait(1, "hot_headed", "Hot-Headed", 85, 40, 10) });

    private static readonly IReadOnlyDictionary<string, int> Composed =
        PlayerTraitWeights.From(new[] { new PlayerTrait(2, "team_player", "Team Player", 30, 10, -5) });

    [Fact]
    public void UngatedChoice_IsOfferedToEveryone()
    {
        var choice = new EventChoice("deflect");

        Assert.True(choice.IsAvailableTo(HotHeaded));
        Assert.True(choice.IsAvailableTo(Composed));
        Assert.True(choice.IsAvailableTo(new Dictionary<string, int>()));
    }

    [Fact]
    public void GatedChoice_IsOfferedOnlyWhenTheTraitClearsTheThreshold()
    {
        var choice = new EventChoice("hit_back")
        {
            RequiredTraitKey = PlayerTraitWeights.Aggression,
            RequiredTraitWeight = 60,
        };

        Assert.True(choice.IsAvailableTo(HotHeaded));   // aggression 85 >= 60
        Assert.False(choice.IsAvailableTo(Composed));   // aggression 30 <  60
    }

    [Fact]
    public void GatedChoice_IsWithheldWhenTheTraitIsAbsentEntirely()
    {
        var choice = new EventChoice("walk")
        {
            RequiredTraitKey = PlayerTraitWeights.Selfishness,
            RequiredTraitWeight = 60,
        };

        Assert.False(choice.IsAvailableTo(new Dictionary<string, int>()));
    }

    [Fact]
    public void GateIsInclusive_AtExactlyTheThreshold()
    {
        var choice = new EventChoice("edge")
        {
            RequiredTraitKey = PlayerTraitWeights.Aggression,
            RequiredTraitWeight = 85,
        };

        Assert.True(choice.IsAvailableTo(HotHeaded)); // aggression is exactly 85
    }

    [Fact]
    public void Definition_WithoutAuthoredChoices_ExposesAnEmptyList()
    {
        // The presenter falls back to an acknowledgement for these, so an unauthored event can
        // still be dismissed rather than stranding the calendar's resume token.
        var definition = new EventDefinition(
            "flight_delay", EventTier.Low, 0.02, new Dictionary<string, double>());

        Assert.Empty(definition.Choices);
        // Keys are always derivable; whether the catalogue defines them is the localizer's business.
        Assert.Equal("event.flight_delay.prompt", definition.PromptKey);
    }
}
