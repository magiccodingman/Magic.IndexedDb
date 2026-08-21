using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Models;
using TestBase.Repository;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class SchemaContractTests
{
    [TestMethod]
    public void PersonSchema_PreservesMappedKeysAndIndexes()
    {
        var schema = SchemaHelper.GetStoreSchema(typeof(Person));

        Assert.AreEqual("Person", schema.TableName);
        Assert.IsTrue(schema.PrimaryKeyAuto);
        CollectionAssert.AreEqual(new[] { "_id" }, schema.ColumnNamesInCompoundKey);
        CollectionAssert.AreEquivalent(
            new[] { "_id", nameof(Person.Name), "TestInt", nameof(Person.TestIntStable2) },
            schema.Indexes);
        CollectionAssert.AreEqual(new[] { "guid" }, schema.UniqueIndexes);
        Assert.AreEqual(1, schema.ColumnNamesInCompoundIndex.Count);
        CollectionAssert.AreEqual(
            new[] { nameof(Person.TestIntStable2), nameof(Person.Name) },
            schema.ColumnNamesInCompoundIndex[0]);
    }

    [TestMethod]
    public void CompoundSchema_PreservesKeyAndIndexOrdering()
    {
        var schema = SchemaHelper.GetStoreSchema(typeof(CompositeRecord));

        Assert.IsFalse(schema.PrimaryKeyAuto);
        CollectionAssert.AreEqual(
            new[] { nameof(CompositeRecord.Tenant), nameof(CompositeRecord.Sequence) },
            schema.ColumnNamesInCompoundKey);
        CollectionAssert.AreEqual(
            new[] { nameof(CompositeRecord.Tenant), nameof(CompositeRecord.Category) },
            schema.ColumnNamesInCompoundIndex.Single());
    }

    [TestMethod]
    public void Validator_AcceptsKnownProductionLikeSchemas()
    {
        MagicValidator.ValidateTables([typeof(Person), typeof(ContractRecord), typeof(CompositeRecord)]);
    }

    [TestMethod]
    public void Validator_RejectsAutoIncrementOnNonNumericKey()
    {
        var exception = Assert.ThrowsExactly<Exception>(() =>
            MagicValidator.ValidateTables([typeof(InvalidAutoIncrementRecord)]));

        StringAssert.Contains(exception.Message, "auto-increment");
        StringAssert.Contains(exception.Message, nameof(InvalidAutoIncrementRecord.Key));
    }

    [TestMethod]
    public void Validator_RejectsConflictingMagicAttributes()
    {
        var exception = Assert.ThrowsExactly<Exception>(() =>
            MagicValidator.ValidateTables([typeof(ConflictingAttributeRecord)]));

        StringAssert.Contains(exception.Message, "multiple Magic attributes");
        StringAssert.Contains(exception.Message, nameof(ConflictingAttributeRecord.Value));
    }

    private sealed class InvalidAutoIncrementRecord : MagicTableTool<InvalidAutoIncrementRecord>, IMagicTable<Person.DbSets>
    {
        public string Key { get; set; } = string.Empty;
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(record => record.Key, true);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
        public string GetTableName() => nameof(InvalidAutoIncrementRecord);
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public Person.DbSets Databases { get; } = new();
    }

    private sealed class ConflictingAttributeRecord : MagicTableTool<ConflictingAttributeRecord>, IMagicTable<Person.DbSets>
    {
        public int Key { get; set; }

        [MagicIndex]
        [MagicName("value")]
        public string Value { get; set; } = string.Empty;

        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(record => record.Key, false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
        public string GetTableName() => nameof(ConflictingAttributeRecord);
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public Person.DbSets Databases { get; } = new();
    }
}
