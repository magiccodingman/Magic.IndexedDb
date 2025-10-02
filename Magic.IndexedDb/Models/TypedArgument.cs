using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using System.Text.Json;

namespace Magic.IndexedDb.Models;

public class TypedArgument<T> : ITypedArgument
{
    public T? Value { get; }

    public TypedArgument(T? value)
    {
        Value = value;
    }

    public async Task<string> Serialize()
    {
        return await MagicSerializationHelper.SerializeObject(Value);
    }

    public async Task<JsonElement> SerializeToJsonElement(MagicJsonSerializationSettings? settings = null)
    {
        return await MagicSerializationHelper.SerializeObjectToJsonElement(Value, settings);
    }

    public async Task<string> SerializeToJsonString(MagicJsonSerializationSettings? settings = null)
    {
        return await MagicSerializationHelper.SerializeObject(Value, settings);
    }
}