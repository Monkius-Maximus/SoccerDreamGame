using SoccerSim.Core.LifeSim;

namespace SoccerSim.Core.Domain;

/// <summary>
/// The active career / save-state: which player the human controls. The controlled
/// club is derived from that player (their <see cref="Player.TeamId"/>), so "who the
/// player is" lives in one record rather than being hardcoded at the composition root.
/// </summary>
/// <param name="Role">
/// Which career is being lived. Selects the life-sim's <see cref="NeedProfile"/> and decides which
/// derived wellbeing outputs the consuming systems read — the single flag that forks a player
/// career from a manager career without forking the simulation.
/// </param>
public sealed record CareerState(
    int HumanPlayerId,
    int HumanTeamId,
    IReadOnlyDictionary<string, int> TraitWeights,
    CareerRole Role);

/// <summary>
/// Projects a player's static personality traits into the 0–100 trait weights the event
/// roll consumes (<c>EventRollContext.TraitWeights</c>; see <c>EventManager.RollForDay</c>,
/// which divides the weight by 100). Keys are the personality dimensions that event
/// definitions modify (e.g. "aggression"). A player can hold several traits, so each
/// dimension takes the strongest expression across them — keeping the value within the
/// 0–100 range the roll formula assumes.
/// </summary>
public static class PlayerTraitWeights
{
    public const string Aggression = "aggression";
    public const string Selfishness = "selfishness";

    public static IReadOnlyDictionary<string, int> From(IReadOnlyList<PlayerTrait> traits)
    {
        var weights = new Dictionary<string, int>();
        foreach (PlayerTrait trait in traits)
        {
            weights[Aggression] = Math.Max(weights.GetValueOrDefault(Aggression), trait.Aggression);
            weights[Selfishness] = Math.Max(weights.GetValueOrDefault(Selfishness), trait.Selfishness);
        }

        return weights;
    }
}
