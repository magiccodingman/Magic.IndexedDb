using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models;

public class TypedArgument<T> : ITypedArgument
{
    public T? Value { get; }

    public TypedArgument(T? value)
    {
        Value = value;
    }

    public string Serialize()
    {
        return MagicSerializationHelper.SerializeObject(Value);
    }

    public JsonElement SerializeToJsonElement(MagicJsonSerializationSettings? settings = null)
    {
        return MagicSerializationHelper.SerializeObjectToJsonElement(Value, settings);
    }

    public string SerializeToJsonString(MagicJsonSerializationSettings? settings = null)
    {
        return MagicSerializationHelper.SerializeObject(Value, settings);
    }
}