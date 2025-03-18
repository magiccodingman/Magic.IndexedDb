﻿using Magic.IndexedDb;

using Magic.IndexedDb.SchemaAnnotations;
using TestWasm.Repository;
using static TestWasm.Models.Person;

namespace TestWasm.Models
{
    public class Nested
    {
        public string Value { get; set; } = "abc";
    }


    public class Person : MagicTableTool<Person>, IMagicTable<DbSets>
    {
        public List<IMagicCompoundIndex> GetCompoundIndexes() =>
            new List<IMagicCompoundIndex>() {
            CreateCompoundIndex(x => x.TestIntStable2, x => x.Name)
            };

        public IMagicCompoundKey? GetCompoundKey() =>
            null;// CreateCompoundKey(x => x.TestIntStable2, x => x.TestIntStable);

        

        public string GetTableName() => "Person";
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public DbSets Databases { get; } = new();
        public sealed class DbSets
        {
            public readonly IndexedDbSet Client = IndexDbContext.Client;
            public readonly IndexedDbSet Employee = IndexDbContext.Employee;
        }


        public int TestIntStable { get; set; }
        public int TestIntStable2 { get; set; } = 10;

        public Nested Nested { get; set; } = new Nested();

        [MagicPrimaryKey(true, "id")]
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
