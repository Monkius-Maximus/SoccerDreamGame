using SoccerSim.Core.Modes;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class ModeStateMachineTests
{
    [Fact]
    public void StartsInLoading_ByDefault()
    {
        Assert.Equal(GameMode.Loading, new ModeStateMachine().CurrentMode);
    }

    [Theory]
    [InlineData(GameMode.Loading, GameMode.Calendar)]
    [InlineData(GameMode.Loading, GameMode.LifeSim)]
    [InlineData(GameMode.Loading, GameMode.Match)]
    [InlineData(GameMode.Calendar, GameMode.Match)]
    [InlineData(GameMode.Calendar, GameMode.LifeSim)]
    [InlineData(GameMode.Calendar, GameMode.Loading)]
    [InlineData(GameMode.LifeSim, GameMode.Calendar)]
    [InlineData(GameMode.LifeSim, GameMode.Loading)]
    [InlineData(GameMode.Match, GameMode.Calendar)]
    [InlineData(GameMode.Match, GameMode.Loading)]
    public void LegalTransition_Moves_AndRaisesEvent(GameMode from, GameMode to)
    {
        var machine = new ModeStateMachine(from);
        GameMode observedFrom = default;
        GameMode observedTo = default;
        bool raised = false;
        machine.ModeChanged += (a, b) =>
        {
            observedFrom = a;
            observedTo = b;
            raised = true;
        };

        machine.TransitionTo(to);

        Assert.Equal(to, machine.CurrentMode);
        Assert.True(raised);
        Assert.Equal(from, observedFrom);
        Assert.Equal(to, observedTo);
    }

    [Theory]
    [InlineData(GameMode.Loading, GameMode.Loading)] // self-transition
    [InlineData(GameMode.Match, GameMode.LifeSim)]   // not adjacent
    [InlineData(GameMode.LifeSim, GameMode.Match)]   // not adjacent
    [InlineData(GameMode.Match, GameMode.Match)]     // self-transition
    public void IllegalTransition_Throws_AndKeepsMode(GameMode from, GameMode to)
    {
        var machine = new ModeStateMachine(from);

        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(to));
        Assert.Equal(from, machine.CurrentMode);
    }

    [Fact]
    public void CanTransitionTo_Reflects_TheTable()
    {
        var machine = new ModeStateMachine(GameMode.Match);

        Assert.True(machine.CanTransitionTo(GameMode.Calendar));
        Assert.False(machine.CanTransitionTo(GameMode.LifeSim));
    }
}
