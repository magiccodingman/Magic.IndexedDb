using System.Text.Json;
using Magic.IndexedDb.Helpers;

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
        // An application converter owns this type's complete JSON representation.
        if (!MagicConverterHelper.HasCustomConverter(typeof(T), newOptions))
            newOptions.Converters.Add(new MagicContractResolver<T>());
        return newOptions;
    }
}
