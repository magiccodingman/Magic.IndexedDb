using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    internal struct MagicQueryFunctions
    {
        public const string Take = "take";
        public const string Take_Last = "takeLast";
        public const string Skip = "skip";
        public const string Order_By = "orderBy";
        public const string Order_By_Descending = "orderByDescending";
        public const string Reverse = "reverse";
        public const string First = "first";
        public const string Last = "last";

    }
    public class StoredMagicQuery
    {
        /// <summary>
        /// additionFunction string name
        /// </summary>
        public string? additionFunction { get; set; }


        /// <summary>
        /// The int value for take, skip, and take last
        /// </summary>
        public int intValue { get; set; } = 0;


        /// <summary>
        /// The property we're targetting
        /// </summary>
        public string? property { get; set; }
    }
}
