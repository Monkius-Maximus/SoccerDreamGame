using SoccerSim.Core.World.Validation;

namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// One division of a country's pyramid. Distinct from <see cref="Competition"/>, which is the
/// FROZEN EDITION of a competition — its member list, its season, its prestige band. A division
/// is the standing structure the editions hang off: it outlives any one season, and it is what
/// promotion and relegation actually connect.
/// </summary>
public sealed record Division(
    /// <summary>1 is the top flight. Tiers are contiguous: a gap means clubs fall out of the
    /// pyramid at the end of a season with nowhere to land.</summary>
    int Tier,
    string DivisionId,
    string Name,
    CompetitionFormat Format,
    int ClubCount,
    /// <summary>How many clubs come UP into this division from the one below.</summary>
    int PromotedIn,
    /// <summary>How many clubs go DOWN out of this division into the one below.</summary>
    int RelegatedOut,
    /// <summary>The clubs enrolled in this division. A club belongs to exactly one division per
    /// country.</summary>
    IReadOnlyList<string> ClubIds)
{
    /// <summary>
    /// Rounds and matches, derived from the format and the field size — never typed.
    ///
    /// <para>Null when this field cannot be played at all: a division just created and not yet
    /// enrolled has no shape, and neither does a group stage over a field that will not divide
    /// into fours. A number invented for those cases would be a fixture list nobody can build, so
    /// the screen shows a dash and <see cref="PyramidRules"/> says why.</para>
    /// </summary>
    public CompetitionShape? Shape => CompetitionFormats.Unplayable(Format, ClubCount) is null
        ? CompetitionFormats.Shape(Format, ClubCount)
        : null;
}

/// <summary>A country's divisions, top to bottom.</summary>
public sealed record LeaguePyramid(string CountryId, IReadOnlyList<Division> Divisions);

/// <summary>
/// What has to be true of a pyramid for a season to be playable at all (ROADMAP.md Sprint 9).
/// These are the failures that only appear once a second division exists, which is why none of
/// them could be written before now.
/// </summary>
public static class PyramidRules
{
    public static IReadOnlyList<Finding> Check(LeaguePyramid pyramid)
    {
        var findings = new List<Finding>();

        List<Division> divisions = pyramid.Divisions.OrderBy(division => division.Tier).ToList();

        if (divisions.Count == 0)
        {
            findings.Add(new Finding(FindingLevel.Error, "PYRAMID_EMPTY", "Pirâmide vazia",
                $"{pyramid.CountryId} não tem nenhuma divisão"));
            return findings;
        }

        CheckTiers(findings, divisions);
        CheckFlow(findings, divisions);
        CheckEnrolment(findings, pyramid, divisions);
        CheckFieldSizes(findings, divisions);

        return findings;
    }

    /// <summary>Tiers run 1, 2, 3… with no repeats and no holes. A hole is a division that
    /// relegates clubs into a level that does not exist.</summary>
    private static void CheckTiers(List<Finding> findings, List<Division> divisions)
    {
        var byTier = divisions.GroupBy(division => division.Tier);

        foreach (var group in byTier.Where(g => g.Count() > 1))
        {
            findings.Add(new Finding(FindingLevel.Error, "TIER_DUP", "Tier duplicado",
                $"tier {group.Key} está em {group.Count()} divisões ({string.Join(", ", group.Select(d => d.DivisionId))})"));
        }

        var tiers = divisions.Select(division => division.Tier).Distinct().Order().ToList();

        if (tiers[0] != 1)
        {
            findings.Add(new Finding(FindingLevel.Error, "TIER_GAP", "Pirâmide sem topo",
                $"o tier mais alto é {tiers[0]}; a pirâmide começa em 1"));
        }

        for (int i = 1; i < tiers.Count; i++)
        {
            if (tiers[i] != tiers[i - 1] + 1)
            {
                findings.Add(new Finding(FindingLevel.Error, "TIER_GAP", "Lacuna de tier",
                    $"não existe divisão no tier {tiers[i - 1] + 1}, entre {tiers[i - 1]} e {tiers[i]}"));
            }
        }
    }

    /// <summary>
    /// The flow balance. For a division, clubs arriving are those relegated out of the division
    /// above plus those promoted in from the one below; clubs leaving are those promoted into the
    /// division above plus those relegated out of this one. When the two differ, the division
    /// changes size every season — silently, and only visibly three seasons later.
    /// </summary>
    private static void CheckFlow(List<Finding> findings, List<Division> divisions)
    {
        for (int i = 0; i < divisions.Count; i++)
        {
            Division division = divisions[i];
            Division? above = i > 0 ? divisions[i - 1] : null;
            Division? below = i + 1 < divisions.Count ? divisions[i + 1] : null;

            int arriving = (above?.RelegatedOut ?? 0) + division.PromotedIn;
            int leaving = (above?.PromotedIn ?? 0) + division.RelegatedOut;

            if (arriving != leaving)
            {
                findings.Add(new Finding(FindingLevel.Error, "PYRAMID_FLOW", "Fluxo desequilibrado",
                    $"{division.Name} recebe {arriving} e perde {leaving} por temporada — "
                    + $"a divisão muda de tamanho ({division.ClubCount} clubes hoje)"));
            }

            // Nothing is below the bottom division, so nothing can come up into it and nothing
            // can go down out of it.
            if (below is null && (division.PromotedIn != 0 || division.RelegatedOut != 0))
            {
                findings.Add(new Finding(FindingLevel.Error, "PYRAMID_FLOW", "Base da pirâmide",
                    $"{division.Name} é a última divisão, mas declara "
                    + $"{division.PromotedIn} promovidos e {division.RelegatedOut} rebaixados"));
            }
        }
    }

    /// <summary>A club plays in exactly one division of a country. Two is not a bigger season; it
    /// is a fixture list that cannot be built.</summary>
    private static void CheckEnrolment(List<Finding> findings, LeaguePyramid pyramid, List<Division> divisions)
    {
        var seen = new Dictionary<string, List<Division>>();

        foreach (Division division in divisions)
        {
            foreach (string clubId in division.ClubIds)
            {
                if (!seen.TryGetValue(clubId, out List<Division>? list))
                    seen[clubId] = list = [];
                list.Add(division);
            }
        }

        foreach ((string clubId, List<Division> memberships) in seen.Where(entry => entry.Value.Count > 1))
        {
            findings.Add(new Finding(FindingLevel.Error, "CLUB_TWO_DIVISIONS", "Clube em duas divisões",
                $"{clubId} está em {string.Join(" e ", memberships.Select(d => d.Name))} "
                + $"— um clube joga uma divisão por país"));
        }
    }

    /// <summary>The declared size has to match the enrolment, and the format has to be able to
    /// use it.</summary>
    private static void CheckFieldSizes(List<Finding> findings, List<Division> divisions)
    {
        foreach (Division division in divisions)
        {
            if (division.ClubIds.Count != division.ClubCount)
            {
                findings.Add(new Finding(FindingLevel.Warning, "DIVISION_SIZE", "Tamanho da divisão",
                    $"{division.Name} declara {division.ClubCount} clubes e tem {division.ClubIds.Count} inscritos"));
            }

            if (CompetitionFormats.Unplayable(division.Format, division.ClubCount) is { } reason)
            {
                findings.Add(new Finding(FindingLevel.Error, "FORMAT_UNPLAYABLE", "Formato impossível",
                    $"{division.Name}: {reason}"));
            }
        }
    }
}
