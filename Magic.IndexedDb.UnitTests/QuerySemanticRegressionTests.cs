using System.Linq.Expressions;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Models.UniversalOperations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class QuerySemanticRegressionTests
{
    [TestMethod]
    public void Binary_string_equality_preserves_CSharp_case_sensitivity()
    {
        var condition = Condition<QueryRecord>(record => record.Name == "Zack");

        Assert.AreEqual("Equal", condition.operation);
        Assert.IsTrue(condition.caseSensitive,
            "Ordinary C# string == is case-sensitive and must not acquire ignore-case cursor semantics.");
    }

    [TestMethod]
    public void Negated_equality_uses_canonical_NotEqual_operation()
    {
        var condition = Condition<QueryRecord>(record => !(record.Name == "Zack"));

        Assert.AreEqual("NotEqual", condition.operation,
            "The JS universal operation vocabulary defines NotEqual, not NotEquals.");
    }

    [TestMethod]
    public void Negated_inequality_uses_canonical_Equal_operation()
    {
        var condition = Condition<QueryRecord>(record => !(record.Name != "Zack"));

        Assert.AreEqual("Equal", condition.operation,
            "Negating != is equality and must use the canonical Equal operation token.");
    }

    [TestMethod]
    public void String_Equals_uses_canonical_Equal_operation()
    {
        var condition = Condition<QueryRecord>(record => record.Name.Equals("Zack"));

        Assert.AreEqual("Equal", condition.operation,
            "Supported string.Equals translation must use the same wire operation understood by the JS evaluator.");
        Assert.IsTrue(condition.caseSensitive,
            "string.Equals(string) uses case-sensitive ordinal equality by default.");
    }

    [TestMethod]
    public void Collection_property_Contains_uses_canonical_Contains_operation()
    {
        var node = new UniversalExpressionBuilder<QueryRecord>(
            record => record.Values.Contains(3)).Build();
        var condition = Conditions(node).Single();

        Assert.AreEqual("Contains", condition.operation,
            "The cursor evaluator already implements Contains for arrays; the translator must not invent ArrayContains.");
    }

    [TestMethod]
    public void Date_member_greater_than_uses_next_day_boundary()
    {
        var target = new DateTime(2030, 4, 5);
        var condition = Condition<QueryRecord>(record => record.When.Date > target);

        Assert.AreEqual("GreaterThanOrEqual", condition.operation);
        Assert.AreEqual(target.AddDays(1), condition.value,
            "date > target means timestamp >= start of the following day.");
    }

    [TestMethod]
    public void Date_member_less_than_or_equal_uses_next_day_exclusive_boundary()
    {
        var target = new DateTime(2030, 4, 5);
        var condition = Condition<QueryRecord>(record => record.When.Date <= target);

        Assert.AreEqual("LessThan", condition.operation);
        Assert.AreEqual(target.AddDays(1), condition.value,
            "date <= target means timestamp < start of the following day.");
    }

    [TestMethod]
    public void Date_member_not_equal_excludes_the_entire_target_day()
    {
        var target = new DateTime(2030, 4, 5);
        var node = new UniversalExpressionBuilder<QueryRecord>(
            record => record.When.Date != target).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.Or, node.Operator);

        var conditions = Conditions(node).ToList();
        Assert.AreEqual(2, conditions.Count);
        CollectionAssert.AreEquivalent(
            new[] { "LessThan", "GreaterThanOrEqual" },
            conditions.Select(condition => condition.operation).ToArray());

        Assert.AreEqual(target,
            conditions.Single(condition => condition.operation == "LessThan").value);
        Assert.AreEqual(target.AddDays(1),
            conditions.Single(condition => condition.operation == "GreaterThanOrEqual").value);
    }

    [TestMethod]
    public void Nullable_date_member_not_equal_includes_null_values()
    {
        var target = new DateTime(2030, 4, 5);
        var node = new UniversalExpressionBuilder<QueryRecord>(
            record => record.NullableWhen.Value.Date != target).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.Or, node.Operator);

        var conditions = Conditions(node).ToList();
        Assert.AreEqual(3, conditions.Count);
        CollectionAssert.AreEquivalent(
            new[] { "IsNull", "LessThan", "GreaterThanOrEqual" },
            conditions.Select(condition => condition.operation).ToArray());

        Assert.AreEqual(target,
            conditions.Single(condition => condition.operation == "LessThan").value);
        Assert.AreEqual(target.AddDays(1),
            conditions.Single(condition => condition.operation == "GreaterThanOrEqual").value);
    }

    [TestMethod]
    public void Empty_Any_rewrites_to_logical_false_without_throwing()
    {
        int[] values = [];
        Expression<Func<QueryRecord, bool>> predicate =
            record => values.Any(value => record.Age == value);

        var rewritten = (Expression<Func<QueryRecord, bool>>)
            new PredicateVisitor<QueryRecord>().Visit(predicate)!;

        Assert.IsFalse(rewritten.Compile()(new QueryRecord { Age = 42 }),
            "Any over an empty sequence is false.");
    }

    [TestMethod]
    public void Empty_All_rewrites_to_logical_true_without_throwing()
    {
        int[] values = [];
        Expression<Func<QueryRecord, bool>> predicate =
            record => values.All(value => record.Age == value);

        var rewritten = (Expression<Func<QueryRecord, bool>>)
            new PredicateVisitor<QueryRecord>().Visit(predicate)!;

        Assert.IsTrue(rewritten.Compile()(new QueryRecord { Age = 42 }),
            "All over an empty sequence is true.");
    }

    [TestMethod]
    public void Nonempty_Any_and_All_rewrites_retain_quantifier_semantics()
    {
        int[] anyValues = [3, 7];
        Expression<Func<QueryRecord, bool>> anyPredicate =
            record => anyValues.Any(value => record.Age == value);
        var anyRewritten = (Expression<Func<QueryRecord, bool>>)
            new PredicateVisitor<QueryRecord>().Visit(anyPredicate)!;

        Assert.IsTrue(anyRewritten.Compile()(new QueryRecord { Age = 7 }));
        Assert.IsFalse(anyRewritten.Compile()(new QueryRecord { Age = 8 }));

        int[] allValues = [3, 3];
        Expression<Func<QueryRecord, bool>> allPredicate =
            record => allValues.All(value => record.Age == value);
        var allRewritten = (Expression<Func<QueryRecord, bool>>)
            new PredicateVisitor<QueryRecord>().Visit(allPredicate)!;

        Assert.IsTrue(allRewritten.Compile()(new QueryRecord { Age = 3 }));
        Assert.IsFalse(allRewritten.Compile()(new QueryRecord { Age = 4 }));
    }

    private static FilterCondition Condition<T>(Expression<Func<T, bool>> predicate)
    {
        var node = new UniversalExpressionBuilder<T>(predicate).Build();
        Assert.IsTrue(node.Condition.HasValue, predicate.ToString());
        return node.Condition.Value;
    }

    private static IEnumerable<FilterCondition> Conditions(FilterNode node)
    {
        if (node.Condition.HasValue)
        {
            yield return node.Condition.Value;
            yield break;
        }

        foreach (var child in node.Children ?? [])
        {
            foreach (var condition in Conditions(child))
                yield return condition;
        }
    }

    private sealed class QueryRecord
    {
        public int Age { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<int> Values { get; set; } = [];
        public DateTime When { get; set; }
        public DateTime? NullableWhen { get; set; }
    }
}
