using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Exceptions;

namespace Magic.IndexedDb.Helpers;

public struct SearchPropEntry
{
    public SearchPropEntry(Type type, Dictionary<string, MagicPropertyEntry> _propertyEntries, ConstructorInfo[] constructors)
    {
        propertyEntries = _propertyEntries;
        jsNameToCsName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ConstructorParameterMappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in propertyEntries)
        {
            jsNameToCsName[entry.Value.JsPropertyName] = entry.Value.CsharpPropertyName;
        }

        // Extract IMagicTableBase info
        if (typeof(IMagicTableBase).IsAssignableFrom(type))
        {
            var instance = Activator.CreateInstance(type) as IMagicTableBase;
            if (instance != null)
            {
                EffectiveTableName = instance.GetTableName(); // Use the provided table name
                EnforcePascalCase = true; // Prevent camel casing
            }
        }
        else
        {
            EffectiveTableName = type.Name; // Default to class name
            EnforcePascalCase = false;
        }

        // 🔥 Pick the best constructor: Prefer MagicConstructor, then fall back to a parameterized one, else fallback to parameterless
        if (constructors.Count(c => c.GetCustomAttribute<MagicConstructorAttribute>() != null) > 1)
        {
            throw new MagicConstructorException("Only one magic constructor is allowed");
        }

        var magicConstructor = constructors.FirstOrDefault(c => c.GetCustomAttribute<MagicConstructorAttribute>() != null);
        if (magicConstructor == null)
        {
            Constructor = constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        }
        HasConstructorParameters = Constructor != null && Constructor.GetParameters().Length > 0;

        // 🔥 Cache constructor parameter mappings
        if (HasConstructorParameters)
        {
            var parameters = Constructor.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                ConstructorParameterMappings[parameters[i].Name!] = i;
            }
        }

        // 🔥 Store constructor in a local variable before using in lambda (Fix for struct issue)
        var localConstructor = Constructor;

        // 🔥 Cache fast instance creator
        if (localConstructor != null)
        {
            InstanceCreator = (args) => localConstructor.Invoke(args);
        }
        else
        {
            InstanceCreator = (_) =>
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    // Instantiate a List<T> when an IEnumerable<T> is requested
                    Type listType = typeof(List<>).MakeGenericType(type.GetGenericArguments());
                    return Activator.CreateInstance(listType);
                }

                if (IsInstantiable(type))
                {
                    return Activator.CreateInstance(type);
                }
                else
                {
                    throw new InvalidOperationException($"Cannot instantiate abstract/interface type {type.FullName}");
                }
            };
        }

    }

    public string EffectiveTableName { get; } // ✅ Stores the final name (SchemaName or C# class name)
    public bool EnforcePascalCase { get; } // ✅ If true, prevents camel casing

    public ConstructorInfo? Constructor { get; } // ✅ Stores the most relevant constructor
    public bool HasConstructorParameters { get; } // ✅ Cached flag to avoid checking length
    public Func<object?[], object?> InstanceCreator { get; } // ✅ Cached instance creator

    public Dictionary<string, MagicPropertyEntry> propertyEntries { get; }
    public Dictionary<string, string> jsNameToCsName { get; }
    public Dictionary<string, int> ConstructorParameterMappings { get; } // ✅ Stores constructor parameter indexes

    /// <summary>
    /// Determines whether a type can be instantiated.
    /// </summary>
    private static bool IsInstantiable(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            return false;

        // Handle generic collection interfaces like IEnumerable<T>, ICollection<T>, etc.
        if (type.IsGenericType)
        {
            Type genericTypeDef = type.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(IEnumerable<>) ||
                genericTypeDef == typeof(ICollection<>) ||
                genericTypeDef == typeof(IList<>))
            {
                return true; // Can instantiate List<T>
            }
        }

        return true;
    }

}

public static class PropertyMappingCache
{
    internal static readonly ConcurrentDictionary<Type, SearchPropEntry> _propertyCache = new();


