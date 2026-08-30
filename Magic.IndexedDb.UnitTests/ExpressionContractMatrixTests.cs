using System.Linq.Expressions;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ExpressionContractMatrixTests
{
    [TestMethod]
    public void NumericComparisonMatrix_PreservesOperationAndOperandDirection()
    {
        (Expression<Func<QueryRecord, bool>> Predicate, string Operation, object Value)[] cases =
        [
            (record => record.Age == 18, "Equal", 18),
            (record => record.Age != 18, "NotEqual", 18),
            (record => record.Age > 18, "GreaterThan", 18),
            (record => record.Age >= 18, "GreaterThanOrEqual", 18),
            (record => record.Age < 18, "LessThan", 18),
            (record => record.Age <= 18, "LessThanOrEqual", 18),
            (record => 18 < record.Age, "GreaterThan", 18),
            (record => 18 <= record.Age, "GreaterThanOrEqual", 18),
            (record => 18 > record.Age, "LessThan", 18),
            (record => 18 >= record.Age, "LessThanOrEqual", 18)
        ];

        foreach (var (predicate, operation, value) in cases)
        {
            var condition = Condition(predicate);
            Assert.AreEqual("persisted_age", condition.property, predicate.ToString());
            Assert.AreEqual(operation, condition.operation, predicate.ToString());
            Assert.AreEqual(value, condition.value, predicate.ToString());
        }
    }

    [TestMethod]
    public void CapturedValues_AreEvaluatedWithoutChangingThePredicateShape()
    {
        var minimum = 21;

        var condition = Condition<QueryRecord>(record => record.Age >= minimum);

        Assert.AreEqual("GreaterThanOrEqual", condition.operation);
        Assert.AreEqual(21, condition.value);
    }

    [TestMethod]
    public void BooleanMember_IsTranslatedAsEqualityWithTrue()
    {
        var condition = Condition<QueryRecord>(record => record.Enabled);

        Assert.AreEqual(nameof(QueryRecord.Enabled), condition.property);
        Assert.AreEqual("Equal", condition.operation);
        Assert.AreEqual(true, condition.value);
    }

    [TestMethod]
    public void StringMethodMatrix_PreservesOperationAndCaseSensitivity()
    {
        (Expression<Func<QueryRecord, bool>> Predicate, string Operation, bool CaseSensitive)[] cases =
        [
            (record => record.Name.Contains("ab"), "Contains", true),
            (record => record.Name.Contains("ab", StringComparison.OrdinalIgnoreCase), "Contains", false),
            (record => record.Name.StartsWith("ab", StringComparison.Ordinal), "StartsWith", true),
            (record => record.Name.EndsWith("ab", StringComparison.OrdinalIgnoreCase), "EndsWith", false),
            (record => !record.Name.Contains("ab"), "NotContains", true),
            (record => !record.Name.StartsWith("ab"), "NotStartsWith", true),
            (record => !record.Name.EndsWith("ab"), "NotEndsWith", true)
        ];

        foreach (var (predicate, operation, caseSensitive) in cases)
        {
            var condition = Condition(predicate);
            Assert.AreEqual(operation, condition.operation, predicate.ToString());
            Assert.AreEqual(caseSensitive, condition.caseSensitive, predicate.ToString());
            Assert.AreEqual("ab", condition.value, predicate.ToString());
        }
    }

    [TestMethod]
    public void LengthComparisonMatrix_UsesLengthOperations()
    {
        (Expression<Func<QueryRecord, bool>> Predicate, string Operation)[] cases =
        [
            (record => record.Name.Length == 3, "LengthEqual"),
            (record => record.Name.Length != 3, "NotLengthEqual"),
            (record => record.Name.Length > 3, "LengthGreaterThan"),
            (record => record.Name.Length >= 3, "LengthGreaterThanOrEqual"),
            (record => record.Name.Length < 3, "LengthLessThan"),
            (record => record.Name.Length <= 3, "LengthLessThanOrEqual")
        ];

        foreach (var (predicate, operation) in cases)
            Assert.AreEqual(operation, Condition(predicate).operation, predicate.ToString());
    }

    [TestMethod]
    public void DateComponentMatrix_UsesComponentOperations()
    {
        (Expression<Func<QueryRecord, bool>> Predicate, string Operation, object Value)[] cases =
        [
            (record => record.When.Year == 2030, "YearEqual", 2030),
            (record => record.When.Month != 2, "NotMonthEqual", 2),
            (record => record.When.Day > 10, "DayGreaterThan", 10),
            (record => record.When.DayOfYear <= 100, "DayOfYearLessThanOrEqual", 100),
            (record => record.When.DayOfWeek == DayOfWeek.Monday, "DayOfWeekEqual", 1)
        ];

        foreach (var (predicate, operation, value) in cases)
        {
            var condition = Condition(predicate);
            Assert.AreEqual(operation, condition.operation, predicate.ToString());
            Assert.AreEqual(value, condition.value, predicate.ToString());
        }
    }

    [TestMethod]
    public void CollectionMembership_PreservesEveryAlternative()
    {
        int[] values = [1, 3, 5];

        var node = new UniversalExpressionBuilder<QueryRecord>(
            record => values.Contains(record.Age)).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.Or, node.Operator);
        CollectionAssert.AreEqual(
            values,
            node.Children!.Select(child => (int)child.Condition!.Value.value!).ToArray());
        Assert.IsTrue(node.Children!.All(child => child.Condition!.Value.operation == "Equal"));
    }

    [TestMethod]
    public void NegatedLogicalExpression_AppliesDeMorgansLaw()
    {
        var node = new UniversalExpressionBuilder<QueryRecord>(
            record => !(record.Age > 18 || record.Name == "admin")).Build();

        Assert.AreEqual(FilterLogicalOperator.And, node.Operator);
        Assert.AreEqual(2, node.Children!.Count);
        Assert.AreEqual("LessThanOrEqual", node.Children[0].Condition!.Value.operation);
        Assert.AreEqual("NotEqual", node.Children[1].Condition!.Value.operation,
            "Negated equality must use the canonical NotEqual operation understood by JavaScript.");
    }

    [TestMethod]
    public void UnsupportedArithmeticExpression_FailsWithActionableContext()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new UniversalExpressionBuilder<QueryRecord>(
                record => record.Age + 1 > 20).Build());

        StringAssert.Contains(exception.Message, "Unsupported binary expression");
    }

    private static Magic.IndexedDb.Models.UniversalOperations.FilterCondition Condition<T>(
        Expression<Func<T, bool>> predicate)
    {
        var node = new UniversalExpressionBuilder<T>(predicate).Build();
        Assert.IsTrue(node.Condition.HasValue, predicate.ToString());
        return node.Condition.Value;
    }

    private sealed class QueryRecord
    {
        [MagicName("persisted_age")]
        public int Age { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public DateTime When { get; set; }
    }
}
