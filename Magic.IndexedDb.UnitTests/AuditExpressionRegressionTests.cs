using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class AuditExpressionRegressionTests
{
    [TestMethod]
    public void CapturedValueConversion_PreservesItsCSharpValue()
    {
        var source = 257;
        var node = new UniversalExpressionBuilder<Record>(row => row.Number == (byte)source).Build();
        Assert.AreEqual(1, Convert.ToInt32(node.Condition!.Value.value));
    }

    [TestMethod]
    public void CapturedStringComparison_PreservesCaseSensitivity()
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var node = new UniversalExpressionBuilder<Record>(row => row.Name.Contains("abc", comparison)).Build();
        Assert.IsFalse(node.Condition!.Value.caseSensitive);
    }

    [TestMethod]
    public void NestedStringMember_DoesNotSilentlyQuerySameNamedRootProperty()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => row.Child.Name.Contains("abc")).Build());
    }

    [TestMethod]
    public void NestedLengthMember_DoesNotSilentlyQuerySameNamedRootProperty()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => row.Child.Name.Length > 2).Build());
    }

    [TestMethod]
    public void CapturedStringMember_DoesNotSilentlyQuerySameNamedRootProperty()
    {
        var other = new Record { Name = "abc" };
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => other.Name.Contains("abc")).Build());
    }

    [TestMethod]
    public void AnyExpansion_PreservesNullElements()
    {
        string?[] values = [null, "abc"];
        Expression<Func<Record, bool>> predicate = row => values.Any(value => row.Name == value);
        var rewritten = (Expression<Func<Record, bool>>)new PredicateVisitor<Record>().Visit(predicate)!;
        foreach (var value in new string?[] { null, "abc", "other" })
        {
            var row = new Record { Name = value! };
            Assert.AreEqual(predicate.Compile()(row), rewritten.Compile()(row));
        }
    }

    [TestMethod]
    public void AllExpansion_PreservesNullElements()
    {
        string?[] values = [null];
        Expression<Func<Record, bool>> predicate = row => values.All(value => row.Name == value);
        var rewritten = (Expression<Func<Record, bool>>)new PredicateVisitor<Record>().Visit(predicate)!;
        foreach (var value in new string?[] { null, "abc" })
        {
            var row = new Record { Name = value! };
            Assert.AreEqual(predicate.Compile()(row), rewritten.Compile()(row));
        }
    }

    [TestMethod]
    public void CreatingCursor_DoesNotMutateTheReusableSourceQuery()
    {
        var query = new MagicQuery<Record>("db", "records", null!);
        var cursor = (MagicCursor<Record>)query.Cursor(row => row.Number > 0);
        Assert.IsFalse(query.ForceCursorMode);
        Assert.IsTrue(cursor.MagicQuery.ForceCursorMode);
        Assert.AreEqual(0, query.Predicates.Count);
        Assert.AreEqual(1, cursor.MagicQuery.Predicates.Count);
    }

    [TestMethod]
    public void ChainedWhere_BindsBothPredicatesToTheCombinedLambda()
    {
        var query = new MagicQuery<Record>("db", "records", null!);
        var staged = (MagicQuery<Record>)query.Where(first => first.Number > 0)
            .Where(second => second.Name == "abc");
        var predicate = staged.GetFinalPredicate().Compile();
        Assert.IsTrue(predicate(new Record { Number = 1, Name = "abc" }));
        Assert.IsFalse(predicate(new Record { Number = 0, Name = "abc" }));
        Assert.IsFalse(predicate(new Record { Number = 1, Name = "other" }));
    }

    [TestMethod]
    public void NegatedNullableComparison_IncludesTheNullCase()
    {
        Expression<Func<Record, bool>>[] predicates =
        [
            row => !(row.NullableNumber > 10),
            row => !(row.NullableNumber >= 10),
            row => !(row.NullableNumber < 10),
            row => !(row.NullableNumber <= 10),
            row => !(10 < row.NullableNumber)
        ];
        foreach (var predicate in predicates)
        {
            var node = new UniversalExpressionBuilder<Record>(predicate).Build();
            var expected = predicate.Compile();
            foreach (var value in new int?[] { null, -1, 9, 10, 11 })
            {
                var row = new Record { NullableNumber = value };
                Assert.AreEqual(expected(row), EvaluateNullableNumber(node, value), $"{predicate}; value={value}");
            }
        }
    }

    [TestMethod]
    public void NestedOrdering_DoesNotSilentlySortSameNamedRootProperty()
    {
        var query = new MagicQuery<Record>("db", "records", null!);
        Assert.ThrowsExactly<ArgumentException>(() => query.OrderBy(row => row.Child.Name));
        Assert.ThrowsExactly<ArgumentException>(() => query.OrderByDescending(row => row.Child.Name));
    }

    [TestMethod]
    public void NestedDateComponent_DoesNotSilentlyQuerySameNamedRootProperty()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => row.Child.When.Year == 2030).Build());
    }

    [TestMethod]
    public void DateComparison_PreservesNonMidnightCutoffs()
    {
        var cutoff = new DateTime(2030, 4, 5, 12, 0, 0);
        Expression<Func<Record, bool>>[] predicates =
        [
            row => row.When.Date == cutoff,
            row => row.When.Date != cutoff,
            row => row.When.Date > cutoff,
            row => row.When.Date >= cutoff,
            row => row.When.Date < cutoff,
            row => row.When.Date <= cutoff
        ];
        AssertDateParity(predicates, [cutoff.AddDays(-1), cutoff.Date, cutoff, cutoff.AddDays(1)]);
    }

    [TestMethod]
    public void DateComparison_SupportsTheLastRepresentableDay()
    {
        var cutoff = DateTime.MaxValue.Date;
        Expression<Func<Record, bool>>[] predicates =
        [
            row => row.When.Date == cutoff,
            row => row.When.Date != cutoff,
            row => row.When.Date > cutoff,
            row => row.When.Date >= cutoff,
            row => row.When.Date < cutoff,
            row => row.When.Date <= cutoff
        ];
        AssertDateParity(predicates, [DateTime.MinValue, cutoff.AddDays(-1), cutoff, DateTime.MaxValue]);
    }

    [TestMethod]
    public void NarrowingEnumCast_IsRejectedInsteadOfChangingMatches()
    {
        // Both 1 and 4294967297 become 1 when narrowed to Int32. A comparison of
        // the stored Int64 enum to 1 would silently lose the second match.
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => (int)row.LargeStatus == 1).Build());
    }

    [TestMethod]
    public void PropertyConverterEquality_UsesTheStoredRepresentation()
    {
        var node = new UniversalExpressionBuilder<Record>(row => row.Status == Status.Active).Build();
        AssertStatusOperand(node.Condition!.Value.value);
    }

    [TestMethod]
    public void PropertyConverterMembership_UsesTheStoredRepresentation()
    {
        Status[] values = [Status.Active];
        var node = new UniversalExpressionBuilder<Record>(row => values.Contains(row.Status)).Build();
        AssertStatusOperand(node.Children!.Single().Condition!.Value.value);
    }

    [TestMethod]
    public void PropertyConverterRange_IsRejectedWhenStoredOrderingIsUnknown()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<Record>(row => (int)row.Status > 0).Build());
        var query = new MagicQuery<Record>("db", "records", null!);
        Assert.ThrowsExactly<InvalidOperationException>(() => query.OrderBy(row => row.Status));
        Assert.ThrowsExactly<InvalidOperationException>(() => query.OrderByDescending(row => row.Status));
    }

    private static void AssertStatusOperand(object? operand)
    {
        var settings = new MagicJsonSerializationSettings();
        using var record = JsonDocument.Parse(MagicSerializationHelper.SerializeObject(
            new Record { Status = Status.Active }, settings));
        using var value = JsonDocument.Parse(MagicSerializationHelper.SerializeObject(operand, settings));
        Assert.AreEqual("\"Active\"", record.RootElement.GetProperty(nameof(Record.Status)).GetRawText());
        Assert.AreEqual(record.RootElement.GetProperty(nameof(Record.Status)).GetRawText(), value.RootElement.GetRawText());
    }

    private static void AssertDateParity(IEnumerable<Expression<Func<Record, bool>>> predicates, DateTime[] values)
    {
        foreach (var predicate in predicates)
        {
            var node = new UniversalExpressionBuilder<Record>(predicate).Build();
            var expected = predicate.Compile();
            foreach (var value in values)
                Assert.AreEqual(expected(new Record { When = value }), EvaluateDate(node, value), $"{predicate}; value={value:O}");
        }
    }

    private static bool EvaluateDate(FilterNode node, DateTime value)
    {
        if (node.NodeType == FilterNodeType.Logical)
            return node.Operator == FilterLogicalOperator.And
                ? node.Children!.All(child => EvaluateDate(child, value))
                : node.Children!.Any(child => EvaluateDate(child, value));
        var condition = node.Condition!.Value;
        if (condition.property == "__constant") return (bool)condition.value!;
        Assert.AreEqual(nameof(Record.When), condition.property);
        var operand = (DateTime)condition.value!;
        return condition.operation switch
        {
            "Equal" => value == operand,
            "NotEqual" => value != operand,
            "GreaterThan" => value > operand,
            "GreaterThanOrEqual" => value >= operand,
            "LessThan" => value < operand,
            "LessThanOrEqual" => value <= operand,
            _ => throw new AssertFailedException($"Unexpected date operation {condition.operation}.")
        };
    }

    private static bool EvaluateNullableNumber(FilterNode node, int? value)
    {
        if (node.NodeType == FilterNodeType.Logical)
            return node.Operator == FilterLogicalOperator.And
                ? node.Children!.All(child => EvaluateNullableNumber(child, value))
                : node.Children!.Any(child => EvaluateNullableNumber(child, value));

        var condition = node.Condition!.Value;
        Assert.AreEqual(nameof(Record.NullableNumber), condition.property);
        var operand = condition.value is null ? (int?)null : Convert.ToInt32(condition.value);
        return condition.operation switch
        {
            "IsNull" => value is null,
            "GreaterThan" => value > operand,
            "GreaterThanOrEqual" => value >= operand,
            "LessThan" => value < operand,
            "LessThanOrEqual" => value <= operand,
            _ => throw new AssertFailedException($"Unexpected numeric operation {condition.operation}.")
        };
    }

    private sealed class Record
    {
        public int Number { get; set; }
        public int? NullableNumber { get; set; }
        public string Name { get; set; } = "";
        public ChildRecord Child { get; set; } = new();
        public DateTime When { get; set; }
        public LargeStatus LargeStatus { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
        public Status Status { get; set; }
    }

    private sealed class ChildRecord
    {
        public string Name { get; set; } = "";
        public DateTime When { get; set; }
    }

    private enum Status { Inactive, Active }
    private enum LargeStatus : long { One = 1, Wrapped = 4294967297 }
}
