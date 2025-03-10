using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;

namespace TestWasm.Models
{
    public class Nested
    {
        public string Value { get; set; } = "abc";
    }

    [MagicTable("Person", DbNames.Client)]
    public class Person
    {
        public Nested Nested { get; set; } = new Nested();

        [MagicPrimaryKey("id")]
        public int _Id { get; set; }

        [MagicIndex]
        public string Name { get; set; }

        [MagicName("Age")]
        public int _Age { get; set; }

        [MagicIndex]
        public int TestInt { get; set; }
                
        public DateTime? DateOfBirth { get; set; }

        [MagicUniqueIndex("guid")]
        public Guid GUIY { get; set; } = Guid.NewGuid();
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

        [Flags]
        public enum Permissions
        {
            None = 0,
            CanRead = 1,
            CanWrite = 1 << 1,
            CanDelete = 1 << 2,
            CanCreate = 1 << 3
        }

        public Permissions Access { get; set; }
    }
}
