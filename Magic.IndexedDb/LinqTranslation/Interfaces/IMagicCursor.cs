﻿using System.Linq.Expressions;

namespace Magic.IndexedDb;

public interface IMagicCursor<T> : IMagicExecute<T> where T : class
{
    /// <summary>
    /// Pure 100% cursor query. No limitations on any appended additions.
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate);

    IMagicCursorPaginationTake<T> Take(int amount);
    IMagicCursorPaginationTake<T> TakeLast(int amount);
    IMagicCursorSkip<T> Skip(int amount);

    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();

    /// <summary>
    /// This always orders first by the primary key, then by whatever is appended afterwards
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    IMagicCursorStage<T> OrderBy(Expression<Func<T, object>> predicate);

    /// <summary>
    /// This always orders by descending by the primary key first, then by whatever is appended afterwards
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    IMagicCursorStage<T> OrderByDescending(Expression<Func<T, object>> predicate);

    /// <summary>
    /// Removes ordering of any indexed columns. Only orders by 
    /// what you dictate then by the row insert order. This creates 
    /// a much more predictable and stable ordering. Only needs 
    /// to be applied once. 
    /// </summary>
    /// <returns></returns>
    IMagicCursorStage<T> StableOrdering();
}