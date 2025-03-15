using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Interfaces
{
    public interface IMagicTableBase
    {
        string GetTableName();

        List<IMagicCompoundIndex>? GetCompoundIndexes();
        IMagicCompoundKey? GetCompoundKey();

        // <summary>
        /// Set the default database most commonly utilized for this table.
        /// </summary>
        IndexedDbSet GetDefaultDatabase();
    }
}
