using System.Globalization;
using System.Text;
using SoccerSim.Core.World.Competitions;

namespace SoccerSim.Core.World.Search;

/// <summary>Which screen a hit belongs to. The screen a result opens is a property of what it
/// IS, so it is decided here rather than guessed at by the front end.</summary>
public enum SearchCategory
{
    Club,
    Player,
    Competition,
    Country,
    GeoNode,
    Source,
    CalibrationConstant,
}

/// <summary>
/// One hit. <see cref="EntityId"/> is what the screen navigates to; <see cref="ClubId"/> is set
/// only for players, because opening a player means opening the club page they live on.
/// </summary>
public sealed record SearchHit(
    SearchCategory Category,
    string EntityId,
    string Label,
    /// <summary>Where it sits, or what it is — the line under the name that tells two clubs called
    /// "Norte" apart.</summary>
    string Detail,
    string? ClubId,
    /// <summary>Lower sorts first. Not shown; it is what puts an exact code above a substring
    /// buried in a note.</summary>
    int Rank);

public sealed record SearchGroup(SearchCategory Category, IReadOnlyList<SearchHit> Hits, int Total);

public sealed record SearchResult(string Query, IReadOnlyList<SearchGroup> Groups, int Total);

/// <summary>
/// The global search (ROADMAP.md Sprint 9). The rail filters clubs, which is the right tool for
/// the screen it sits on and useless for everything else: a player's name, a geo node, a source,
/// the key of a calibration constant. This sweeps all seven and says which screen each answer is
/// on.
///
/// <para>Accent- and case-insensitive, because the batch is Portuguese and nobody types "Pó de
/// Arroz" with the accent when they are looking for it. Matching is on a normalised copy; what is
/// shown is always the original.</para>
/// </summary>
public static class WorldSearch
{
    /// <summary>Below two characters the answer is "most of the world", which is not an answer.</summary>
    public const int MinimumQueryLength = 2;

    /// <summary>Per category, so one crowded category cannot bury the rest. The group carries its
    /// full count so the screen can say how many were not drawn.</summary>
    public const int PerCategory = 12;

    // Rank bands. An id or code typed in full is almost always the thing being looked for; a
    // substring in the middle of a note almost never is.
    private const int ExactRank = 0;
    private const int PrefixRank = 1;
    private const int ContainsRank = 2;

    public static SearchResult Search(
        WorldSnapshot world,
        IReadOnlyList<CountryProfile> countries,
        string query)
    {
        string needle = Normalize(query);

        if (needle.Length < MinimumQueryLength)
            return new SearchResult(query, [], 0);

        List<SearchHit> hits =
        [
            .. Clubs(world, needle),
            .. Players(world, needle),
            .. Competitions(world, needle),
            .. Countries(world, countries, needle),
            .. GeoNodes(world, needle),
            .. Sources(world, needle),
            .. Constants(world, needle),
        ];

        var groups = new List<SearchGroup>();

        // Enum order, which is the order the categories are declared in and the order the screen
        // draws them: the things an author is most often looking for first.
        foreach (SearchCategory category in Enum.GetValues<SearchCategory>())
        {
            List<SearchHit> found =
            [
                .. hits.Where(hit => hit.Category == category)
                    .OrderBy(hit => hit.Rank)
                    .ThenBy(hit => hit.Label, StringComparer.Ordinal),
            ];

            if (found.Count > 0)
                groups.Add(new SearchGroup(category, [.. found.Take(PerCategory)], found.Count));
        }

        return new SearchResult(query, groups, hits.Count);
    }

    // ------------------------------------------------------------- the sweeps

