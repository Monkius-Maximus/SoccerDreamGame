using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Clubs and their 1:1 deviation audit.
///
/// <para>
/// Every write runs the club through <see cref="WorldDerivations.Recalculate(ClubIdentity, WorldCalibration)"/>
/// first. That is deliberately here, at the port boundary, rather than in a service the caller
/// could forget to use: it makes persisting a stale ΔE, luminance, polarity rule or home-advantage
/// modifier impossible through this repository, whatever the caller passed in (ROADMAP.md Sprint 2,
/// "Nunca aceitar derivado vindo do cliente").
/// </para>
/// </summary>
internal sealed class ClubRepository : SqliteRepositoryBase, IClubRepository
{
    private const string SelectColumns =
        @"ClubId, DisplayCode, OfficialName, ShortName, Nickname, FoundingYear,
          CityName, Uf, CountryId, GeoNodeId, DistrictArchetype,
          PrestigeBand, ClubStrength, SquadSize, NamingRule,
          ShieldShape, CentralCharge, Motto,
          PalettePrimary, PaletteSecondary, PaletteTertiary, TypographyStyle,
          CollarStyle, FitStyle,
          HomePattern, HomeShirt, HomeShorts, HomeSocks, HomeLuminance,
          AwayPattern, AwayShirt, AwayShorts, AwaySocks,
          DeltaE, DeltaEThreshold, PolarityRule,
          StadiumName, StadiumCapacity, AtmosphereArchetype, PitchSurface,
          DefaultTacticalStyle, TacticalStyleProvenance, HomeAdvantageModifier, DerbyRivalClubId";

    private const string AuditColumns =
        @"ClubId, AnchorClubName, AnchorCityName, AnchorFoundingYear, GeneratedFoundingYear,
          FoundingDecadePreserved, FoundingSourceCitation, AnchorNickname, NicknameCommercialLevel,
          NicknameTrademarked, NicknameEvidence, NamingRule, NamingRuleReason, PhoneticSimilarity,
          CrestOriginalChargeReplaced, CrestSubstituteCharge, CrestSourceCitation, DistrictSourceCitation,
          TacticalStyleProvenance, TacticalStyleEvidence, ChromaticPolicy, AnchorFactsVerified,
          ReviewedBy, ReviewDate, Note";

    private readonly Func<WorldCalibration> _calibration;

    public ClubRepository(
        SqliteConnection connection,
        Func<SqliteTransaction?> transactionAccessor,
        Func<WorldCalibration> calibration)
        : base(connection, transactionAccessor)
        => _calibration = calibration;

    public Task<ClubIdentity?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ClubRow? row = null;
        using (SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Clubs WHERE ClubId = $id;"))
        {
            command.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
                row = ReadRow(reader);
        }

        return Task.FromResult(row is null ? null : row.Value.ToClub(LoadAudit(row.Value.ClubId)));
    }

