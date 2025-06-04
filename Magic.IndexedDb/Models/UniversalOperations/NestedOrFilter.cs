using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models.UniversalOperations;

/// <summary>
/// Supported in Magic IndexDB Blazor C# code, but not currently supported purely through the 
/// universal LINQ JS layer. Built for future capabilities for flattening on the JS side 
/// instead of C# in future refactors. Only used currently for future scaleability. 
/// 
/// Represents a complex query that allows deeply nested OR conditions.
/// This enables advanced filtering logic with multi-layered conditions.
/// Example:
/// (
///     (age > 35 && testInt == 9) || 
///     (name StartsWith "J")
/// ) ||
/// (name Contains "bo")
/// </summary>
public class NestedOrFilter
{
    /// <summary>
    /// A list of OR-groups, where each group represents a possible matching condition set.
    /// </summary>
    public List<OrFilterGroup> orGroups { get; set; } = new();

    public bool universalFalse { get; set; } = false;
}