    private static IEnumerable<SearchHit> Clubs(WorldSnapshot world, string needle)
    {
        Dictionary<string, GeoNode> nodes = world.GeoNodes.ToDictionary(node => node.GeoNodeId);

        foreach (ClubIdentity club in world.Clubs)
        {
            int? rank = Best(needle,
                club.ClubId, club.DisplayCode, club.Identity.ShortName,
                club.Identity.OfficialName, club.Identity.Nickname, club.Stadium.Name);

            if (rank is null)
                continue;

            string city = nodes.TryGetValue(club.Geography.GeoNodeId, out GeoNode? node)
                ? node.DisplayName
                : club.Geography.GeoNodeId;

            yield return new SearchHit(
                SearchCategory.Club,
                club.ClubId,
                club.Identity.ShortName,
                $"{city} · {club.DisplayCode} · {club.World.PrestigeBand}",
                club.ClubId,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> Players(WorldSnapshot world, string needle)
    {
        Dictionary<string, string> clubNames = world.Clubs.ToDictionary(
            club => club.ClubId, club => club.Identity.ShortName);

        foreach (CharacterRecord player in world.Characters)
        {
            int? rank = Best(needle,
                player.PlayerId, player.ShirtName,
                $"{player.FirstName} {player.LastName}", player.LastName);

            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.Player,
                player.PlayerId,
                $"{player.FirstName} {player.LastName}",
                $"{clubNames.GetValueOrDefault(player.ClubId, player.ClubId)} · "
                + $"{player.PrimaryPosition} · {player.Overall} OVR · {player.Age} anos",
                player.ClubId,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> Competitions(WorldSnapshot world, string needle)
    {
        foreach (Competition competition in world.Competitions)
        {
            int? rank = Best(needle, competition.CompetitionId, competition.Name, competition.EditionId);
            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.Competition,
                competition.CompetitionId,
                competition.Name,
                $"{competition.Scope} · {competition.ClubCount} clubes · {competition.Rounds} rodadas",
                null,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> Countries(
        WorldSnapshot world,
        IReadOnlyList<CountryProfile> countries,
        string needle)
    {
        ILookup<string, ClubIdentity> byCountry = world.Clubs.ToLookup(club => club.Geography.CountryId);

        foreach (CountryProfile country in countries)
        {
            int? rank = Best(needle, country.CountryId, country.Currency);
            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.Country,
                country.CountryId,
                country.CountryId,
                $"{byCountry[country.CountryId].Count()} clubes · {country.Currency}",
                null,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> GeoNodes(WorldSnapshot world, string needle)
    {
        foreach (GeoNode node in world.GeoNodes)
        {
            int? rank = Best(needle, node.GeoNodeId, node.DisplayName);
            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.GeoNode,
                node.GeoNodeId,
                node.DisplayName,
                $"{node.Kind} · {node.GeoNodeId}",
                null,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> Sources(WorldSnapshot world, string needle)
    {
        foreach (WorldSource source in world.Sources)
        {
            int? rank = Best(needle, source.Tema, source.Fonte, source.Numero, source.Url);
            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.Source,
                source.Tema,
                source.Fonte ?? source.Tema,
                source.Url is null ? $"{source.Tema} · sem URL" : source.Tema,
                null,
                rank.Value);
        }
    }

    private static IEnumerable<SearchHit> Constants(WorldSnapshot world, string needle)
    {
        foreach ((string key, CalibrationConstant constant) in world.Calibration.Constants)
        {
            int? rank = Best(needle, key, constant.Unit, constant.Note);
            if (rank is null)
                continue;

            yield return new SearchHit(
                SearchCategory.CalibrationConstant,
                key,
                key,
                $"{constant.Value.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"))} {constant.Unit}",
                null,
                rank.Value);
        }
    }

    // ------------------------------------------------------------- the matching

    /// <summary>The best rank any of these fields gives, or null when none of them match.</summary>
    private static int? Best(string needle, params string?[] fields)
    {
        int? best = null;

        foreach (string? field in fields)
        {
            if (Rank(needle, field) is not { } rank)
                continue;

            if (best is null || rank < best)
                best = rank;
        }

        return best;
    }

    private static int? Rank(string needle, string? field)
    {
        if (string.IsNullOrEmpty(field))
            return null;

        string hay = Normalize(field);

        if (hay == needle)
            return ExactRank;

        if (hay.StartsWith(needle, StringComparison.Ordinal))
            return PrefixRank;

        return hay.Contains(needle, StringComparison.Ordinal) ? ContainsRank : null;
    }

    /// <summary>
    /// Lower-cased and stripped of accents. Decomposing and dropping the combining marks rather
    /// than keeping a table of letter pairs: the batch is Portuguese, and a table would be a list
    /// of the accents somebody happened to think of.
    /// </summary>
    private static string Normalize(string text)
    {
        string decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
