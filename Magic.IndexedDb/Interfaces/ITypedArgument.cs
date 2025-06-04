using Magic.IndexedDb.Models;
using System.Text.Json;

namespace Magic.IndexedDb.Interfaces;

public interface ITypedArgument
{
    string Serialize(); // Still needed for some cases
    JsonElement SerializeToJsonElement(MagicJsonSerializationSettings? settings = null); // Ensures proper object passing
    string SerializeToJsonString(MagicJsonSerializationSettings? settings = null);
}