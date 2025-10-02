using System.Buffers;
using System.IO.Pipelines;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using System.Text.Json;

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

    public static async Task<string[]> SerializeObjectsToString(ITypedArgument[] objs, MagicJsonSerializationSettings? settings = null)
    {
        var arr = new List<string>();
        foreach (ITypedArgument arg in objs)
        {
            string value = await arg.SerializeToJsonString(settings);
            arr.Add(value);
        }
        return arr.ToArray();
    }

    public static async Task<JsonElement> SerializeObjectToJsonElement<T>(T value, MagicJsonSerializationSettings? settings = null)
    {
        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied
        
        var pipe = new Pipe(GetPipeOptions());
        await JsonSerializer.SerializeAsync(pipe.Writer, value, typeof(T), options, default);
        await pipe.Writer.CompleteAsync();
        var result = await pipe.Reader.ReadAsync();
        await pipe.Reader.CompleteAsync();
        var resultbytes = result.Buffer.ToArray();
        var resultchars = resultbytes.Select(c => (char)c).ToArray();
        var jsonString = String.Join("", resultchars);

        // Convert the string to a JsonElement so that Blazor treats it as a structured object
        using JsonDocument doc = JsonDocument.Parse(jsonString);
        return doc.RootElement.Clone(); // Clone to prevent disposal issues
    }

    public static async Task SerializeObjectToStreamAsync<T>(StreamWriter writer, T value, MagicJsonSerializationSettings? settings = null)
    {
        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        var options = settings.GetOptionsWithResolver<T>();
        string jsonString = JsonSerializer.Serialize(value, options); // Use your serializer

        await writer.WriteAsync(jsonString);
        await writer.FlushAsync();
    }

    public static async Task<string> SerializeObject<T>(T? value, MagicJsonSerializationSettings? settings = null)
    {
        if (value == null)
            return "null";

        if (settings == null)
            settings = new MagicJsonSerializationSettings();

        if (value == null)
            throw new ArgumentNullException(nameof(value), "Object cannot be null");

        var options = settings.GetOptionsWithResolver<T>(); // Ensure the correct resolver is applied
        
        var pipe = new Pipe(GetPipeOptions());
        await JsonSerializer.SerializeAsync(pipe.Writer, value, typeof(T), options);
        await pipe.Writer.CompleteAsync();
        var result = await pipe.Reader.ReadAsync();
        var resultbytes = result.Buffer.ToArray();
        var resultchars = resultbytes.Select(c => (char)c).ToArray();
        var jsonString = String.Join("", resultchars);
        return jsonString;
    }

    public static async Task<T?> DeserializeObject<T>(string json, MagicJsonSerializationSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be null or empty.", nameof(json));

        if (settings == null)
            settings = new MagicJsonSerializationSettings();
        
        Type type = typeof(T);

        var options = settings.GetOptionsWithResolver<T>(); // Ensure correct resolver for deserialization
        char[] chars = json.ToCharArray();
        byte[] bytes = chars.Select(c => (byte)c).ToArray();
        var span = new ReadOnlyMemory<byte>(bytes);
        var pipe = new Pipe(GetPipeOptions());
        await pipe.Writer.WriteAsync(span);
        await pipe.Writer.CompleteAsync();
        var ret = await JsonSerializer.DeserializeAsync<T?>(pipe.Reader, options);
        await pipe.Reader.CompleteAsync();
        return ret;
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

    public static PipeOptions GetPipeOptions()
    {
        return new PipeOptions(pauseWriterThreshold:0);
    }
}