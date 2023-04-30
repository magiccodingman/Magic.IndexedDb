using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class DbStore
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string EncryptionKey { get; set; }
        public List<StoreSchema> StoreSchemas { get; set; }
        public List<DbMigration> DbMigrations { get; set; } = new List<DbMigration>();
    }
}
