using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.SchemaAnnotations
{
    [AttributeUsage(AttributeTargets.Property)]
    public class MagicIndexAttribute : Attribute { }
}
