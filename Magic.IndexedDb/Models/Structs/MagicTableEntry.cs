using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public struct MagicTableEntry
    {

        /// <summary>
        /// Property with the, "MagicTableAttribute" attribute appended
        /// </summary>
        public bool IsIndexDbTable { get; }

        public MagicTableEntry(bool isIndexDbTable)
        {
            IsIndexDbTable = isIndexDbTable;
        }
    }
}
