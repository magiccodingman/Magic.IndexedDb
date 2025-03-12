using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Interfaces
{
    public interface IMagicQueryStage<T> : IMagicExecute<T> where T : class
    {
        IMagicQueryStage<T> Take(int amount);
        IMagicQueryStage<T> TakeLast(int amount);
        IMagicQueryStage<T> Skip(int amount);
        IMagicQueryStage<T> OrderBy(Expression<Func<T, object>> predicate);
        IMagicQueryStage<T> OrderByDescending(Expression<Func<T, object>> predicate);

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
