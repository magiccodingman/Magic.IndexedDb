using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;

namespace IndexDb.Example
{
    [MagicTable("Person", DbNames.Client)]
    public class Person
    {
        [MagicPrimaryKey("id")]
        public int _Id { get; set; }

        [MagicIndex]
        public string Name { get; set; }

        [MagicIndex("Age")]
        public int _Age { get; set; }

        [MagicIndex]
        public int TestInt { get; set; }

        [MagicUniqueIndex("guid")]
        public Guid GUIY { get; set; } = Guid.NewGuid();

        [MagicEncrypt]
        public string Secret { get; set; }

        [MagicNotMapped]
        public string DoNotMapTest { get; set; }

        [MagicNotMapped]
        public string SecretDecrypted { get; set; }

        private bool testPrivate { get; set; } = false;

        public bool GetTest()
        {
            return true;
        }

    }

    
}
