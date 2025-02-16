using Magic.IndexedDb.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class MagicTableAttribute : Attribute
    {
        public string SchemaName { get; }
        public string DatabaseName { get; }

        public MagicTableAttribute(string schemaName, string databaseName)
        {
            SchemaName = schemaName;
            DatabaseName = databaseName;
        }
    }
}
