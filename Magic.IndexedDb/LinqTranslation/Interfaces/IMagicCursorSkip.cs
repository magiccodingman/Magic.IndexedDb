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
        //IMagicCursorSkip<T> Skip(int amount);
        Task<T?> FirstOrDefaultAsync();
        Task<T?> LastOrDefaultAsync();

    }
}
