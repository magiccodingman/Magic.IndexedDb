using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models.UniversalOperations
{
    /// <summary>
    /// Represents a group of conditions that are connected using AND (&&).
    /// All conditions inside this group must be TRUE for the filter to apply.
    /// Example: (Age > 35 && Name StartsWith "J")
    /// </summary>
    public class AndFilterGroup
    {
        /// <summary>
        /// A list of conditions that are joined using AND (&&).
        /// </summary>
        public List<FilterCondition> conditions { get; set; } = new();
    }
}
