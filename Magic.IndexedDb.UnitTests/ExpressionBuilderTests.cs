using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ExpressionBuilderTests
{
    [TestMethod]
    public void PlainEnumEquality_IsRecognizedAndSerializedNumerically()
    {
        var node = new UniversalExpressionBuilder<EnumRecord>(
            record => record.Status == RecordStatus.Active).Build();

        AssertNumericEnumCondition(node);
    }

    [TestMethod]
    public void ExplicitEnumConversions_AreRecognizedAsPropertyComparisons()
    {
        var node = new UniversalExpressionBuilder<EnumRecord>(
            record => (int)record.Status == (int)RecordStatus.Active).Build();

        AssertNumericEnumCondition(node);
    }

    [TestMethod]
    public void NullableAndReversedEnumEquality_AreRecognized()
    {
        var nullable = new UniversalExpressionBuilder<NullableEnumRecord>(
            record => record.Status == RecordStatus.Active).Build();
        var reversed = new UniversalExpressionBuilder<EnumRecord>(
            record => RecordStatus.Active == record.Status).Build();

        Assert.AreEqual(RecordStatus.Active, nullable.Condition!.Value.value);
        Assert.AreEqual(RecordStatus.Active, reversed.Condition!.Value.value);
    }

    [TestMethod]
    public void StringEnumConverter_UsesTheSameRepresentationForRecordsAndFilters()
    {
        var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
        var node = new UniversalExpressionBuilder<NamedEnumRecord>(
            record => record.Status == NamedStatus.Active).Build();

        var recordJson = MagicSerializationHelper.SerializeObject(
            new NamedEnumRecord { Status = NamedStatus.Active }, settings);
        var filterJson = MagicSerializationHelper.SerializeObject(node, settings);

        Assert.AreEqual("Active", JsonDocument.Parse(recordJson).RootElement
            .GetProperty("status").GetString());
        Assert.AreEqual("Active", JsonDocument.Parse(filterJson).RootElement
            .GetProperty("condition").GetProperty("value").GetString());
    }

    [TestMethod]
    public void StringBackedEnums_RejectRangeComparisons()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<NamedEnumRecord>(
                record => (int)record.Status > 0).Build());

        StringAssert.Contains(exception.Message, "persisted as a JSON string");
    }

    [TestMethod]
    public void NonEnumMemberConversions_RemainUnsupported()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<NumericRecord>(
                record => (int)record.Amount == 1).Build());

        StringAssert.Contains(exception.Message, "Unsupported binary expression");
    }

    private static void AssertNumericEnumCondition(
        Magic.IndexedDb.LinqTranslation.Models.FilterNode node)
    {
        Assert.IsTrue(node.Condition.HasValue);
        Assert.AreEqual(nameof(EnumRecord.Status), node.Condition.Value.property);
        Assert.AreEqual("Equal", node.Condition.Value.operation);
        Assert.AreEqual(RecordStatus.Active, node.Condition.Value.value);

        var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
        var json = MagicSerializationHelper.SerializeObject(node, settings);
        Assert.AreEqual(1, JsonDocument.Parse(json).RootElement
            .GetProperty("condition").GetProperty("value").GetInt32());
    }

    private sealed class EnumRecord
    {
        public RecordStatus Status { get; set; }
    }

    private sealed class NullableEnumRecord
    {
        public RecordStatus? Status { get; set; }
    }

    private sealed class NamedEnumRecord
    {
        public NamedStatus Status { get; set; }
    }

    private sealed class NumericRecord
    {
        public decimal Amount { get; set; }
    }

    private enum RecordStatus
    {
        Inactive = 0,
        Active = 1
    }

    [JsonConverter(typeof(JsonStringEnumConverter<NamedStatus>))]
    private enum NamedStatus
    {
        Inactive = 0,
        Active = 1
    }
}
