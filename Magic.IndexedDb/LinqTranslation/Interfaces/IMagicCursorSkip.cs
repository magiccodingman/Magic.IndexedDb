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
    public interface IMagicCursorSkip<T> : IMagicExecute<T> where T : class
    {
        IMagicCursorSkip<T> Skip(int amount);

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

        Task<T?> FirstOrDefaultAsync();
        Task<T?> LastOrDefaultAsync();
    }
}