    public static List<MagicPropertyEntry> GetPrimaryKeysOfType(Type type)
    {
        return GetTypeOfTProperties(type).propertyEntries
            .Where(prop => prop.Value.PrimaryKey)
            .Select(prop => prop.Value)
            .ToList();
    }


    public static SearchPropEntry GetTypeOfTProperties(Type type)
    {
        EnsureTypeIsCached(type);
        if (_propertyCache.TryGetValue(type!, out var properties))
        {
            return properties;
        }
        throw new Exception("Something went very wrong getting GetTypeOfTProperties");
    }


    private static readonly HashSet<Type> _simpleTypes = new()
    {
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(Guid), typeof(Uri), typeof(TimeSpan)
    };

    // Cache lookups for extreme performance
    private static readonly ConcurrentDictionary<Type, bool> _typeCache = new();
    private static readonly Type ObjectType = typeof(object); // ✅ Cached for performance

    public static bool IsSimpleType(Type type)
    {
        if (type == null)
            return false;

        // First, check cache
        if (_typeCache.TryGetValue(type, out bool cachedResult))
            return cachedResult;

        // If the type is Nullable<T>, extract T.
        if (Nullable.GetUnderlyingType(type) is Type underlyingType)
            type = underlyingType;

        // Anonymous = complex
        if (IsAnonymousType(type))
            return true;

        // Unwrap System.Text.Json.Nodes.JsonValueCustomized<T> (avoiding .FullName for perf)
        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            if (genericType.Namespace == "System.Text.Json.Nodes" && genericType.Name.StartsWith("JsonValueCustomized"))
            {
                type = type.GetGenericArguments()[0]; // Extract T from JsonValueCustomized<T>

                // If T is still just object, force serialization as a simple type
                if (type == ObjectType)
                {
                    _typeCache.TryAdd(type, true);
                    return true;
                }
            }
        }

        // If the final unwrapped type is still object, force serialization as-is
        if (type == ObjectType)
        {
            _typeCache.TryAdd(type, true);
            return true;
        }

        // Check if the final unwrapped type is simple
        bool result = type.IsPrimitive || type.IsEnum || _simpleTypes.Contains(type);

        // Store result in cache
        _typeCache.TryAdd(type, result);

