using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class QueryPlannerInputContractTests
{
    [TestMethod]
    public void Independent_indexed_AND_reaches_JS_as_AND_with_both_conditions()
    {
        var node = new UniversalExpressionBuilder<PlannerRecord>(
            record => record.Name == "Zack" && record.TestInt == 3).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.And, node.Operator);

        var conditions = Conditions(node).ToList();
        Assert.AreEqual(2, conditions.Count);
        CollectionAssert.AreEquivalent(
            new[] { nameof(PlannerRecord.Name), nameof(PlannerRecord.TestInt) },
            conditions.Select(condition => condition.property).ToArray());
        Assert.IsTrue(conditions.All(condition => condition.operation == "Equal"));
    }

    [TestMethod]
    public void Compound_candidate_with_residual_reaches_JS_with_all_three_conditions()
    {
        var node = new UniversalExpressionBuilder<PlannerRecord>(
            record => record.Name == "Zack"
                && record.TestIntStable2 == 10
                && record.TestInt == 3).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.And, node.Operator);

        var conditions = Conditions(node).ToList();
        Assert.AreEqual(3, conditions.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(PlannerRecord.Name),
                nameof(PlannerRecord.TestIntStable2),
                nameof(PlannerRecord.TestInt)
            },
            conditions.Select(condition => condition.property).ToArray());
    }

    [TestMethod]
    public void Multiple_StartsWith_reaches_JS_as_OR_of_prefix_conditions()
    {
        var node = new UniversalExpressionBuilder<PlannerRecord>(
            record => record.Name.StartsWith("Za", StringComparison.OrdinalIgnoreCase)
                || record.Name.StartsWith("Lu", StringComparison.OrdinalIgnoreCase)).Build();

        Assert.AreEqual(FilterNodeType.Logical, node.NodeType);
        Assert.AreEqual(FilterLogicalOperator.Or, node.Operator);

        var conditions = Conditions(node).ToList();
        Assert.AreEqual(2, conditions.Count);
        Assert.IsTrue(conditions.All(condition => condition.operation == "StartsWith"));
        Assert.IsTrue(conditions.All(condition => condition.caseSensitive == false));
        CollectionAssert.AreEquivalent(
            new[] { "Za", "Lu" },
            conditions.Select(condition => (string)condition.value!).ToArray());
    }

    private static IEnumerable<Magic.IndexedDb.Models.UniversalOperations.FilterCondition> Conditions(FilterNode node)
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

    private sealed class PlannerRecord
    {
        public string Name { get; set; } = string.Empty;
        public int TestInt { get; set; }
        public int TestIntStable2 { get; set; }
    }
}
