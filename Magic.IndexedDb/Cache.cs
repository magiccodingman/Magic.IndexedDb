using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    internal static class Cache
    {
        public static long JsMessageSizeBytes { get; set; } = 31000;
        /// <summary>
        /// this is the wwwroot path of "./magicDB.js" for importing the script
        /// </summary>
        public const string MagicDbJsImportPath = "./magicDB.js";
    }
}
