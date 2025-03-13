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
    public interface IMagicQuery<T> : IMagicExecute<T> where T : class
    {
        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        IMagicQuery<T> Where(Expression<Func<T, bool>> predicate);

        IMagicQueryPaginationTake<T> Take(int amount);
        IMagicQueryFinal<T> TakeLast(int amount);
        IMagicQueryFinal<T> Skip(int amount);
        IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate);
        IMagicQueryOrderable<T> OrderByDescending(Expression<Func<T, object>> predicate);
    }
}
