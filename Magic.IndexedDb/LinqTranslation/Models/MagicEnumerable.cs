using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class MagicEnumerable<T> where T : class
    {
        internal MagicQuery<T> MagicQuery { get; }
        public MagicEnumerable(MagicQuery<T> _magicQuery)
        {
            MagicQuery = _magicQuery;
        }


    }
}
