namespace SoccerSim.Core.Domain;

/// <summary>
/// The active career / save-state: which player the human controls, and the world seed
/// everything else derives from. The controlled club is derived from that player (their
/// <see cref="Player.TeamId"/>), so "who the player is" lives in one record rather than
/// being hardcoded at the composition root.
/// </summary>
/// <param name="MasterSeed">
/// The world seed for this save. Every simulation stream derives from it via
/// <c>RandomStream.Create(MasterSeed, StreamName.X, …ids)</c>, so it is what makes a save
/// replayable — and why it belongs here rather than in the composition root. A save with no
/// recorded seed is an error, not something to substitute a default for.
/// </param>
public sealed record CareerState(
    int HumanPlayerId,
    int HumanTeamId,
    IReadOnlyDictionary<string, int> TraitWeights,
    ulong MasterSeed);

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
