using System.Linq.Expressions;

namespace Magic.IndexedDb;

public interface IMagicQueryStaging<T> : IMagicExecute<T> where T : class
{
    /// <summary>
    /// The order you apply does get applied correctly in the query, 
    /// but the returned results will not be in the same order. 
    /// If order matters, you must apply the order again on return. 
    /// This is a fundemental limitation of IndexDB. 
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate);
    IMagicQueryPaginationTake<T> Take(int amount);
    IMagicQueryFinal<T> TakeLast(int amount);
    IMagicQueryFinal<T> Skip(int amount);

    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();
}