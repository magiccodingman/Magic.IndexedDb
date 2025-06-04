using Magic.IndexedDb.LinqTranslation.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb;

public interface IMagicCursorFinal<T> : IMagicExecute<T> where T : class
{
    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();
}
public interface IMagicCursorPaginationTake<T> : IMagicExecute<T> where T : class
{
    IMagicCursorSkip<T> Skip(int amount);

}
public interface IMagicCursorStage<T> : IMagicExecute<T> where T : class
{

    IMagicCursorPaginationTake<T> Take(int amount);
    IMagicCursorPaginationTake<T> TakeLast(int amount);
    IMagicCursorSkip<T> Skip(int amount);

    Task<T?> FirstOrDefaultAsync();
    Task<T?> LastOrDefaultAsync();
}