using Magic.IndexedDb.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public struct MagicPropertyEntry
    {
        /// <summary>
        /// Constructor for initializing MagicPropertyEntry while reducing memory footprint.
        /// </summary>
        public MagicPropertyEntry(PropertyInfo property, IColumnNamed? columnNamedAttribute,
                                  bool indexed, bool uniqueIndex, bool primaryKey, bool notMapped)
        {
            Property = property;
            _columnNamedAttribute = columnNamedAttribute;
            Indexed = indexed;
            UniqueIndex = uniqueIndex;
            PrimaryKey = primaryKey;
            NotMapped = notMapped;
        }

        /// <summary>
        /// Reference to the IColumnNamed attribute if present, otherwise null. 
        /// This prevents saving the original string provided unecessary which 
        /// saves minimum 20 bytes if the IColumnName originally was empty. 
        /// Aka it means we're saving much more than 20 bytes per item.
        /// </summary>
        private readonly IColumnNamed? _columnNamedAttribute;

        /// <summary>
        /// The JavaScript/Column Name mapping
        /// </summary>
        public string JsPropertyName =>
            _columnNamedAttribute?.ColumnName ?? Property.Name;

        /// <summary>
        /// Reference to the PropertyInfo instead of storing the C# name as a string. 
        /// Which reduces memory print from the minimum empty string size of 20 bytes 
        /// to now only 8 bytes (within 64 bit systems).
        /// </summary>
        public PropertyInfo Property { get; set; }

        /// <summary>
        /// The C# Property Name mapping
        /// </summary>
        public string CsharpPropertyName => Property.Name;

        /// <summary>
        /// Property with the, "MagicIndexAttribute" attribute appended
        /// </summary>
        public bool Indexed { get; set; }

        /// <summary>
        /// Property with the, "MagicUniqueIndexAttribute" attribute appended
        /// </summary>
        public bool UniqueIndex { get; set; }

        /// <summary>
        /// Property with the, "MagicPrimaryKeyAttribute" attribute appended
        /// </summary>
        public bool PrimaryKey { get; set; }

        /// <summary>
        /// Property with the, "MagicNotMappedAttribute" attribute appended
        /// </summary>
        public bool NotMapped { get; set; }
    }
}
