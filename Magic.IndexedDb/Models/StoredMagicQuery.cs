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
        public string? Name { get; set; }
        public int IntValue { get; set; } = 0;
        public string? StringValue { get; set; }
    }
}
