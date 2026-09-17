namespace SoccerSim.Core.World.Competitions;

/// <summary>
/// Builds a country's profile from the batch that is already in it.
///
/// <para>This exists because the pilot world has one country and no profile: the calibration
/// carries <c>eurToBrl</c> and <c>wageFloorBrl</c> as global constants, which was honest while
/// "global" and "Brazil" were the same thing. Seeding a profile from the batch is how the world
/// gets the shape a second country needs, without anyone having to retype what is already
/// there.</para>
/// </summary>
public static class CountryProfiles
{
    /// <summary>
    /// Currency codes for the countries the pilot knows. Keyed by the ISO code the batch uses in
    /// <c>geography.countryId</c> ("BRA"), which is not the geo node's id ("geo_bra") — the code
    /// identifies the country, the node holds its name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownCurrencies =
        new Dictionary<string, string> { ["BRA"] = "BRL" };

    /// <summary>
    /// The profile a country's own data implies: its currency and wage floor from the calibration,
    /// and its nationality mix measured from the players actually in it.
    ///
    /// <para><see cref="CountryProfile.NationalityMixSource"/> is left null on purpose. A
    /// distribution measured from the batch it will go on to generate is not evidence about the
    /// world, and the audit says so rather than letting it pass as sourced.</para>
    /// </summary>
    public static CountryProfile FromBatch(
        string countryId,
        IReadOnlyList<ClubIdentity> clubs,
        IReadOnlyList<CharacterRecord> characters,
        WorldCalibration calibration)
    {
        var clubIds = clubs
            .Where(club => club.Geography.CountryId == countryId)
            .Select(club => club.ClubId)
            .ToHashSet();

        var squad = characters.Where(player => clubIds.Contains(player.ClubId)).ToList();

        return new CountryProfile(
            countryId,
            KnownCurrencies.TryGetValue(countryId, out string? currency) ? currency : "EUR",
            calibration.Constant("eurToBrl"),
            (int)calibration.Constant("wageFloorBrl"),
            MeasureMix(squad),
            NationalityMixSource: null);
    }

    /// <summary>Shares of the nationalities actually present, most common first. Rounded to four
    /// places so the table reads as percentages rather than as floating-point noise.</summary>
    public static IReadOnlyList<NationalityShare> MeasureMix(IReadOnlyList<CharacterRecord> players)
    {
        if (players.Count == 0)
            return [];

        return players
            .GroupBy(player => player.Nationality)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new NationalityShare(
                group.Key,
                Math.Round((double)group.Count() / players.Count, 4)))
            .ToList();
    }

    /// <summary>
    /// Each country code's display name, taken from the geo tree.
    ///
    /// <para>The ISO code ("BRA") and the geo node ("geo_bra") are different names for the same
    /// place, and nothing links them directly — deliberately, since the code comes from the batch
    /// and the node from the tree (sql/0014_world_countries.sql). A club knows both, so the link
    /// goes through the clubs: walk up from a club's node until a Country is reached.</para>
    ///
    /// <para>A country with no clubs yet is therefore absent from the result, which is correct —
    /// nothing in the world says what it is called.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> NamesFrom(WorldSnapshot world)
    {
        Dictionary<string, GeoNode> nodes = world.GeoNodes.ToDictionary(node => node.GeoNodeId);
        var names = new Dictionary<string, string>();

        foreach (ClubIdentity club in world.Clubs)
        {
            if (names.ContainsKey(club.Geography.CountryId))
                continue;

            GeoNode? node = nodes.GetValueOrDefault(club.Geography.GeoNodeId);
            while (node is not null && node.Kind != GeoNodeKind.Country)
                node = node.ParentId is null ? null : nodes.GetValueOrDefault(node.ParentId);

            if (node is not null)
                names[club.Geography.CountryId] = node.DisplayName;
        }

        return names;
    }
}
