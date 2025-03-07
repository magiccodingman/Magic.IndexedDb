using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class DbStore
    {
        public string Name { get; set; }
        public int Version { get; set; }

        [Obsolete("Being depreciated")]
        public string EncryptionKey { get; set; }

        [JsonPropertyName("storeSchemas")]
        public List<StoreSchema> StoreSchemas { get; set; }

        [Obsolete("This will likely be created to work with the Truth Protocol. Stay tuned.")]
        public List<DbMigration> DbMigrations { get; set; } = new List<DbMigration>();
    }
}
