using Magic.IndexedDb.LinqTranslation.Extensions;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ExpressionBuilderTests
{
    [TestMethod]
    public void ExplicitEnumConversions_AreRecognizedAsPropertyComparisons()
    {
        var node = new UniversalExpressionBuilder<EnumRecord>(
            record => (int)record.Status == (int)RecordStatus.Active).Build();

        Assert.IsTrue(node.Condition.HasValue);
        Assert.AreEqual(nameof(EnumRecord.Status), node.Condition.Value.property);
        Assert.AreEqual("Equal", node.Condition.Value.operation);
        Assert.AreEqual(1, node.Condition.Value.value);
    }

    private sealed class EnumRecord
    {
        public RecordStatus Status { get; set; }
    }

    private enum RecordStatus
    {
        Inactive = 0,
        Active = 1
    }
}
