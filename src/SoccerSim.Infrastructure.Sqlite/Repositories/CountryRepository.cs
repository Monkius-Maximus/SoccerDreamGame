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

internal sealed class DivisionRepository : SqliteRepositoryBase, IDivisionRepository
{
    private const string SelectColumns =
        "DivisionId, CountryId, Tier, Name, Format, ClubCount, PromotedIn, RelegatedOut";

    public DivisionRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<LeaguePyramid> GetPyramidAsync(string countryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LeaguePyramid(countryId, Query(countryId)));
    }

    public Task<IReadOnlyList<Division>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(countryId: null));
    }

    public Task SaveAsync(string countryId, Division division, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Divisions (DivisionId, CountryId, Tier, Name, Format, ClubCount, PromotedIn, RelegatedOut)
              VALUES ($id, $country, $tier, $name, $format, $clubs, $up, $down)
              ON CONFLICT (DivisionId) DO UPDATE SET
                  CountryId = excluded.CountryId,
                  Tier = excluded.Tier,
                  Name = excluded.Name,
                  Format = excluded.Format,
                  ClubCount = excluded.ClubCount,
                  PromotedIn = excluded.PromotedIn,
                  RelegatedOut = excluded.RelegatedOut;"))
        {
            command.Parameters.AddWithValue("$id", division.DivisionId);
            command.Parameters.AddWithValue("$country", countryId);
            command.Parameters.AddWithValue("$tier", division.Tier);
            command.Parameters.AddWithValue("$name", division.Name);
            command.Parameters.AddWithValue("$format", division.Format.ToString());
            command.Parameters.AddWithValue("$clubs", division.ClubCount);
            command.Parameters.AddWithValue("$up", division.PromotedIn);
            command.Parameters.AddWithValue("$down", division.RelegatedOut);
            command.ExecuteNonQuery();
        }

        using (SqliteCommand clear = CreateCommand("DELETE FROM DivisionClubs WHERE DivisionId = $id;"))
        {
            clear.Parameters.AddWithValue("$id", division.DivisionId);
            clear.ExecuteNonQuery();
        }

        for (int ordinal = 0; ordinal < division.ClubIds.Count; ordinal++)
        {
            using SqliteCommand insert = CreateCommand(
                "INSERT INTO DivisionClubs (DivisionId, ClubId, Ordinal) VALUES ($id, $club, $ordinal);");
            insert.Parameters.AddWithValue("$id", division.DivisionId);
            insert.Parameters.AddWithValue("$club", division.ClubIds[ordinal]);
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string divisionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Divisions WHERE DivisionId = $id;");
        command.Parameters.AddWithValue("$id", divisionId);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<Division> Query(string? countryId)
    {
        var members = new Dictionary<string, List<string>>();

        using (SqliteCommand command = CreateCommand(
            @"SELECT dc.DivisionId, dc.ClubId FROM DivisionClubs dc
              JOIN Divisions d ON d.DivisionId = dc.DivisionId"
            + (countryId is null ? "" : " WHERE d.CountryId = $country")
            + " ORDER BY dc.DivisionId, dc.Ordinal;"))
        {
            if (countryId is not null)
                command.Parameters.AddWithValue("$country", countryId);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                if (!members.TryGetValue(id, out List<string>? list))
                    members[id] = list = [];
                list.Add(reader.GetString(1));
            }
        }

        var divisions = new List<Division>();

        using (SqliteCommand command = CreateCommand(
            $"SELECT {SelectColumns} FROM Divisions"
            + (countryId is null ? "" : " WHERE CountryId = $country")
            + " ORDER BY CountryId, Tier;"))
        {
            if (countryId is not null)
                command.Parameters.AddWithValue("$country", countryId);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                divisions.Add(new Division(
                    Tier: reader.GetInt32(2),
                    DivisionId: id,
                    Name: reader.GetString(3),
                    Format: Enum.Parse<CompetitionFormat>(reader.GetString(4)),
                    ClubCount: reader.GetInt32(5),
                    PromotedIn: reader.GetInt32(6),
                    RelegatedOut: reader.GetInt32(7),
                    ClubIds: members.TryGetValue(id, out List<string>? clubs) ? clubs : []));
            }
        }

        return divisions;
    }
}
