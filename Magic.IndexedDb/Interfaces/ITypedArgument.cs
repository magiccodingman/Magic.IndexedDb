using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Interfaces
{
    public interface ITypedArgument
    {
        string Serialize(); // Still needed for some cases
        JsonElement SerializeToJsonElement(MagicJsonSerializationSettings? settings = null); // Ensures proper object passing
        string SerializeToJsonString(MagicJsonSerializationSettings? settings = null);
    }
}
