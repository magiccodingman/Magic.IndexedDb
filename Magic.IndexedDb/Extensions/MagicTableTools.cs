using Magic.IndexedDb.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class MagicTableTool<T> where T : class, IMagicTableBase, new()
    {
        protected CreateCompoundIndex CreateCompoundIndex(params Expression<Func<T, object>>[] keySelectors)
        {
            return MagicCompoundExtension.Create<T>(keySelectors);
        }
    }

}
