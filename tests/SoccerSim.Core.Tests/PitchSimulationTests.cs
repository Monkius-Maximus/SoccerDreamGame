using SoccerSim.Core.Pitch;
using SoccerSim.Core.Random;
using SoccerSim.Core.Tactics;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Match-level acceptance criteria of the tactics → behaviour module, run on the tick host:
/// absolute determinism, no oscillation, and tactics visibly changing emergent behaviour.
/// </summary>
public sealed class PitchSimulationTests
{
    private const int HomeFirstId = 1;
    private const int AwayFirstId = 101;

    private static PitchSimulation Simulation(TeamTactics homeTactics, TeamTactics awayTactics, ulong seed) => new(
        TacticalAiTestKit.Team(HomeFirstId, homeTactics),
        TacticalAiTestKit.Team(AwayFirstId, awayTactics),
        RandomStream.Create(seed, StreamName.MatchSimulation));

    [Fact]
    public void SameSeedAndTactics_ProduceIdenticalTrajectoriesAndEvents()
    {
        PitchSimulation a = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 42);
        PitchSimulation b = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 42);

        for (int block = 0; block < 6; block++)
        {
            a.Run(250);
            b.Run(250);
            Assert.Equal(a.PlayerStates, b.PlayerStates);
            Assert.Equal(a.Ball, b.Ball);
        }

        Assert.Equal(a.Events, b.Events);
        Assert.Equal((a.HomeScore, a.AwayScore), (b.HomeScore, b.AwayScore));
    }

    [Fact]
    public void DifferentSeeds_DivergeEventually()
    {
        PitchSimulation a = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 1);
        PitchSimulation b = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 2);

        a.Run(1800);
        b.Run(1800);

        Assert.NotEqual(a.PlayerStates, b.PlayerStates);
    }

    [Fact]
    public void NoOscillation_UtilitySwitchesStayRare()
    {
        PitchSimulation sim = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 42);
        sim.Run(1800); // 30 seconds of play.

        foreach (PlayerState player in sim.PlayerStates)
        {
            int switches = sim.DecisionSwitches(player.PlayerId);
            Assert.True(switches <= 30, $"player {player.PlayerId} switched utility action {switches} times in 30 s");
        }
    }

    [Fact]
    public void HighPressing_KeepsDefendersOnTopOfTheCarrier()
    {
        // "Engage earlier" measured continuously: how close the nearest AWAY defender stays to
        // the HOME carrier during BUILD-UP (carrier in his own half) — the phase where a high
        // press hunts and a passive block stays home. A single match is chaotic, so the metric
        // aggregates a few fixed seeds — still fully deterministic.
        double MeanNearestDefenderDistance(double awayPressing)
        {
            double sum = 0.0;
            int samples = 0;
            foreach (ulong seed in new ulong[] { 42, 7, 123 })
            {
                PitchSimulation sim = Simulation(TeamTactics.Default, TacticalAiTestKit.Tactics(pressing: awayPressing), seed);
                for (int tick = 0; tick < 7200; tick++)
                {
                    sim.Step();
                    if (sim.Ball.OwnerPlayerId is int owner && owner < AwayFirstId)
                    {
                        PlayerState carrier = sim.GetPlayerState(owner);
                        if (carrier.Position.X > PitchDimensions.Standard.Length / 2.0)
                            continue;
                        sum += sim.PlayerStates.Where(p => p.Side == TeamSide.Away).Min(p => p.Position.DistanceTo(carrier.Position));
                        samples++;
                    }
                }
            }

            Assert.True(samples > 0, "expected the home side to build up in its own half at some point");
            return sum / samples;
        }

        double lowPress = MeanNearestDefenderDistance(0.05);
        double highPress = MeanNearestDefenderDistance(0.95);
        Assert.True(highPress < lowPress - 1.0,
            $"mean nearest-defender distance: high-press={highPress:F2} m, low-press={lowPress:F2} m");
    }

    [Fact]
    public void PassingDirectness_ChangesPassLength()
    {
        PitchSimulation shortGame = Simulation(TacticalAiTestKit.Tactics(directness: 0.05), TeamTactics.Default, seed: 42);
        PitchSimulation directGame = Simulation(TacticalAiTestKit.Tactics(directness: 0.95), TeamTactics.Default, seed: 42);

        shortGame.Run(3600);
        directGame.Run(3600);

        double MeanHomePassDistance(PitchSimulation sim)
        {
            double[] distances = sim.Events
                .Where(e => e.Kind == PitchEventKind.PassStarted && e.PlayerId < AwayFirstId)
                .Select(e => e.Value)
                .ToArray();
            Assert.True(distances.Length > 0, "expected the home side to attempt passes");
            return distances.Average();
        }

        Assert.True(MeanHomePassDistance(directGame) > MeanHomePassDistance(shortGame),
            $"mean pass distance: direct={MeanHomePassDistance(directGame):F1} m, short={MeanHomePassDistance(shortGame):F1} m");
    }

    [Fact]
    public void AttackingMentality_PushesTheTeamHigherUpThePitch()
    {
        PitchSimulation defensive = Simulation(TacticalAiTestKit.Tactics(Mentality.VeryDefensive), TeamTactics.Default, seed: 42);
        PitchSimulation attacking = Simulation(TacticalAiTestKit.Tactics(Mentality.VeryAttacking), TeamTactics.Default, seed: 42);

        double MeanHomeOutfieldX(PitchSimulation sim)
        {
            double sum = 0.0;
            int samples = 0;
            for (int block = 0; block < 12; block++)
            {
                sim.Run(150);
                foreach (PlayerState p in sim.PlayerStates)
                {
                    if (p.Side == TeamSide.Home && p.PlayerId != HomeFirstId) // skip the goalkeeper
                    {
                        sum += p.Position.X;
                        samples++;
                    }
                }
            }

            return sum / samples;
        }

        double defensiveX = MeanHomeOutfieldX(defensive);
        double attackingX = MeanHomeOutfieldX(attacking);
        Assert.True(attackingX > defensiveX + 2.0,
            $"mean home outfield X: attacking={attackingX:F1} m, defensive={defensiveX:F1} m");
    }

    [Fact]
    public void Simulation_ProducesFootballShapedActivity()
    {
        PitchSimulation sim = Simulation(TeamTactics.Default, TeamTactics.Default, seed: 7);
        sim.Run(3600); // one simulated minute.

        Assert.Contains(sim.Events, e => e.Kind == PitchEventKind.PassStarted);
        Assert.Contains(sim.Events, e => e.Kind == PitchEventKind.PassCompleted);

        // Everyone stays on the pitch.
        foreach (PlayerState p in sim.PlayerStates)
        {
            Assert.InRange(p.Position.X, 0.0, PitchDimensions.Standard.Length);
            Assert.InRange(p.Position.Y, 0.0, PitchDimensions.Standard.Width);
        }
    }
}
