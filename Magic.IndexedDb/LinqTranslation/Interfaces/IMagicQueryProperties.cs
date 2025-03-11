using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Interfaces
{
    // Remove this?
    public interface IMagicQueryProperties<T> where T : class
    {
        MagicQuery<T> MagicQuery { get; set; }
    }
}
