using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Magic.IndexedDb.Helpers;

/// <summary>
/// Helper to serialize between the Magic Library content to the JS. To communicate with Dexie.JS - 
/// Note I left this public only to allow it to be targeted by external projects for testing.
/// </summary>
public static class MagicSerializationHelper
{

    public static object[] SerializeObjects(ITypedArgument[] objs, MagicJsonSerializationSettings? settings = null)
    {
        return objs.Select(arg => arg.SerializeToJsonElement(settings)).Cast<object>().ToArray();
    }

    internal static JsonElement[] SerializeArguments(
        ITypedArgument[] arguments,
        MagicJsonSerializationSettings? settings = null)
    {
        return arguments.Select(argument => argument.SerializeToJsonElement(settings)).ToArray();
    }

    public static string[] SerializeObjectsToString(ITypedArgument[] objs, MagicJsonSerializationSettings? settings = null)
    {
        return objs.Select(arg => arg.SerializeToJsonString(settings)).ToArray();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(StoreRecord<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(UpdateRecord<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PrimaryKeys))]
    public static JsonElement SerializeObjectToJsonElement<T>(T value, MagicJsonSerializationSettings? settings = null)
    {
        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied
        return JsonSerializer.SerializeToElement(value, options);
    }

    public static async Task SerializeObjectToStreamAsync<T>(StreamWriter writer, T value, MagicJsonSerializationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        settings ??= new MagicJsonSerializationSettings();
        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        // Preserve the public TextWriter overload's encoding behavior. Internal
        // interop uses the Stream overload below to avoid this intermediate string.
        var options = settings.GetOptionsWithResolver<T>();
        await writer.WriteAsync(JsonSerializer.Serialize(value, options));
        await writer.FlushAsync();
    }

    public static async Task SerializeObjectToStreamAsync<T>(Stream stream, T value, MagicJsonSerializationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        var options = settings.GetOptionsWithResolver<T>();
        await JsonSerializer.SerializeAsync(stream, value, options);
        await stream.FlushAsync();
    }

    internal static async Task SerializeJsPackageToStreamAsync(
        Stream stream,
        MagicJsPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(package);

        // The wire envelope has stable names under both the standard and
        // fully linked runtimes. Arguments already carry their own contracts.
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("protocolVersion", package.ProtocolVersion);
        writer.WriteBoolean("yieldResults", package.YieldResults);
        writer.WriteString("modulePath", package.ModulePath);
        writer.WriteString("methodName", package.MethodName);
        writer.WriteBoolean("isVoid", package.IsVoid);
        writer.WriteStartArray("parameters");
        foreach (var parameter in package.Parameters)
            parameter.WriteTo(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static string SerializeObject<T>(T? value, MagicJsonSerializationSettings? settings = null)
    {
        if (value == null)
            return "null";

        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied

        return JsonSerializer.Serialize(value, options);
    }

    public static T? DeserializeObject<T>(string json, MagicJsonSerializationSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be null or empty.", nameof(json));

        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        var options = settings.GetOptionsWithResolver<T>(); // Ensure correct resolver for deserialization

        return JsonSerializer.Deserialize<T>(json, options);
    }

    public static void PopulateObject<T>(T source, T target)
    {
        if (source == null || target == null)
            throw new ArgumentNullException("Source and target cannot be null");

        var json = JsonSerializer.Serialize(source);
        var deserialized = JsonSerializer.Deserialize<T>(json);

        foreach (var prop in typeof(T).GetProperties())
        {
            var value = prop.GetValue(deserialized);
            prop.SetValue(target, value);
        }
    }
}
