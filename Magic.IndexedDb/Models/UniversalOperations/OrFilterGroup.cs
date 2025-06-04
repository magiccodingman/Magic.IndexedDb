using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models.UniversalOperations;

/// <summary>
/// Represents a group of AND-filter groups that are connected using OR (||).
/// At least one of these AND groups must be TRUE for the filter to apply.
/// Example: (age > 35 && name StartsWith "J") || (name Contains "bo")
/// </summary>
public class OrFilterGroup
{
    /// <summary>
    /// A list of AND-groups, where each group represents an AND-filter set.
    /// </summary>
    public List<AndFilterGroup> andGroups { get; set; } = new();
}