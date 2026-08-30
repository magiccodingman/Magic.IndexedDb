using System.Linq.Expressions;

namespace Magic.IndexedDb;

public interface IMagicQueryStaging<T> : IMagicExecute<T> where T : class
{
    /// <summary>
    /// Adds another predicate to the staged query. Materialized results returned by
    /// <see cref="IMagicExecute{T}.ToListAsync"/> preserve the resulting query order.
    /// </summary>
    /// <param name="predicate">Predicate to apply.</param>
    /// <returns>The staged query.</returns>
    IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate);

    IMagicQueryPaginationTake<T> Take(int amount);
    IMagicQueryFinal<T> TakeLast(int amount);
    IMagicQueryFinal<T> Skip(int amount);

    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();
}
