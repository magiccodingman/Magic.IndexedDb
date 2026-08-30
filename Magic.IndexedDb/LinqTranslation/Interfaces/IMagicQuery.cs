using System.Linq.Expressions;

namespace Magic.IndexedDb;

public interface IMagicQuery<T> : IMagicExecute<T> where T : class
{
    string DatabaseName { get; }
    public string SchemaName { get; }

    /// <summary>
    /// Adds a predicate to the query pipeline. Materialized results returned by
    /// <see cref="IMagicExecute{T}.ToListAsync"/> preserve the resulting query order.
    /// </summary>
    /// <param name="predicate">Predicate to apply.</param>
    /// <returns>The staged query.</returns>
    IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate);

    IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate);

    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();

    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate);

    Task<int> CountAsync();

    IMagicQueryPaginationTake<T> Take(int amount);
    IMagicQueryFinal<T> TakeLast(int amount);
    IMagicQueryFinal<T> Skip(int amount);

    /// <summary>
    /// Orders the query ascending by the selected persisted property. When that
    /// property is not independently orderable by IndexedDB, the planner uses the
    /// cursor ordering path rather than treating compound-key membership as an index.
    /// </summary>
    /// <param name="predicate">Property to order by.</param>
    /// <returns>The ordered query.</returns>
    IMagicQueryOrderableTable<T> OrderBy(Expression<Func<T, object>> predicate);

    /// <summary>
    /// Orders the query descending by the selected persisted property. When that
    /// property is not independently orderable by IndexedDB, the planner uses the
    /// cursor ordering path rather than treating compound-key membership as an index.
    /// </summary>
    /// <param name="predicate">Property to order by.</param>
    /// <returns>The ordered query.</returns>
    IMagicQueryOrderableTable<T> OrderByDescending(Expression<Func<T, object>> predicate);

    Task AddRangeAsync(IEnumerable<T> records, CancellationToken cancellationToken = default);

    Task<int> UpdateAsync(T item, CancellationToken cancellationToken = default);

    Task<int> UpdateRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

    Task DeleteAsync(T item, CancellationToken cancellationToken = default);

    Task<int> DeleteRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

    Task AddAsync(T record, CancellationToken cancellationToken = default);
    Task ClearTable();
}
