using SoccerSim.Core.Domain;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.Persistence;

public interface IPlayerRepository : IRepository<Player, int>
{
    Task<IReadOnlyList<Player>> ListByTeamAsync(int teamId, CancellationToken cancellationToken = default);
}

public interface ITeamRepository : IRepository<Team, int>
{
    Task<IReadOnlyList<Team>> ListByLeagueAsync(int leagueId, CancellationToken cancellationToken = default);
}

public interface ILeagueRepository : IRepository<League, int>
{
    Task<IReadOnlyList<League>> ListByTierAsync(SimulationTier tier, CancellationToken cancellationToken = default);
}

public interface IPlayerTraitRepository : IRepository<PlayerTrait, int>
{
    Task<PlayerTrait?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Match access for the LOD background simulation. Reads fixtures and bulk-writes
/// results purely on rows, without loading any rendering assets into RAM (GDD §7).
/// </summary>
public interface IMatchRepository : IRepository<Match, int>
{
    Task<IReadOnlyList<Match>> ListFixturesAsync(int leagueId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task BulkInsertResultsAsync(IEnumerable<MatchResult> results, CancellationToken cancellationToken = default);
}

/// <summary>Standings use a composite (Season, Team) key, so they get a bespoke contract.</summary>
public interface IStandingRepository
{
    Task<Standing?> GetAsync(int seasonId, int teamId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Standing>> GetTableAsync(int seasonId, CancellationToken cancellationToken = default);

    Task UpsertAsync(Standing standing, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transaction boundary that exposes the repositories over a single connection.
/// Swapping SQLite for Postgres/Turso later means providing a new implementation
/// of this and the repositories — the core and Godot layers are untouched.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IPlayerRepository Players { get; }
    ITeamRepository Teams { get; }
    ILeagueRepository Leagues { get; }
    IPlayerTraitRepository PlayerTraits { get; }
    IMatchRepository Matches { get; }
    IStandingRepository Standings { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
