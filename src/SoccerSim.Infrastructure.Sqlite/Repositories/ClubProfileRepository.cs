using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// One <c>club_profiles.json</c> document per country (sql/0018). The document is validated by
/// <see cref="ClubProfilesReader"/> on the way in and read through it again on the way out, so the
/// stored text and the generator's input cannot drift apart.
/// </summary>
internal sealed class ClubProfileRepository : SqliteRepositoryBase, IClubProfileRepository
{
    public ClubProfileRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<ClubProfiles?> GetAsync(string countryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("SELECT Document FROM ClubProfileDocuments WHERE CountryId = $country;");
        command.Parameters.AddWithValue("$country", countryId);

        return Task.FromResult(command.ExecuteScalar() is string document ? ClubProfilesReader.Read(document) : null);
    }

    public Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var countries = new List<string>();
        using SqliteCommand command = CreateCommand("SELECT CountryId FROM ClubProfileDocuments ORDER BY CountryId;");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            countries.Add(reader.GetString(0));

        return Task.FromResult<IReadOnlyList<string>>(countries);
    }

    public Task<ClubProfiles> SaveAsync(string document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ClubProfiles profiles = ClubProfilesReader.Read(document);

        using SqliteCommand command = CreateCommand(
            @"INSERT INTO ClubProfileDocuments (CountryId, Document) VALUES ($country, $document)
              ON CONFLICT (CountryId) DO UPDATE SET Document = excluded.Document;");
        command.Parameters.AddWithValue("$country", profiles.CountryId);
        command.Parameters.AddWithValue("$document", document);
        command.ExecuteNonQuery();

        return Task.FromResult(profiles);
    }
}
