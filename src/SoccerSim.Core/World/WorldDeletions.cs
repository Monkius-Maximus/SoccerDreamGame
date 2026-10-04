namespace SoccerSim.Core.World;

/// <summary>
/// Removing a club or a player, with everything that pointed at them (ROADMAP.md Sprint 9).
///
/// <para>Pure, and that is the point: a club is referenced from three places — its squad, the
/// rivals that name it, and the seasons it takes part in — and forgetting one leaves a dangling
/// pointer the batch audit then reports as somebody else's problem. Stating the whole cleanup in
/// one function is what makes it checkable.</para>
/// </summary>
public static class WorldDeletions
{
    /// <summary>What a deletion took with it, so the confirmation can say it before and the
    /// message can say it after.</summary>
    public sealed record ClubRemoval(
        WorldSnapshot World,
        string ClubName,
        int Players,
        int RivalsCleared,
        int CompetitionsLeft);

    public static ClubRemoval RemoveClub(WorldSnapshot world, string clubId)
    {
        ClubIdentity club = world.Clubs.FirstOrDefault(candidate => candidate.ClubId == clubId)
            ?? throw new ArgumentException($"Não existe clube '{clubId}'.", nameof(clubId));

        // Rivals first, because a derby is a relationship: the club going away leaves the other
        // side pointing at nothing, and DERBY_DANGLING is an error in the sweep.
        var rivals = world.Clubs
            .Where(candidate => candidate.AiProfile.DerbyRivalClubId == clubId)
            .Select(candidate => candidate.ClubId)
            .ToHashSet();

        // The season loses a participant; the competition keeps its declared size. The size is the
        // structure the transition rules are written against (ADR-0012 §5), and an unfilled season
        // is what SEASON_UNFILLED reports.
        var seasons = world.Seasons
            .Select(season => season.ParticipantClubIds.Contains(clubId)
                ? season with { ParticipantClubIds = season.ParticipantClubIds.Where(id => id != clubId).ToList() }
                : season)
            .ToList();

        int players = world.Characters.Count(player => player.ClubId == clubId);

        return new ClubRemoval(
            world with
            {
                Clubs = world.Clubs
                    .Where(candidate => candidate.ClubId != clubId)
                    .Select(candidate => rivals.Contains(candidate.ClubId)
                        ? candidate with { AiProfile = candidate.AiProfile with { DerbyRivalClubId = null } }
                        : candidate)
                    .ToList(),
                Characters = world.Characters.Where(player => player.ClubId != clubId).ToList(),
                Seasons = seasons,
            },
            club.Identity.ShortName,
            players,
            rivals.Count,
            world.Seasons.Count(season => season.ParticipantClubIds.Contains(clubId)));
    }

    /// <summary>Removes one player. Nothing else points at a player, so this is the one deletion
    /// with no cleanup — worth saying, because the absence looks like an oversight otherwise.</summary>
    public static WorldSnapshot RemovePlayer(WorldSnapshot world, string playerId)
    {
        if (world.Characters.All(player => player.PlayerId != playerId))
            throw new ArgumentException($"Não existe jogador '{playerId}'.", nameof(playerId));

        return world with
        {
            Characters = world.Characters.Where(player => player.PlayerId != playerId).ToList(),
        };
    }
}
