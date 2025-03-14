using Magic.IndexedDb.LinqTranslation.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
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

        IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate);

        IMagicQueryPaginationTake<T> Take(int amount);
        IMagicQueryFinal<T> TakeLast(int amount);
        IMagicQueryFinal<T> Skip(int amount);

        /// <summary>
        /// This always orders first by the primary key, then by whatever is appended afterwards
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate);

        /// <summary>
        /// This always orders by descending by the primary key first, then by whatever is appended afterwards
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        IMagicQueryOrderable<T> OrderByDescending(Expression<Func<T, object>> predicate);
    }
}
