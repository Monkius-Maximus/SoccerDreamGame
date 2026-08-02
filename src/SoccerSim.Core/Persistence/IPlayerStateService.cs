using SoccerSim.Core.Domain;

namespace SoccerSim.Core.Persistence;

/// <summary>
/// Loads the human's player aggregate and persists their DYNAMIC state.
///
/// <para>
/// Exists because the life simulation's whole claim — that wellbeing reaches the pitch — needs a
/// live <see cref="Player"/> to write <see cref="Player.FormMood"/> into. Without this, the form
/// modifier is computed and displayed but never applied to anything.
/// </para>
///
/// <para>
/// Synchronous and connection-only, matching <see cref="ICareerService"/> and <c>IFixtureGateway</c>,
/// so the composition root can use it during Godot's synchronous startup. The async
/// <see cref="IPlayerRepository"/> remains the right tool for bulk work; this is the narrow
/// "who am I, and save my form" path.
/// </para>
/// </summary>
public interface IPlayerStateService
{
    /// <summary>The player aggregate with base attributes and traits, or null when unknown.</summary>
    Player? Load(int playerId);

    /// <summary>
    /// The season a team is currently playing, resolved through its league's current season, or
    /// null when the league has no season open. FormMood is keyed by season, so a save with no
    /// season simply does not persist form rather than inventing one.
    /// </summary>
    int? GetCurrentSeasonId(int teamId);

    /// <summary>
    /// Write the player's form for a season. Upserts, because the value is recalculated every
    /// simulated day rather than appended.
    /// </summary>
    void SaveFormMood(int playerId, int seasonId, FormMood form, DateTime date);

    /// <summary>The persisted form for a season, or null when none has been written yet.</summary>
    FormMood? LoadFormMood(int playerId, int seasonId);
}
