using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class StoreSchema
    {
        public string Name { get; set; }
        public string PrimaryKey { get; set; }
        public bool PrimaryKeyAuto { get; set; }
        public List<string> UniqueIndexes { get; set; } = new List<string>();
        public List<string> Indexes { get; set; } = new List<string>();
    }
}
