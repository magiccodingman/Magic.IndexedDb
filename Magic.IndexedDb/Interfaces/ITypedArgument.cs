using Magic.IndexedDb.Models;
using System.Text.Json;

namespace Magic.IndexedDb.Interfaces;

public interface ITypedArgument
{
    Task<string> Serialize(); // Still needed for some cases
    Task<JsonElement> SerializeToJsonElement(MagicJsonSerializationSettings? settings = null); // Ensures proper object passing
    Task<string> SerializeToJsonString(MagicJsonSerializationSettings? settings = null);
}