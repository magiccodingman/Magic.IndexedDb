using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models.UniversalOperations
{
    /// <summary>
    /// Represents a single filtering condition applied to a property.
    /// Example: "name StartsWith 'J'", "age > 35", etc.
    /// </summary>
    public struct FilterCondition
    {
        /// <summary>
        /// The name of the property to filter on.
        /// Example: "name", "age"
        /// </summary>
        public string property { get; set; }

        /// <summary>
        /// The comparison operation to apply.
        /// Example: "Equal", "StartsWith", "GreaterThan"
        /// </summary>
        public string operation { get; set; }

        /// <summary>
        /// The value being compared against.
        /// Example: "John" (for strings), 35 (for numbers).
        /// </summary>
        public object? value { get; set; }

        /// <summary>
        /// Indicates whether the value should be treated as a string.
        /// </summary>
        public bool isString { get; set; }

        /// <summary>
        /// Specifies whether the comparison should be case-sensitive (for strings).
        /// </summary>
        public bool caseSensitive { get; set; }

        public FilterCondition(string _property, string _operation, object? _value, 
            bool _isString = false, bool _caseSensitive = false)
        {
            property = _property;
            operation = _operation;
            value = _value;
            isString = _isString;
            caseSensitive = _caseSensitive;
        }
    }
}
