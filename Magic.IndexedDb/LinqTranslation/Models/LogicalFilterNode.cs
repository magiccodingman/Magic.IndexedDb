using Magic.IndexedDb.Models.UniversalOperations;

namespace Magic.IndexedDb.LinqTranslation.Models;

public class FilterNode
{
    public FilterNodeType NodeType { get; set; }
    public FilterLogicalOperator Operator { get; set; }
    public List<FilterNode>? Children { get; set; }
    public FilterCondition? Condition { get; set; }
}

/// <summary>
/// Represents logical operators in the filter tree.
/// </summary>
public enum FilterLogicalOperator
{
    And = 0,
    Or = 1,
}

public enum FilterNodeType
{
    Logical = 0,
    Condition = 1
}

///// <summary>
///// The base class for any node in our filter tree.
///// </summary>
//public class FilterNode
//{
//    public FilterNodeType NodeType { get; set; }
//    public FilterLogicalOperator? Operator { get; set; }
//    public List<FilterNode>? Children { get; set; }
//    public FilterCondition? Condition { get; set; }
//}



///// <summary>
///// A node that holds a logical operator (AND/OR) and child nodes.
///// </summary>
//public class LogicalFilterNode : FilterNode
//{
//    public FilterLogicalOperator Operator { get; set; }

//    /// <summary>
//    /// The child nodes under this operator (could be conditions or nested operators).
//    /// </summary>
//    public List<FilterNode> Children { get; set; } = new();
//}



///// <summary>
///// A node that holds a single FilterCondition (leaf).
///// </summary>
//public class ConditionFilterNode : FilterNode
//{
//    public FilterCondition Condition { get; set; }

//    public ConditionFilterNode(FilterCondition condition)
//    {
//        Condition = condition;
//    }

//    public ConditionFilterNode() { }
//}