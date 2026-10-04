using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Competitions;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class CountryRepository : SqliteRepositoryBase, ICountryRepository
{
    public CountryRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<IReadOnlyList<CountryProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(countryId: null));
    }

    public Task<CountryProfile?> GetAsync(string countryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(countryId).FirstOrDefault());
    }

    public Task SaveAsync(CountryProfile country, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Countries (CountryId, Currency, EurToLocal, WageFloorMonthly, NationalityMixSource)
              VALUES ($id, $currency, $rate, $floor, $source)
              ON CONFLICT (CountryId) DO UPDATE SET
                  Currency = excluded.Currency,
                  EurToLocal = excluded.EurToLocal,
                  WageFloorMonthly = excluded.WageFloorMonthly,
                  NationalityMixSource = excluded.NationalityMixSource;"))
        {
            command.Parameters.AddWithValue("$id", country.CountryId);
            command.Parameters.AddWithValue("$currency", country.Currency);
            command.Parameters.AddWithValue("$rate", country.EurToLocal);
            command.Parameters.AddWithValue("$floor", country.WageFloorMonthly);
            command.Parameters.AddWithValue("$source", (object?)country.NationalityMixSource ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        // The mix is replaced wholesale: a share removed from the table has to disappear, and
        // merging row by row would leave the old one behind summing the table past 1.
        using (SqliteCommand clear = CreateCommand("DELETE FROM CountryNationalities WHERE CountryId = $id;"))
        {
            clear.Parameters.AddWithValue("$id", country.CountryId);
            clear.ExecuteNonQuery();
        }

        foreach (NationalityShare share in country.NationalityMix)
        {
            using SqliteCommand insert = CreateCommand(
                "INSERT INTO CountryNationalities (CountryId, Nationality, Share) VALUES ($id, $nat, $share);");
            insert.Parameters.AddWithValue("$id", country.CountryId);
            insert.Parameters.AddWithValue("$nat", share.Nationality);
            insert.Parameters.AddWithValue("$share", share.Share);
            insert.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string countryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Countries WHERE CountryId = $id;");
        command.Parameters.AddWithValue("$id", countryId);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<CountryProfile> Query(string? countryId)
    {
        var shares = new Dictionary<string, List<NationalityShare>>();

        using (SqliteCommand command = CreateCommand(
            "SELECT CountryId, Nationality, Share FROM CountryNationalities"
            + (countryId is null ? "" : " WHERE CountryId = $id")
            + " ORDER BY Share DESC, Nationality;"))
        {
            if (countryId is not null)
                command.Parameters.AddWithValue("$id", countryId);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                if (!shares.TryGetValue(id, out List<NationalityShare>? list))
                    shares[id] = list = [];
                list.Add(new NationalityShare(reader.GetString(1), reader.GetDouble(2)));
            }
        }

        var countries = new List<CountryProfile>();

        using (SqliteCommand command = CreateCommand(
            "SELECT CountryId, Currency, EurToLocal, WageFloorMonthly, NationalityMixSource FROM Countries"
            + (countryId is null ? "" : " WHERE CountryId = $id")
            + " ORDER BY CountryId;"))
        {
            if (countryId is not null)
                command.Parameters.AddWithValue("$id", countryId);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                countries.Add(new CountryProfile(
                    id,
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetInt32(3),
                    shares.TryGetValue(id, out List<NationalityShare>? mix) ? mix : [],
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        return countries;
    }
}