    public Task<IReadOnlyList<ClubIdentity>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(filter: string.Empty, bind: null));
    }

    public Task<IReadOnlyList<ClubIdentity>> ListByGeoNodeAsync(string geoNodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            filter: "WHERE GeoNodeId = $gid",
            bind: command => command.Parameters.AddWithValue("$gid", geoNodeId)));
    }

    public Task<string> AddAsync(ClubIdentity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClubIdentity club = WorldDerivations.Recalculate(entity, _calibration());

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Clubs (ClubId, DisplayCode, OfficialName, ShortName, Nickname, FoundingYear,
                  CityName, Uf, CountryId, GeoNodeId, DistrictArchetype,
                  PrestigeBand, ClubStrength, SquadSize, NamingRule,
                  ShieldShape, CentralCharge, Motto,
                  PalettePrimary, PaletteSecondary, PaletteTertiary, TypographyStyle,
                  CollarStyle, FitStyle,
                  HomePattern, HomeShirt, HomeShorts, HomeSocks, HomeLuminance,
                  AwayPattern, AwayShirt, AwayShorts, AwaySocks,
                  DeltaE, DeltaEThreshold, PolarityRule,
                  StadiumName, StadiumCapacity, AtmosphereArchetype, PitchSurface,
                  DefaultTacticalStyle, TacticalStyleProvenance, HomeAdvantageModifier, DerbyRivalClubId)
              VALUES ($id, $code, $official, $short, $nick, $founded,
                  $city, $uf, $country, $geo, $district,
                  $band, $strength, $squad, $naming,
                  $shield, $charge, $motto,
                  $p1, $p2, $p3, $typography,
                  $collar, $fit,
                  $homePattern, $homeShirt, $homeShorts, $homeSocks, $luminance,
                  $awayPattern, $awayShirt, $awayShorts, $awaySocks,
                  $deltaE, $deltaEThreshold, $polarity,
                  $stadium, $capacity, $atmosphere, $pitch,
                  $tactical, $tacticalProv, $homeAdv, $rival);"))
        {
            Bind(command, club);
            command.ExecuteNonQuery();
        }

        WriteAudit(club.Audit, insert: true);
        return Task.FromResult(club.ClubId);
    }

    public Task UpdateAsync(ClubIdentity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClubIdentity club = WorldDerivations.Recalculate(entity, _calibration());

        using (SqliteCommand command = CreateCommand(
            @"UPDATE Clubs SET DisplayCode = $code, OfficialName = $official, ShortName = $short,
                  Nickname = $nick, FoundingYear = $founded,
                  CityName = $city, Uf = $uf, CountryId = $country, GeoNodeId = $geo, DistrictArchetype = $district,
                  PrestigeBand = $band, ClubStrength = $strength, SquadSize = $squad, NamingRule = $naming,
                  ShieldShape = $shield, CentralCharge = $charge, Motto = $motto,
                  PalettePrimary = $p1, PaletteSecondary = $p2, PaletteTertiary = $p3, TypographyStyle = $typography,
                  CollarStyle = $collar, FitStyle = $fit,
                  HomePattern = $homePattern, HomeShirt = $homeShirt, HomeShorts = $homeShorts,
                  HomeSocks = $homeSocks, HomeLuminance = $luminance,
                  AwayPattern = $awayPattern, AwayShirt = $awayShirt, AwayShorts = $awayShorts, AwaySocks = $awaySocks,
                  DeltaE = $deltaE, DeltaEThreshold = $deltaEThreshold, PolarityRule = $polarity,
                  StadiumName = $stadium, StadiumCapacity = $capacity,
                  AtmosphereArchetype = $atmosphere, PitchSurface = $pitch,
                  DefaultTacticalStyle = $tactical, TacticalStyleProvenance = $tacticalProv,
                  HomeAdvantageModifier = $homeAdv, DerbyRivalClubId = $rival
              WHERE ClubId = $id;"))
        {
            Bind(command, club);
            command.ExecuteNonQuery();
        }

        WriteAudit(club.Audit, insert: false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Clubs WHERE ClubId = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <summary>Two queries — the clubs, then all their audit rows — rather than one audit query
    /// per club. The audit reuses the caller's filter as a subquery, so the query count does not
    /// grow with the result size.</summary>
    private IReadOnlyList<ClubIdentity> Query(string filter, Action<SqliteCommand>? bind)
    {
        var rows = new List<ClubRow>();
        using (SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Clubs {filter} ORDER BY ClubId;"))
        {
            bind?.Invoke(command);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(ReadRow(reader));
        }

        if (rows.Count == 0)
            return [];

        Dictionary<string, ClubDeviationAudit> audits = LoadAuditsFor($"(SELECT ClubId FROM Clubs {filter})", bind);

        var clubs = new List<ClubIdentity>(rows.Count);
        foreach (ClubRow row in rows)
        {
            if (!audits.TryGetValue(row.ClubId, out ClubDeviationAudit? audit))
                throw new InvalidOperationException($"Club '{row.ClubId}' has no deviation audit row; the two are written together.");
            clubs.Add(row.ToClub(audit));
        }

        return clubs;
    }

    private Dictionary<string, ClubDeviationAudit> LoadAuditsFor(string matching, Action<SqliteCommand>? bind)
    {
        var byClub = new Dictionary<string, ClubDeviationAudit>();
        using SqliteCommand command = CreateCommand(
            $"SELECT {AuditColumns} FROM ClubDeviationAudit WHERE ClubId IN {matching};");
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            byClub[reader.GetString(0)] = MapAudit(reader);
        return byClub;
    }

    private ClubDeviationAudit LoadAudit(string clubId)
    {
        using SqliteCommand command = CreateCommand($"SELECT {AuditColumns} FROM ClubDeviationAudit WHERE ClubId = $id;");
        command.Parameters.AddWithValue("$id", clubId);
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
            throw new InvalidOperationException($"Club '{clubId}' has no deviation audit row; the two are written together.");

        return MapAudit(reader);
    }

    private static ClubDeviationAudit MapAudit(SqliteDataReader reader) =>
        new(
            ClubId: reader.GetString(0),
            AnchorClubName: reader.GetString(1),
            AnchorCityName: reader.GetString(2),
            AnchorFoundingYear: reader.GetInt32(3),
            GeneratedFoundingYear: reader.GetInt32(4),
            FoundingDecadePreserved: reader.GetInt32(5),
            FoundingSourceCitation: reader.GetString(6),
            AnchorNickname: reader.GetString(7),
            NicknameCommercialLevel: reader.GetInt32(8),
            NicknameTrademarked: WorldRow.Flag(reader, 9),
            NicknameEvidence: reader.GetString(10),
            NamingRule: WorldRow.Enum<NamingRule>(reader, 11),
            NamingRuleReason: reader.GetString(12),
            PhoneticSimilarity: WorldRow.NullableDouble(reader, 13),
            CrestOriginalChargeReplaced: reader.GetString(14),
            CrestSubstituteCharge: reader.GetString(15),
            CrestSourceCitation: reader.GetString(16),
            DistrictSourceCitation: reader.GetString(17),
            TacticalStyleProvenance: WorldRow.Enum<TacticalStyleProvenance>(reader, 18),
            TacticalStyleEvidence: reader.GetString(19),
            ChromaticPolicy: reader.GetString(20),
            AnchorFactsVerified: WorldRow.Flag(reader, 21),
            ReviewedBy: reader.GetString(22),
            ReviewDate: reader.GetString(23),
            Note: WorldRow.NullableString(reader, 24));

    private void WriteAudit(ClubDeviationAudit audit, bool insert)
    {
        string sql = insert
            ? @"INSERT INTO ClubDeviationAudit (ClubId, AnchorClubName, AnchorCityName, AnchorFoundingYear,
                    GeneratedFoundingYear, FoundingDecadePreserved, FoundingSourceCitation, AnchorNickname,
                    NicknameCommercialLevel, NicknameTrademarked, NicknameEvidence, NamingRule, NamingRuleReason,
                    PhoneticSimilarity, CrestOriginalChargeReplaced, CrestSubstituteCharge, CrestSourceCitation,
                    DistrictSourceCitation, TacticalStyleProvenance, TacticalStyleEvidence, ChromaticPolicy,
                    AnchorFactsVerified, ReviewedBy, ReviewDate, Note)
                VALUES ($id, $anchorName, $anchorCity, $anchorYear, $generatedYear, $decade, $foundingCite,
                    $anchorNick, $commercial, $trademarked, $nickEvidence, $naming, $namingReason, $phonetic,
                    $chargeReplaced, $substituteCharge, $crestCite, $districtCite, $tacticalProv, $tacticalEvidence,
                    $chromatic, $verified, $reviewer, $reviewDate, $note);"
            : @"UPDATE ClubDeviationAudit SET AnchorClubName = $anchorName, AnchorCityName = $anchorCity,
                    AnchorFoundingYear = $anchorYear, GeneratedFoundingYear = $generatedYear,
                    FoundingDecadePreserved = $decade, FoundingSourceCitation = $foundingCite,
                    AnchorNickname = $anchorNick, NicknameCommercialLevel = $commercial,
                    NicknameTrademarked = $trademarked, NicknameEvidence = $nickEvidence,
                    NamingRule = $naming, NamingRuleReason = $namingReason, PhoneticSimilarity = $phonetic,
                    CrestOriginalChargeReplaced = $chargeReplaced, CrestSubstituteCharge = $substituteCharge,
                    CrestSourceCitation = $crestCite, DistrictSourceCitation = $districtCite,
                    TacticalStyleProvenance = $tacticalProv, TacticalStyleEvidence = $tacticalEvidence,
                    ChromaticPolicy = $chromatic, AnchorFactsVerified = $verified, ReviewedBy = $reviewer,
                    ReviewDate = $reviewDate, Note = $note
                WHERE ClubId = $id;";

        using SqliteCommand command = CreateCommand(sql);
        command.Parameters.AddWithValue("$id", audit.ClubId);
        command.Parameters.AddWithValue("$anchorName", audit.AnchorClubName);
        command.Parameters.AddWithValue("$anchorCity", audit.AnchorCityName);
        command.Parameters.AddWithValue("$anchorYear", audit.AnchorFoundingYear);
        command.Parameters.AddWithValue("$generatedYear", audit.GeneratedFoundingYear);
        command.Parameters.AddWithValue("$decade", audit.FoundingDecadePreserved);
        command.Parameters.AddWithValue("$foundingCite", audit.FoundingSourceCitation);
        command.Parameters.AddWithValue("$anchorNick", audit.AnchorNickname);
        command.Parameters.AddWithValue("$commercial", audit.NicknameCommercialLevel);
        command.Parameters.AddWithValue("$trademarked", audit.NicknameTrademarked ? 1 : 0);
        command.Parameters.AddWithValue("$nickEvidence", audit.NicknameEvidence);
        command.Parameters.AddWithValue("$naming", audit.NamingRule.ToString());
        command.Parameters.AddWithValue("$namingReason", audit.NamingRuleReason);
        command.Parameters.AddWithValue("$phonetic", WorldRow.OrNull(audit.PhoneticSimilarity));
        command.Parameters.AddWithValue("$chargeReplaced", audit.CrestOriginalChargeReplaced);
        command.Parameters.AddWithValue("$substituteCharge", audit.CrestSubstituteCharge);
        command.Parameters.AddWithValue("$crestCite", audit.CrestSourceCitation);
        command.Parameters.AddWithValue("$districtCite", audit.DistrictSourceCitation);
        command.Parameters.AddWithValue("$tacticalProv", audit.TacticalStyleProvenance.ToString());
        command.Parameters.AddWithValue("$tacticalEvidence", audit.TacticalStyleEvidence);
        command.Parameters.AddWithValue("$chromatic", audit.ChromaticPolicy);
        command.Parameters.AddWithValue("$verified", audit.AnchorFactsVerified ? 1 : 0);
        command.Parameters.AddWithValue("$reviewer", audit.ReviewedBy);
        command.Parameters.AddWithValue("$reviewDate", audit.ReviewDate);
        command.Parameters.AddWithValue("$note", WorldRow.OrNull(audit.Note));
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, ClubIdentity club)
    {
        command.Parameters.AddWithValue("$id", club.ClubId);
        command.Parameters.AddWithValue("$code", club.DisplayCode);

        command.Parameters.AddWithValue("$official", club.Identity.OfficialName);
        command.Parameters.AddWithValue("$short", club.Identity.ShortName);
        command.Parameters.AddWithValue("$nick", club.Identity.Nickname);
        command.Parameters.AddWithValue("$founded", club.Identity.FoundingYear);

        command.Parameters.AddWithValue("$city", club.Geography.CityName);
        command.Parameters.AddWithValue("$uf", club.Geography.Uf);
        command.Parameters.AddWithValue("$country", club.Geography.CountryId);
        command.Parameters.AddWithValue("$geo", club.Geography.GeoNodeId);
        command.Parameters.AddWithValue("$district", club.Geography.DistrictArchetype.ToString());

        command.Parameters.AddWithValue("$band", club.World.PrestigeBand.ToString());
        command.Parameters.AddWithValue("$strength", club.World.ClubStrength);
        command.Parameters.AddWithValue("$squad", club.World.SquadSize);
        command.Parameters.AddWithValue("$naming", club.World.NamingRule.ToString());

        command.Parameters.AddWithValue("$shield", club.Crest.ShieldShape.ToString());
        command.Parameters.AddWithValue("$charge", club.Crest.CentralCharge);
        command.Parameters.AddWithValue("$motto", club.Crest.Motto);

        command.Parameters.AddWithValue("$p1", club.Palette.Primary);
        command.Parameters.AddWithValue("$p2", club.Palette.Secondary);
        command.Parameters.AddWithValue("$p3", club.Palette.Tertiary);
        command.Parameters.AddWithValue("$typography", club.Palette.TypographyStyle.ToString());

        command.Parameters.AddWithValue("$collar", club.Kits.CollarStyle.ToString());
        command.Parameters.AddWithValue("$fit", club.Kits.FitStyle.ToString());
        command.Parameters.AddWithValue("$homePattern", club.Kits.Home.FabricPattern.ToString());
        command.Parameters.AddWithValue("$homeShirt", club.Kits.Home.Shirt);
        command.Parameters.AddWithValue("$homeShorts", club.Kits.Home.Shorts);
        command.Parameters.AddWithValue("$homeSocks", club.Kits.Home.Socks);
        command.Parameters.AddWithValue("$luminance", club.Kits.Home.Luminance);
        command.Parameters.AddWithValue("$awayPattern", club.Kits.Away.FabricPattern.ToString());
        command.Parameters.AddWithValue("$awayShirt", club.Kits.Away.Shirt);
        command.Parameters.AddWithValue("$awayShorts", club.Kits.Away.Shorts);
        command.Parameters.AddWithValue("$awaySocks", club.Kits.Away.Socks);
        command.Parameters.AddWithValue("$deltaE", club.Kits.DeltaE);
        command.Parameters.AddWithValue("$deltaEThreshold", club.Kits.DeltaEThreshold);
        command.Parameters.AddWithValue("$polarity", club.Kits.PolarityRule);

        command.Parameters.AddWithValue("$stadium", club.Stadium.Name);
        command.Parameters.AddWithValue("$capacity", club.Stadium.Capacity);
        command.Parameters.AddWithValue("$atmosphere", club.Stadium.AtmosphereArchetype.ToString());
        command.Parameters.AddWithValue("$pitch", club.Stadium.PitchSurface.ToString());

        command.Parameters.AddWithValue("$tactical", club.AiProfile.DefaultTacticalStyle.ToString());
        command.Parameters.AddWithValue("$tacticalProv", club.AiProfile.TacticalStyleProvenance.ToString());
        command.Parameters.AddWithValue("$homeAdv", club.AiProfile.HomeAdvantageModifier);
        command.Parameters.AddWithValue("$rival", WorldRow.OrNull(club.AiProfile.DerbyRivalClubId));
    }

    private static ClubRow ReadRow(SqliteDataReader reader) => new(
        reader.GetString(0),
        new ClubIdentity(
            ClubId: reader.GetString(0),
            DisplayCode: reader.GetString(1),
            Identity: new ClubIdentityInfo(reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5)),
            Geography: new ClubGeography(
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                WorldRow.Enum<DistrictArchetype>(reader, 10)),
            World: new ClubWorldProfile(
                WorldRow.Enum<PrestigeBand>(reader, 11),
                reader.GetDouble(12),
                reader.GetInt32(13),
                WorldRow.Enum<NamingRule>(reader, 14)),
            Crest: new ClubCrest(
                WorldRow.Enum<ShieldShape>(reader, 15),
                reader.GetString(16),
                reader.GetString(17),
                // Mirrors the palette by definition (ALGORITHMS.md §4.3), so it is derived on
                // read instead of being stored twice and risking the two disagreeing.
                [reader.GetString(18), reader.GetString(19), reader.GetString(20)]),
            Palette: new ClubPalette(
                reader.GetString(18),
                reader.GetString(19),
                reader.GetString(20),
                WorldRow.Enum<TypographyStyle>(reader, 21)),
            Kits: new ClubKits(
                WorldRow.Enum<CollarStyle>(reader, 22),
                WorldRow.Enum<FitStyle>(reader, 23),
                Home: new HomeKit(
                    WorldRow.Enum<FabricPattern>(reader, 24),
                    reader.GetString(25),
                    reader.GetString(26),
                    reader.GetString(27),
                    reader.GetDouble(28)),
                Away: new AwayKit(
                    WorldRow.Enum<FabricPattern>(reader, 29),
                    reader.GetString(30),
                    reader.GetString(31),
                    reader.GetString(32)),
                DeltaE: reader.GetDouble(33),
                DeltaEThreshold: reader.GetDouble(34),
                PolarityRule: reader.GetString(35)),
            Stadium: new ClubStadium(
                reader.GetString(36),
                reader.GetInt32(37),
                WorldRow.Enum<AtmosphereArchetype>(reader, 38),
                WorldRow.Enum<PitchSurface>(reader, 39)),
            AiProfile: new ClubAiProfile(
                WorldRow.Enum<TacticalStyle>(reader, 40),
                WorldRow.Enum<TacticalStyleProvenance>(reader, 41),
                reader.GetDouble(42),
                WorldRow.NullableString(reader, 43)),
            // Filled in by LoadAudit once the reader is closed.
            Audit: null!));

    /// <summary>A club read from the Clubs table, still missing its audit row.</summary>
    private readonly record struct ClubRow(string ClubId, ClubIdentity Club)
    {
        public ClubIdentity ToClub(ClubDeviationAudit audit) => Club with { Audit = audit };
    }
}
