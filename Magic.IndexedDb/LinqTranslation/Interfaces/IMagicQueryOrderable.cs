﻿using System.Linq.Expressions;

namespace Magic.IndexedDb;

/// <summary>
/// You are in the staging phase. This is still a fully supported query that 
/// has the potential of utilizing indexes. But you can apply only a few more 
/// operations.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IMagicQueryOrderable<T> : IMagicExecute<T> where T : class
{
    IMagicQueryPaginationTake<T> Take(int amount);
    IMagicQueryFinal<T> TakeLast(int amount);
    IMagicQueryFinal<T> Skip(int amount);
    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();

    /// <summary>
    /// In memory processing from this point forward! IndexDB 
    /// does not support complex query WHERE statements after appended 
    /// queries like, 'Take', 'Skip', 'OrderBy', and others are utilized.
    /// </summary>
    /// <param name="predicate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<T>> WhereAsync(Expression<Func<T, bool>> predicate);
}