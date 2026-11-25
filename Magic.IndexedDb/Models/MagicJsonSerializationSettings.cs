using System.Text.Json;
using Magic.IndexedDb.Helpers;

namespace Magic.IndexedDb.Models;

public class MagicJsonSerializationSettings
{
    private JsonSerializerOptions _options = new();

    /// <summary>Controls wide numeric values for direct serialization calls. Database services use their registered database policy.</summary>
    public MagicNumericStorageMode NumericStorageMode { get; set; }

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
        var newOptions = GetOptionsWithNumericConverters();
        // An application converter owns this type's complete JSON representation.
        if (!MagicConverterHelper.HasCustomConverter(typeof(T), newOptions))
            newOptions.Converters.Add(new MagicContractResolver<T>());
        return newOptions;
    }

    internal JsonSerializerOptions GetOptionsWithNumericConverters()
    {
        if (!Enum.IsDefined(NumericStorageMode))
            throw new ArgumentOutOfRangeException(nameof(NumericStorageMode));
        var options = new JsonSerializerOptions(Options);
        if (NumericStorageMode == MagicNumericStorageMode.Lossless &&
            !options.Converters.Any(converter => converter is MagicNumericConverterFactory))
            options.Converters.Add(new MagicNumericConverterFactory());
        return options;
    }
}
