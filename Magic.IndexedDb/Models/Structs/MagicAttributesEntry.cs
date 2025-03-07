using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models.Structs
{
    public struct MagicAttributesEntry
    {
        /// <summary>
        /// Constructor for initializing MagicPropertyEntry while reducing memory footprint.
        /// </summary>
        public MagicAttributesEntry(bool indexed, bool uniqueIndex, bool primaryKey, bool notMapped)
        {
            Indexed = indexed;
            UniqueIndex = uniqueIndex;
            PrimaryKey = primaryKey;
            NotMapped = notMapped;
        }

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
