using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Interfaces
{
    public interface IMagicExecute<T> where T : class
    {
        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default);

        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <returns></returns>
        Task<List<T>> ToListAsync();
    }
}
