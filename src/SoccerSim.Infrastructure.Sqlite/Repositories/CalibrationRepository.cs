using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Reads and writes the calibration as one aggregate across its six tables. Saving replaces the
/// whole set: a half-applied re-fit (new pivot, old doubling step) would silently produce values
/// that match neither calibration.
/// </summary>
internal sealed class CalibrationRepository : SqliteRepositoryBase, ICalibrationRepository
{
    private readonly Action _onSaved;

    /// <param name="onSaved">Invalidates the unit of work's cached calibration, so club and
    /// character writes in the same transaction recalculate against the new numbers rather than
    /// the ones loaded before the re-fit.</param>
    public CalibrationRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor, Action onSaved)
        : base(connection, transactionAccessor)
        => _onSaved = onSaved;

    public Task<WorldCalibration?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var constants = new Dictionary<string, CalibrationConstant>();
        Read("SELECT Key, Value, Unit, Note FROM CalibrationConstants;", reader =>
            constants[reader.GetString(0)] = new CalibrationConstant(
                reader.GetDouble(1), reader.GetString(2), reader.GetString(3)));

        if (constants.Count == 0)
            return Task.FromResult<WorldCalibration?>(null);

        var ageMult = new List<AgeMultStep>();
        Read("SELECT Age, Multiplier FROM CalibrationAgeMultipliers ORDER BY Age;", reader =>
            ageMult.Add(new AgeMultStep(reader.GetInt32(0), reader.GetDouble(1))));

        var bands = new Dictionary<PrestigeBand, PrestigeBandCalibration>();
        Read("SELECT Band, ValueMult, CapMean, CapSd, N FROM CalibrationPrestigeBands;", reader =>
            bands[WorldRow.Enum<PrestigeBand>(reader, 0)] = new PrestigeBandCalibration(
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetInt32(4)));

        var homeAdv = new Dictionary<AtmosphereArchetype, double>();
        Read("SELECT AtmosphereArchetype, Modifier FROM CalibrationHomeAdvantage;", reader =>
            homeAdv[WorldRow.Enum<AtmosphereArchetype>(reader, 0)] = reader.GetDouble(1));

        var stadiumProfiles = new Dictionary<string, StadiumProfileEntry>();
        Read("SELECT CountryId, Mean, Sd, MinValue, MaxValue FROM CalibrationStadiumProfiles;", reader =>
            stadiumProfiles[reader.GetString(0)] = new StadiumProfileEntry(
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));

        var weights = new Dictionary<Position, Dictionary<Attr, double>>();
        Read("SELECT Position, Attr, Weight FROM PositionWeights;", reader =>
        {
            var position = WorldRow.Enum<Position>(reader, 0);
            if (!weights.TryGetValue(position, out Dictionary<Attr, double>? row))
                weights[position] = row = [];
            row[WorldRow.Enum<Attr>(reader, 1)] = reader.GetDouble(2);
        });

        var positionWeights = weights.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<Attr, double>)entry.Value);

        return Task.FromResult<WorldCalibration?>(new WorldCalibration(
            constants, ageMult, bands, homeAdv, stadiumProfiles, positionWeights));
    }

    public Task SaveAsync(WorldCalibration calibration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string table in Tables)
            Execute($"DELETE FROM {table};", bind: null);

        foreach ((string key, CalibrationConstant constant) in calibration.Constants)
        {
            Execute("INSERT INTO CalibrationConstants (Key, Value, Unit, Note) VALUES ($k, $v, $u, $n);", command =>
            {
                command.Parameters.AddWithValue("$k", key);
                command.Parameters.AddWithValue("$v", constant.Value);
                command.Parameters.AddWithValue("$u", constant.Unit);
                command.Parameters.AddWithValue("$n", constant.Note);
            });
        }

        foreach (AgeMultStep step in calibration.AgeMult)
        {
            Execute("INSERT INTO CalibrationAgeMultipliers (Age, Multiplier) VALUES ($a, $m);", command =>
            {
                command.Parameters.AddWithValue("$a", step.Age);
                command.Parameters.AddWithValue("$m", step.Multiplier);
            });
        }

        foreach ((PrestigeBand band, PrestigeBandCalibration entry) in calibration.Bands)
        {
            Execute(
                "INSERT INTO CalibrationPrestigeBands (Band, ValueMult, CapMean, CapSd, N) VALUES ($b, $v, $m, $s, $n);",
                command =>
                {
                    command.Parameters.AddWithValue("$b", band.ToString());
                    command.Parameters.AddWithValue("$v", entry.ValueMult);
                    command.Parameters.AddWithValue("$m", entry.CapMean);
                    command.Parameters.AddWithValue("$s", entry.CapSd);
                    command.Parameters.AddWithValue("$n", entry.N);
                });
        }

        foreach ((AtmosphereArchetype atmosphere, double modifier) in calibration.HomeAdv)
        {
            Execute("INSERT INTO CalibrationHomeAdvantage (AtmosphereArchetype, Modifier) VALUES ($a, $m);", command =>
            {
                command.Parameters.AddWithValue("$a", atmosphere.ToString());
                command.Parameters.AddWithValue("$m", modifier);
            });
        }

        foreach ((string countryId, StadiumProfileEntry profile) in calibration.StadiumProfile)
        {
            Execute(
                @"INSERT INTO CalibrationStadiumProfiles (CountryId, Mean, Sd, MinValue, MaxValue)
                  VALUES ($c, $mean, $sd, $min, $max);",
                command =>
                {
                    command.Parameters.AddWithValue("$c", countryId);
                    command.Parameters.AddWithValue("$mean", profile.Mean);
                    command.Parameters.AddWithValue("$sd", profile.Sd);
                    command.Parameters.AddWithValue("$min", profile.Min);
                    command.Parameters.AddWithValue("$max", profile.Max);
                });
        }

        foreach ((Position position, IReadOnlyDictionary<Attr, double> row) in calibration.PositionWeights)
        {
            foreach ((Attr attr, double weight) in row)
            {
                Execute("INSERT INTO PositionWeights (Position, Attr, Weight) VALUES ($p, $a, $w);", command =>
                {
                    command.Parameters.AddWithValue("$p", position.ToString());
                    command.Parameters.AddWithValue("$a", attr.ToString());
                    command.Parameters.AddWithValue("$w", weight);
                });
            }
        }

        _onSaved();
        return Task.CompletedTask;
    }

    private static readonly string[] Tables =
    [
        "CalibrationConstants",
        "CalibrationAgeMultipliers",
        "CalibrationPrestigeBands",
        "CalibrationHomeAdvantage",
        "CalibrationStadiumProfiles",
        "PositionWeights",
    ];

    private void Read(string sql, Action<SqliteDataReader> onRow)
    {
        using SqliteCommand command = CreateCommand(sql);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            onRow(reader);
    }

    private void Execute(string sql, Action<SqliteCommand>? bind)
    {
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        command.ExecuteNonQuery();
    }
}
