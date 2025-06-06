using System.Text.Json;

namespace Magic.IndexedDb.Models;

public class MagicJsonSerializationSettings
{
    private JsonSerializerOptions _options = new();

    public JsonSerializerOptions Options
    {
        get => _options;
        set => _options = value ?? new JsonSerializerOptions(); // Ensure it's never null
    }

    public bool UseCamelCase
    {
        get => _options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase;
        set
        {
            _options = new JsonSerializerOptions(_options) // Clone existing settings
            {
                PropertyNamingPolicy = value ? JsonNamingPolicy.CamelCase : null
            };
        }
    }

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