namespace SoccerSim.Core.Persistence;

/// <summary>Generic async CRUD contract over an aggregate with a single-column key.</summary>
public interface IRepository<T, TKey>
    where T : class
{
    Task<T?> GetAsync(TKey id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default);

    Task<TKey> AddAsync(T entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}
