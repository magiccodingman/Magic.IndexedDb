﻿using Magic.IndexedDb.LinqTranslation.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    /// <summary>
    /// You are in the staging phase. This is still a fully supported query that 
    /// has the potential of utilizing indexes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IMagicQueryStage<T> : IMagicExecute<T> where T : class
    {
        IMagicQueryPaginationTake<T> Take(int amount);
        IMagicQueryPaginationTake<T> TakeLast(int amount);
        IMagicQueryFinal<T> Skip(int amount);
        IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate);
        IMagicQueryOrderable<T> OrderByDescending(Expression<Func<T, object>> predicate);

        /// <summary>
        /// In memory processing from this point forward! IndexDB 
        /// does not support complex query WHERE statements after appended 
        /// queries like, 'Take', 'Skip', 'OrderBy', and others are utilized.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<IEnumerable<T>> WhereAsync(Expression<Func<T, bool>> predicate, [EnumeratorCancellation] CancellationToken cancellationToken = default);
    }
}
