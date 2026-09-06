using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Characters, their 12 attribute rows and their 1:1 deviation audit.
///
/// <para>
/// Every write recalculates shirt name, overall and the economy chain from the attributes, the
/// stored age and the prestige band of the character's club — the band is read from the database,
/// not taken from the caller, so a client cannot inflate a player's value by claiming a different
/// club's band.
/// </para>
/// </summary>
internal sealed class CharacterRepository : SqliteRepositoryBase, ICharacterRepository
{
    private const string SelectColumns =
        @"PlayerId, ClubId, ShirtNumber, FirstName, LastName, ShirtName, Nationality, SecondNationality,
          DateOfBirth, Age, Phase, SquadRole, PrimaryPosition, SecondaryPositions, PreferredFoot,
          WeakFootRating, SkillMovesRating, Height, BuildType, PotentialGap, Provenance,
          Overall, PotentialOverall, MarketValueEur, SalaryMonthlyBrl";

    private readonly Func<WorldCalibration> _calibration;

    public CharacterRepository(
        SqliteConnection connection,
        Func<SqliteTransaction?> transactionAccessor,
        Func<WorldCalibration> calibration)
        : base(connection, transactionAccessor)
        => _calibration = calibration;

    public Task<CharacterRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CharacterRecord? shell = null;
        using (SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Characters WHERE PlayerId = $id;"))
        {
            command.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
                shell = Map(reader);
        }

        return Task.FromResult(shell is null ? null : Complete(shell));
    }