        return result;
    }

    private static bool IsAnonymousType(Type type)
    {
        return Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
               && type.IsGenericType
               && type.Name.Contains("AnonymousType")
               && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
               && type.Namespace == null;
    }

    public static IEnumerable<Type> GetAllNestedComplexTypes(IEnumerable<PropertyInfo> properties)
    {
        HashSet<Type> complexTypes = new();
        Stack<Type> typeStack = new();

        // Initial population of the stack
        foreach (var property in properties)
        {
            if (IsComplexType(property.PropertyType))
            {
                typeStack.Push(property.PropertyType);
                complexTypes.Add(property.PropertyType);
            }
        }

        // Process all nested complex types
        while (typeStack.Count > 0)
        {
            var currentType = typeStack.Pop();
            var nestedProperties = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var nestedProperty in nestedProperties)
            {
                if (IsComplexType(nestedProperty.PropertyType) && !complexTypes.Contains(nestedProperty.PropertyType))
                {
                    complexTypes.Add(nestedProperty.PropertyType);
                    typeStack.Push(nestedProperty.PropertyType);
                }
            }
        }

        return complexTypes;
    }

    public static bool IsComplexType(Type type)
    {
        return !(IsSimpleType(type)
                 || type == typeof(string)
                 || typeof(IEnumerable).IsAssignableFrom(type) // Non-generic IEnumerable
                 || (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition())) // Generic IEnumerable<T>
                 || type.IsArray); // Arrays are collections too
    }

    /// <summary>
    /// Gets the C# property name given a JavaScript property name.
    /// </summary>
    public static string GetCsharpPropertyName<T>(string jsPropertyName)
    {
        return GetCsharpPropertyName(jsPropertyName, typeof(T));
    }

    /// <summary>
    /// Gets the C# property name given a JavaScript property name.
    /// </summary>
    public static string GetCsharpPropertyName(string jsPropertyName, Type type)
    {
        EnsureTypeIsCached(type);
        string typeKey = type.FullName!;

        try
        {
            if (_propertyCache.TryGetValue(type, out var search))
            {
                return search.GetCsharpPropertyName(jsPropertyName);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving C# property name for JS property '{jsPropertyName}' in type {type.FullName}.", ex);
        }

        return jsPropertyName; // Fallback to original name if not found
    }

    public static MagicPropertyEntry GetPropertyByCsharpName(this SearchPropEntry propCachee, string csharpName)
    {
        string errorMsg = $"Error retrieving C# property by the name of '{csharpName}'.";
        try
        {
            if (propCachee.propertyEntries.TryGetValue(csharpName, out var entry))
            {
                return entry;
            }
        }
        catch (Exception ex)
        {
            throw new Exception(errorMsg, ex);
        }

        throw new Exception(errorMsg);
    }

    public static string GetCsharpPropertyName(this SearchPropEntry propCachee, string jsPropertyName)
    {
        try
        {
            if (propCachee.jsNameToCsName.TryGetValue(jsPropertyName, out var csName)
                && propCachee.propertyEntries.TryGetValue(csName, out var entry))
            {
                return entry.CsharpPropertyName;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving C# property name for JS property '{jsPropertyName}'.", ex);
        }

        return jsPropertyName; // Fallback to original name if not found
    }


    /// <summary>
    /// Gets the JavaScript property name (ColumnName) given a C# property name.
    /// </summary>
    public static string GetJsPropertyName<T>(string csharpPropertyName)
    {
        return GetJsPropertyName(csharpPropertyName, typeof(T));
    }

    public static string GetJsPropertyName<T>(PropertyInfo prop)
    {
        return GetJsPropertyName(prop.Name, typeof(T));
    }

    public static string GetJsPropertyName(PropertyInfo prop, Type type)
    {
        return GetJsPropertyName(prop.Name, type);
    }

    /// <summary>
    /// Gets the JavaScript property name (ColumnName) given a C# property name.
    /// </summary>
    public static string GetJsPropertyName(string csharpPropertyName, Type type)
    {
        EnsureTypeIsCached(type);
        string typeKey = type.FullName!;

        try
        {
            if (_propertyCache.TryGetValue(type, out var properties) &&
                properties.propertyEntries.TryGetValue(csharpPropertyName, out var entry))
            {
                return entry.JsPropertyName;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving JS property name for C# property '{csharpPropertyName}' in type {type.FullName}.", ex);
        }

        return csharpPropertyName; // Fallback to original name if not found
    }

    /// <summary>
    /// Gets the cached MagicPropertyEntry for a given property name.
    /// </summary>
    public static MagicPropertyEntry GetPropertyEntry<T>(string propertyName)
    {
        return GetPropertyEntry(propertyName, typeof(T));
    }

    /// <summary>
    /// Gets the cached MagicPropertyEntry for a given property name.
    /// </summary>
    public static MagicPropertyEntry GetPropertyEntry(string propertyName, Type type)
    {
        EnsureTypeIsCached(type);
        string typeKey = type.FullName!;

        try
        {
            if (_propertyCache.TryGetValue(type, out var properties) &&
                properties.propertyEntries.TryGetValue(propertyName, out var entry))
            {
                return entry;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving property entry for '{propertyName}' in type {type.FullName}.", ex);
        }

        throw new Exception($"Error: Property '{propertyName}' not found in type {type.FullName}.");
    }

    /// <summary>
    /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
    /// </summary>
    public static MagicPropertyEntry GetPropertyEntry<T>(PropertyInfo property)
    {
        return GetPropertyEntry(property.Name, typeof(T));
    }

    /// <summary>
    /// Gets the cached MagicPropertyEntry for a given PropertyInfo reference.
    /// </summary>
    public static MagicPropertyEntry GetPropertyEntry(PropertyInfo property, Type type)
    {
        return GetPropertyEntry(property.Name, type);
    }

    /// <summary>
    /// Ensures that both schema and property caches are built for the given type.
    /// </summary>
    internal static void EnsureTypeIsCached<T>()
    {
        Type type = typeof(T);
    }

    internal static void EnsureTypeIsCached(Type type)
    {
        //string typeKey = type.FullName!;

        // Avoid re-registering types if the typeKey already exists
        if (_propertyCache.ContainsKey(type))
            return;

        // Ensure schema metadata is cached
        SchemaHelper.EnsureSchemaIsCached(type);

        // Initialize the dictionary for this type
        var propertyEntries = new Dictionary<string, MagicPropertyEntry>(StringComparer.OrdinalIgnoreCase);

        bool isMagicTable = SchemaHelper.HasMagicTableInterface(type);

        var instance = Activator.CreateInstance(type) as IMagicTableBase;

        IMagicCompoundKey compoundKey;
        List<IMagicCompoundIndex>? compoundIndexes = new List<IMagicCompoundIndex>();
        HashSet<string> keyNames = new HashSet<string>();
        if (instance != null)
        {
            compoundKey = instance.GetKeys();
            compoundIndexes = instance.GetCompoundIndexes();
            keyNames = new HashSet<string>(compoundKey.PropertyInfos.Select(p => p.Name));
        }

        List<MagicPropertyEntry> newMagicPropertyEntry = new List<MagicPropertyEntry>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
        {
            if (property.GetIndexParameters().Length > 0)
                continue; // 🔥 Skip indexers entirely

            string propertyKey = property.Name; // Now stored as string, not PropertyInfo

            var columnAttribute = GetPropertyColumnAttribute(property);

            bool isPrimaryKey = keyNames.Contains(property.Name);

            bool isCompoundIndexed = false;
            if(compoundIndexes != null && compoundIndexes.Any())
            {
                isCompoundIndexed = compoundIndexes.SelectMany(x => x.PropertyInfos).Any(x => x.Name == property.Name);
            }

            var magicEntry = new MagicPropertyEntry(
                property,
                columnAttribute,
                property.IsDefined(typeof(MagicIndexAttribute), inherit: true)
                || isPrimaryKey || isCompoundIndexed
                ,
                property.IsDefined(typeof(MagicUniqueIndexAttribute), inherit: true),
                isPrimaryKey,
                property.IsDefined(typeof(MagicNotMappedAttribute), inherit: true),
                isMagicTable
                || property.IsDefined(typeof(MagicNameAttribute), inherit: true)
            );

            newMagicPropertyEntry.Add(magicEntry);
            propertyEntries[propertyKey] = magicEntry; // Store property entry with string key
        }

        // 🔥 Extract constructor metadata
        var constructors = type.GetConstructors();

        // Cache the properties for this type
        _propertyCache[type] = new SearchPropEntry(type, propertyEntries,
            constructors);

        var complexTypes = GetAllNestedComplexTypes(newMagicPropertyEntry.Select(x => x.Property));
        if (complexTypes != null && complexTypes.Any())
        {
            foreach (var comp in complexTypes)
            {
                EnsureTypeIsCached(comp);
            }
        }
    }

    internal static IColumnNamed? GetPropertyColumnAttribute(PropertyInfo property)
    {
        var columnAttribute = property.GetCustomAttributes()
            .FirstOrDefault(attr => attr is IColumnNamed) as IColumnNamed;

        if (columnAttribute != null && string.IsNullOrWhiteSpace(columnAttribute.ColumnName))
        {
            columnAttribute = null;
        }

        return columnAttribute;
    }

    internal static string GetJsPropertyNameNoCache(IColumnNamed? columnAttribute, string PropertyName)
    {
        return columnAttribute?.ColumnName ?? PropertyName;
    }
}