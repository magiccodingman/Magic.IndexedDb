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

        // Support is coming. It's mostly the C# side that needs small refactoring to support.
        // Though the JS side will need to implement and support primary key multi key support when 
        // coming in as brackets.
        //IMagicCompoundKey? GetCompoundKey();

        // <summary>
        /// Set the default database most commonly utilized for this table.
        /// </summary>
        IndexedDbSet GetDefaultDatabase();
    }
}
