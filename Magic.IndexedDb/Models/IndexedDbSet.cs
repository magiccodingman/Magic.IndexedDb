using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb;

public class IndexedDbSet
{
    public string DatabaseName { get; }
    public IndexedDbSet(string databaseName)
    {
        DatabaseName = databaseName;
    }
}