    public Task<IReadOnlyList<CharacterRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM Characters ORDER BY PlayerId;", bind: null));
    }

    public Task<IReadOnlyList<CharacterRecord>> ListByClubAsync(string clubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            $"SELECT {SelectColumns} FROM Characters WHERE ClubId = $cid ORDER BY ShirtNumber;",
            command => command.Parameters.AddWithValue("$cid", clubId)));
    }

    public Task<string> AddAsync(CharacterRecord entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CharacterRecord character = Recalculate(entity);

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Characters (PlayerId, ClubId, ShirtNumber, FirstName, LastName, ShirtName,
                  Nationality, SecondNationality, DateOfBirth, Age, Phase, SquadRole, PrimaryPosition,
                  SecondaryPositions, PreferredFoot, WeakFootRating, SkillMovesRating, Height, BuildType,
                  PotentialGap, Provenance, Overall, PotentialOverall, MarketValueEur, SalaryMonthlyBrl)
              VALUES ($id, $club, $shirt, $first, $last, $shirtName, $nat, $nat2, $dob, $age, $phase,
                  $role, $position, $secondary, $foot, $weakFoot, $skillMoves, $height, $build,
                  $gap, $provenance, $overall, $potential, $value, $salary);"))
        {
            Bind(command, character);
            command.ExecuteNonQuery();
        }

        WriteAttributes(character);
        WriteAudit(character, insert: true);
        return Task.FromResult(character.PlayerId);
    }

    public Task UpdateAsync(CharacterRecord entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CharacterRecord character = Recalculate(entity);

        using (SqliteCommand command = CreateCommand(
            @"UPDATE Characters SET ClubId = $club, ShirtNumber = $shirt, FirstName = $first, LastName = $last,
                  ShirtName = $shirtName, Nationality = $nat, SecondNationality = $nat2, DateOfBirth = $dob,
                  Age = $age, Phase = $phase, SquadRole = $role, PrimaryPosition = $position,
                  SecondaryPositions = $secondary, PreferredFoot = $foot, WeakFootRating = $weakFoot,
                  SkillMovesRating = $skillMoves, Height = $height, BuildType = $build, PotentialGap = $gap,
                  Provenance = $provenance, Overall = $overall, PotentialOverall = $potential,
                  MarketValueEur = $value, SalaryMonthlyBrl = $salary
              WHERE PlayerId = $id;"))
        {
            Bind(command, character);
            command.ExecuteNonQuery();
        }

        WriteAttributes(character);
        WriteAudit(character, insert: false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Characters WHERE PlayerId = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <summary>Recomputes the derived fields against the band of the club the character actually
    /// belongs to, read from the database.</summary>
    private CharacterRecord Recalculate(CharacterRecord character)
    {
        using SqliteCommand command = CreateCommand("SELECT PrestigeBand FROM Clubs WHERE ClubId = $id;");
        command.Parameters.AddWithValue("$id", character.ClubId);
        if (command.ExecuteScalar() is not string bandText)
        {
            throw new InvalidOperationException(
                $"Character '{character.PlayerId}' references club '{character.ClubId}', which is not in the " +
                "database. Market value cannot be computed without the club's prestige band.");
        }

        return WorldDerivations.Recalculate(character, Enum.Parse<PrestigeBand>(bandText), _calibration());
    }

    private IReadOnlyList<CharacterRecord> Query(string sql, Action<SqliteCommand>? bind)
    {
        var shells = new List<CharacterRecord>();
        using (SqliteCommand command = CreateCommand(sql))
        {
            bind?.Invoke(command);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                shells.Add(Map(reader));
        }

        // Same shape as the legacy PlayerRepository: the reader must close before the per-record
        // attribute/audit queries can reuse the connection.
        var characters = new List<CharacterRecord>(shells.Count);
        foreach (CharacterRecord shell in shells)
            characters.Add(Complete(shell));
        return characters;
    }

    private CharacterRecord Complete(CharacterRecord shell) =>
        shell with { Attrs = LoadAttributes(shell.PlayerId), Audit = LoadAudit(shell.PlayerId) };

    private IReadOnlyDictionary<Attr, int> LoadAttributes(string playerId)
    {
        var attrs = new Dictionary<Attr, int>();
        using SqliteCommand command = CreateCommand(
            "SELECT Attr, Value FROM CharacterAttributes WHERE PlayerId = $id;");
        command.Parameters.AddWithValue("$id", playerId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            attrs[WorldRow.Enum<Attr>(reader, 0)] = reader.GetInt32(1);
        return attrs;
    }

    private CharacterDeviationAudit LoadAudit(string playerId)
    {
        using SqliteCommand command = CreateCommand(
            @"SELECT AnchorPlayerName, AnchorNationality, DeviationFromSurname, GeneratedSurname,
                     PhoneticSimilarity, DeviationMethod, AnchorFactsVerified
              FROM CharacterDeviationAudit WHERE PlayerId = $id;");
        command.Parameters.AddWithValue("$id", playerId);
        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
            throw new InvalidOperationException($"Character '{playerId}' has no deviation audit row; the two are written together.");

        return new CharacterDeviationAudit(
            WorldRow.NullableString(reader, 0),
            WorldRow.NullableString(reader, 1),
            WorldRow.NullableString(reader, 2),
            WorldRow.NullableString(reader, 3),
            WorldRow.NullableDouble(reader, 4),
            WorldRow.NullableString(reader, 5),
            WorldRow.Flag(reader, 6));
    }

    /// <summary>Rewrites all 12 attribute rows in one statement — 688 commands for a full import
    /// instead of 8,256.</summary>
    private void WriteAttributes(CharacterRecord character)
    {
        using (SqliteCommand clear = CreateCommand("DELETE FROM CharacterAttributes WHERE PlayerId = $id;"))
        {
            clear.Parameters.AddWithValue("$id", character.PlayerId);
            clear.ExecuteNonQuery();
        }

        var values = new List<string>(character.Attrs.Count);
        var parameters = new List<(string Name, object Value)>();
        int index = 0;
        foreach ((Attr attr, int value) in character.Attrs)
        {
            values.Add($"($id, $a{index}, $v{index})");
            parameters.Add(($"$a{index}", attr.ToString()));
            parameters.Add(($"$v{index}", value));
            index++;
        }

        using SqliteCommand command = CreateCommand(
            $"INSERT INTO CharacterAttributes (PlayerId, Attr, Value) VALUES {string.Join(", ", values)};");
        command.Parameters.AddWithValue("$id", character.PlayerId);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private void WriteAudit(CharacterRecord character, bool insert)
    {
        CharacterDeviationAudit audit = character.Audit;
        string sql = insert
            ? @"INSERT INTO CharacterDeviationAudit (PlayerId, AnchorPlayerName, AnchorNationality,
                    DeviationFromSurname, GeneratedSurname, PhoneticSimilarity, DeviationMethod, AnchorFactsVerified)
                VALUES ($id, $anchorName, $anchorNat, $deviation, $generated, $phonetic, $method, $verified);"
            : @"UPDATE CharacterDeviationAudit SET AnchorPlayerName = $anchorName, AnchorNationality = $anchorNat,
                    DeviationFromSurname = $deviation, GeneratedSurname = $generated, PhoneticSimilarity = $phonetic,
                    DeviationMethod = $method, AnchorFactsVerified = $verified
                WHERE PlayerId = $id;";

        using SqliteCommand command = CreateCommand(sql);
        command.Parameters.AddWithValue("$id", character.PlayerId);
        command.Parameters.AddWithValue("$anchorName", WorldRow.OrNull(audit.AnchorPlayerName));
        command.Parameters.AddWithValue("$anchorNat", WorldRow.OrNull(audit.AnchorNationality));
        command.Parameters.AddWithValue("$deviation", WorldRow.OrNull(audit.DeviationFromSurname));
        command.Parameters.AddWithValue("$generated", WorldRow.OrNull(audit.GeneratedSurname));
        command.Parameters.AddWithValue("$phonetic", WorldRow.OrNull(audit.PhoneticSimilarity));
        command.Parameters.AddWithValue("$method", WorldRow.OrNull(audit.DeviationMethod));
        command.Parameters.AddWithValue("$verified", audit.AnchorFactsVerified ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, CharacterRecord character)
    {
        command.Parameters.AddWithValue("$id", character.PlayerId);
        command.Parameters.AddWithValue("$club", character.ClubId);
        command.Parameters.AddWithValue("$shirt", character.ShirtNumber);
        command.Parameters.AddWithValue("$first", character.FirstName);
        command.Parameters.AddWithValue("$last", character.LastName);
        command.Parameters.AddWithValue("$shirtName", character.ShirtName);
        command.Parameters.AddWithValue("$nat", character.Nationality);
        command.Parameters.AddWithValue("$nat2", WorldRow.OrNull(character.SecondNationality));
        command.Parameters.AddWithValue("$dob", character.DateOfBirth.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$age", character.Age);
        command.Parameters.AddWithValue("$phase", character.Phase.ToString());
        command.Parameters.AddWithValue("$role", character.SquadRole.ToString());
        command.Parameters.AddWithValue("$position", character.PrimaryPosition.ToString());
        command.Parameters.AddWithValue("$secondary", WorldRow.OrNull(FormatSecondary(character.SecondaryPositions)));
        command.Parameters.AddWithValue("$foot", character.PreferredFoot.ToString());
        command.Parameters.AddWithValue("$weakFoot", character.WeakFootRating);
        command.Parameters.AddWithValue("$skillMoves", character.SkillMovesRating);
        command.Parameters.AddWithValue("$height", character.Height);
        command.Parameters.AddWithValue("$build", character.BuildType.ToString());
        command.Parameters.AddWithValue("$gap", character.PotentialGap);
        command.Parameters.AddWithValue("$provenance", character.Provenance.ToString());
        command.Parameters.AddWithValue("$overall", character.Overall);
        command.Parameters.AddWithValue("$potential", character.PotentialOverall);
        command.Parameters.AddWithValue("$value", character.MarketValueEur);
        command.Parameters.AddWithValue("$salary", character.SalaryMonthlyBrl);
    }

    /// <summary>Stored pipe-separated, the same shape the source data uses ("AM|ST").</summary>
    private static string? FormatSecondary(IReadOnlyList<Position> positions) =>
        positions.Count == 0 ? null : string.Join('|', positions);

    private static IReadOnlyList<Position> ParseSecondary(string? raw) =>
        raw is null
            ? []
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Position>).ToList();

    /// <summary>Maps the Characters row. Attributes and audit are attached by
    /// <see cref="Complete"/> once the reader is closed.</summary>
    private static CharacterRecord Map(SqliteDataReader reader) => new(
        PlayerId: reader.GetString(0),
        ClubId: reader.GetString(1),
        ShirtNumber: reader.GetInt32(2),
        FirstName: reader.GetString(3),
        LastName: reader.GetString(4),
        ShirtName: reader.GetString(5),
        Nationality: reader.GetString(6),
        SecondNationality: WorldRow.NullableString(reader, 7),
        DateOfBirth: DateOnly.ParseExact(reader.GetString(8), "yyyy-MM-dd"),
        Age: reader.GetInt32(9),
        Phase: WorldRow.Enum<Phase>(reader, 10),
        SquadRole: WorldRow.Enum<SquadRole>(reader, 11),
        PrimaryPosition: WorldRow.Enum<Position>(reader, 12),
        SecondaryPositions: ParseSecondary(WorldRow.NullableString(reader, 13)),
        PreferredFoot: WorldRow.Enum<PreferredFoot>(reader, 14),
        WeakFootRating: reader.GetInt32(15),
        SkillMovesRating: reader.GetInt32(16),
        Height: reader.GetInt32(17),
        BuildType: WorldRow.Enum<BuildType>(reader, 18),
        Attrs: new Dictionary<Attr, int>(),
        PotentialGap: reader.GetInt32(19),
        Provenance: WorldRow.Enum<Provenance>(reader, 20),
        Overall: reader.GetInt32(21),
        PotentialOverall: reader.GetInt32(22),
        MarketValueEur: reader.GetInt32(23),
        SalaryMonthlyBrl: reader.GetInt32(24),
        Audit: null!);
}
