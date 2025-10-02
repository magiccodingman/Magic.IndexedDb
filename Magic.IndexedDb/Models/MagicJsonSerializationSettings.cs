using System.Text.Json;

namespace Magic.IndexedDb.Models;

public class MagicJsonSerializationSettings
{
    private JsonSerializerOptions Options = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Ensures the MagicContractResolver is applied for a specific type at runtime.
    /// </summary>
    public JsonSerializerOptions GetOptionsWithResolver<T>()
    {
        var newOptions = new JsonSerializerOptions(Options); // Clone settings
        newOptions.Converters.Add(new MagicContractResolver<T>()); // Ensure the correct resolver is added
        return newOptions;
    }
}