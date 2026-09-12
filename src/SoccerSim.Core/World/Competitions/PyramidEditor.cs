namespace SoccerSim.Core.World.Competitions;

/// <summary>Thrown when a pyramid edit is refused. The message is the reason, written for the
/// person who tried it — the API turns it into a 400 with that text.</summary>
public sealed class PyramidException : Exception
{
    public PyramidException(string message) : base(message)
    {
    }
}

/// <summary>
/// The operations the multi-league screen performs on a country's pyramid (ROADMAP.md Sprint 9).
/// Pure, like <see cref="GeoTree"/>: they take a pyramid and return a pyramid, so the rules can be
/// stated and tested without a database, and the endpoint's only job is to persist the answer.
///
/// <para>There are four, and they are chosen so that the two structural rules
/// <see cref="PyramidRules"/> checks — tiers contiguous, tiers unique — cannot be broken from the
/// screen at all. A division is always added at the BOTTOM and removing one closes the gap behind
/// it. Inserting a level in the middle is not offered: a pyramid is built downwards, and an
/// operation that can produce a hole needs a second operation to repair it.</para>
/// </summary>
public static class PyramidEditor
{
    /// <summary>
    /// Adds a division below the last one. It starts empty, promoting and relegating nobody — the
    /// flow between it and the division above is something the author states next, and a number
    /// guessed here would balance the pyramid without anyone having decided it.
    /// </summary>
    public static LeaguePyramid AddDivision(
        LeaguePyramid pyramid,
        string divisionId,
        string name,
        CompetitionFormat format,
        int clubCount)
    {
        if (string.IsNullOrWhiteSpace(divisionId))
            throw new PyramidException("uma divisão precisa de um id.");

        if (string.IsNullOrWhiteSpace(name))
            throw new PyramidException("uma divisão precisa de um nome.");

        if (pyramid.Divisions.Any(division => division.DivisionId == divisionId))
            throw new PyramidException($"já existe uma divisão com o id '{divisionId}'.");

        CheckSize(clubCount);

        int tier = pyramid.Divisions.Count == 0 ? 1 : pyramid.Divisions.Max(division => division.Tier) + 1;

        return pyramid with
        {
            Divisions = [.. pyramid.Divisions, new Division(
                tier, divisionId, name.Trim(), format, clubCount,
                PromotedIn: 0, RelegatedOut: 0, ClubIds: [])],
        };
    }

    /// <summary>
    /// Removes a division and pulls every division below it up one tier, so the pyramid stays
    /// contiguous.
    ///
    /// <para>Refused while clubs are still enrolled, for the same reason a geo node with clubs on
    /// it cannot be deleted: the clubs would leave the pyramid without anyone saying where they
    /// went. Withdraw them first — that is a decision, and this would be a side effect.</para>
    /// </summary>
    public static LeaguePyramid RemoveDivision(LeaguePyramid pyramid, string divisionId)
    {
        Division target = Find(pyramid, divisionId);

        if (target.ClubIds.Count > 0)
        {
            throw new PyramidException(
                $"{target.Name} ainda tem {target.ClubIds.Count} clube(s) inscrito(s) — retire-os antes de apagá-la.");
        }

        return pyramid with
        {
            Divisions = [.. pyramid.Divisions
                .Where(division => division.DivisionId != divisionId)
                .Select(division => division.Tier > target.Tier
                    ? division with { Tier = division.Tier - 1 }
                    : division)],
        };
    }

    /// <summary>
    /// Rewrites what the author states about a division: its name, its format, the size it is
    /// meant to reach and the flow in and out. The tier is not here — it is the division's place
    /// in the pyramid, and the only things that change it are adding and removing levels.
    /// </summary>
    public static LeaguePyramid Rewrite(
        LeaguePyramid pyramid,
        string divisionId,
        string name,
        CompetitionFormat format,
        int clubCount,
        int promotedIn,
        int relegatedOut)
    {
        Division target = Find(pyramid, divisionId);

        if (string.IsNullOrWhiteSpace(name))
            throw new PyramidException("uma divisão precisa de um nome.");

        CheckSize(clubCount);

        if (promotedIn < 0 || relegatedOut < 0)
            throw new PyramidException("promovidos e rebaixados não podem ser negativos.");

        if (promotedIn + relegatedOut > clubCount)
        {
            throw new PyramidException(
                $"{promotedIn} promovidos e {relegatedOut} rebaixados não cabem numa divisão de {clubCount} clubes.");
        }

        return Replace(pyramid, target with
        {
            Name = name.Trim(),
            Format = format,
            ClubCount = clubCount,
            PromotedIn = promotedIn,
            RelegatedOut = relegatedOut,
        });
    }

    /// <summary>
    /// Enrols a club, taking it out of whatever division of this country it was in. A club plays
    /// one division per country, so a move is what enrolling always means — asking the author to
    /// withdraw first would only give them a chance to forget.
    /// </summary>
    public static LeaguePyramid Enrol(LeaguePyramid pyramid, string divisionId, string clubId)
    {
        Division target = Find(pyramid, divisionId);

        if (string.IsNullOrWhiteSpace(clubId))
            throw new PyramidException("nenhum clube informado.");

        if (target.ClubIds.Contains(clubId))
            throw new PyramidException($"{clubId} já está em {target.Name}.");

        return pyramid with
        {
            Divisions = [.. pyramid.Divisions.Select(division => division.DivisionId == divisionId
                ? division with { ClubIds = [.. division.ClubIds, clubId] }
                : division with { ClubIds = [.. division.ClubIds.Where(id => id != clubId)] })],
        };
    }

    /// <summary>Takes a club out of a division, leaving it in no division of this country.</summary>
    public static LeaguePyramid Withdraw(LeaguePyramid pyramid, string divisionId, string clubId)
    {
        Division target = Find(pyramid, divisionId);

        if (!target.ClubIds.Contains(clubId))
            throw new PyramidException($"{clubId} não está em {target.Name}.");

        return Replace(pyramid, target with
        {
            ClubIds = [.. target.ClubIds.Where(id => id != clubId)],
        });
    }

    /// <summary>
    /// Two is the floor the schema itself states (sql/0014_world_countries.sql): one club plays
    /// nobody. Checked here as well so the answer is a refusal with a reason instead of a
    /// constraint violation from inside the database.
    /// </summary>
    private static void CheckSize(int clubCount)
    {
        if (clubCount < 2)
            throw new PyramidException($"uma divisão precisa de ao menos dois clubes, e esta declara {clubCount}.");
    }

    private static Division Find(LeaguePyramid pyramid, string divisionId) =>
        pyramid.Divisions.FirstOrDefault(division => division.DivisionId == divisionId)
        ?? throw new PyramidException($"não existe divisão '{divisionId}' em {pyramid.CountryId}.");

    private static LeaguePyramid Replace(LeaguePyramid pyramid, Division division) =>
        pyramid with
        {
            Divisions = [.. pyramid.Divisions.Select(existing =>
                existing.DivisionId == division.DivisionId ? division : existing)],
        };
}
