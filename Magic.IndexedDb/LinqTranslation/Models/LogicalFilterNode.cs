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