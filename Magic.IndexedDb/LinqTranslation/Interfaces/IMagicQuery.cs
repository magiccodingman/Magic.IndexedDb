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
        IMagicQuery<T> Where(Expression<Func<T, bool>> predicate);

        // Working on supporting these operations without the Where
        IMagicQueryStage<T> Take(int amount);
        IMagicQueryStage<T> TakeLast(int amount);
        IMagicQueryStage<T> Skip(int amount);
        IMagicQueryStage<T> OrderBy(Expression<Func<T, object>> predicate);
        IMagicQueryStage<T> OrderByDescending(Expression<Func<T, object>> predicate);
    }
}
