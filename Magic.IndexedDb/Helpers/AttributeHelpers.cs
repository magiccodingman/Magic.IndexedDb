using Magic.IndexedDb.Interfaces;
using System.Collections.Concurrent;

namespace Magic.IndexedDb.Helpers;

public class PrimaryKeys
{
    public string JsName { get; set; }
    public object Value { get; set; }
}
public static class AttributeHelpers
{
    private static readonly ConcurrentDictionary<Type, IMagicCompoundKey> _primaryKeyCache = new();

    public static List<PrimaryKeys> GetPrimaryKeys<T>(T item) where T : class
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        var primaryKeyProps = GetPrimaryKeyProperties(typeof(T)).PropertyInfos;

        return primaryKeyProps
            .Select(p => new PrimaryKeys
            {
                JsName = PropertyMappingCache.GetJsPropertyName<T>(p), // Convert this if needed (e.g., camelCase conversion)
                Value = p.GetValue(item)!
            })
            .ToList();
    }


    public static Type[] GetPrimaryKeyTypes<T>() where T : IMagicTableBase
    {
        return GetPrimaryKeyProperties(typeof(T)).PropertyInfos.Select(p => p.PropertyType).ToArray();
    }

    public static void ValidatePrimaryKey<T>(object[] keys) where T : IMagicTableBase
    {
        var expectedTypes = GetPrimaryKeyTypes<T>();

        if (keys.Length != expectedTypes.Length)
            throw new ArgumentException($"Invalid number of keys. Expected: {expectedTypes.Length}, received: {keys.Length}.");

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == null || !expectedTypes[i].IsInstanceOfType(keys[i]))
            {
                throw new ArgumentException($"Invalid key type at index {i}. Expected: {expectedTypes[i]}, received: {keys[i]?.GetType()}.");
            }
        }
    }

    private static IMagicCompoundKey GetPrimaryKeyProperties(Type type)
    {
        return _primaryKeyCache.GetOrAdd(type, t =>
        {
            if (!typeof(IMagicTableBase).IsAssignableFrom(t))
                throw new InvalidOperationException($"Type '{t.Name}' must implement IMagicTableBase.");

            var instance = Activator.CreateInstance(t) as IMagicTableBase;
            if (instance == null)
                throw new InvalidOperationException($"Unable to create an instance of '{t.Name}'.");

            var compoundKey = instance.GetKeys();
            if (compoundKey == null || compoundKey.PropertyInfos.Length == 0)
                throw new InvalidOperationException($"Type '{t.Name}' must have at least one primary key.");

            return compoundKey;
        });
    }
}