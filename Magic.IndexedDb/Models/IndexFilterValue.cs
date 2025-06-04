using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models;

public class IndexFilterValue
{
    public IndexFilterValue(string indexName, object filterValue)
    {
        IndexName = indexName;
        FilterValue = filterValue;
    }

    public string IndexName { get; set; }
    public object FilterValue { get; set; }
}