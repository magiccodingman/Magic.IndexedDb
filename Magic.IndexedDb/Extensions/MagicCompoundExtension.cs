using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public static class MagicCompoundExtension
    {
        public static IMagicCompoundIndex Create<T>(params Expression<Func<T, object>>[] keySelectors)
        {
            return InternalMagicCompoundIndex<T>.Create(keySelectors);
        }
    }
}
