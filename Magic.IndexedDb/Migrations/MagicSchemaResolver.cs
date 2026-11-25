using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.Migrations;

/// <summary>Resolves the one persisted model used by runtime creation and migration scaffolding.</summary>
public static class MagicSchemaResolver
{
    public static IReadOnlyList<ResolvedMagicDatabase> Resolve(IEnumerable<Type> tableTypes,
        IEnumerable<Type> repositoryTypes, MagicSchemaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryTypes);
        var repositories = repositoryTypes.Distinct().ToArray();
        var definitions = new List<IndexedDbSet>();
        foreach (var repository in repositories)
        {
            if (repository is null || !repository.IsClass || repository.IsAbstract || repository.ContainsGenericParameters ||
                !SchemaHelper.ImplementsIMagicRepository(repository))
                throw Error("MAGDB3001", $"'{repository}' is not a concrete IMagicRepository.");

            object? instance = null;
            var fields = SchemaHelper.GetIndexedDbSetFields(repository).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            if (fields.Length == 0)
                throw Error("MAGDB3001", $"Repository '{repository.Name}' has no IndexedDbSet fields.");
            foreach (var field in fields)
            {
                if (!field.IsStatic)
                {
                    if (repository.GetConstructor(Type.EmptyTypes) is null)
                        throw Error("MAGDB3001", $"Repository '{repository.Name}' needs a public parameterless constructor for instance declarations.");
                    instance ??= Activator.CreateInstance(repository);
                }
                if (field.GetValue(instance) is not IndexedDbSet definition)
                    throw Error("MAGDB3001", $"Repository field '{repository.Name}.{field.Name}' returned no database declaration.");
                definitions.Add(definition);
            }
        }
        return ResolveDefinitions(tableTypes, definitions, options);
    }

    /// <summary>Resolves already-discovered declarations without scanning the process's assemblies.</summary>
    public static IReadOnlyList<ResolvedMagicDatabase> ResolveDefinitions(IEnumerable<Type> tableTypes,
        IEnumerable<IndexedDbSet> databaseDefinitions, MagicSchemaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tableTypes);
        ArgumentNullException.ThrowIfNull(databaseDefinitions);
        options ??= new();
        var definitions = databaseDefinitions.Distinct().ToArray();
        if (definitions.Any(definition => definition is null))
            throw Error("MAGDB3001", "Database declarations cannot be null.");

        var byName = new Dictionary<string, IndexedDbSet>(StringComparer.Ordinal);
        var schemaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ValidateSchemaId(definition.SchemaId);
            ValidateName(definition.DatabaseName, "database");
            if (!byName.TryAdd(definition.DatabaseName, definition))
                throw Error("MAGDB3004", $"Database '{definition.DatabaseName}' has multiple declarations. Reuse one declaration for dynamic instances.");
            if (!schemaIds.Add(definition.SchemaId))
                throw Error("MAGDB3004", $"Schema identity '{definition.SchemaId}' is declared more than once.");
            if (definition.IncludedTypes.Intersect(definition.ExcludedTypes).Any())
                throw Error("MAGDB3008", $"Schema '{definition.SchemaId}' includes and excludes the same table.");
        }

        foreach (var database in options.NumericModes.Keys.Concat(options.RecordPolicies.Keys).Concat(options.ProtectedDatabases).Distinct())
            if (!byName.ContainsKey(database))
                throw Error("MAGDB3002", $"Storage configuration names unregistered database '{database}'.");

        var protectedDatabases = options.ProtectedDatabases.ToHashSet(StringComparer.Ordinal);
        foreach (var database in options.RecordPolicies.Keys)
            if (!protectedDatabases.Contains(database))
                throw Error("MAGDB3011", $"Record protection for '{database}' requires that database in ProtectedDatabases.");

        var types = tableTypes.Concat(definitions.SelectMany(definition => definition.IncludedTypes)).Distinct().ToArray();
        var defaults = new Dictionary<Type, IndexedDbSet>();
        foreach (var type in types)
        {
            if (type is null || !type.IsClass || type.IsAbstract || type.ContainsGenericParameters ||
                !SchemaHelper.ImplementsIMagicTable(type) || type.GetConstructor(Type.EmptyTypes) is null)
                throw Error("MAGDB3001", $"'{type}' must be a concrete IMagicTable<> with a public parameterless constructor.");
            try
            {
                var table = (IMagicTableBase)Activator.CreateInstance(type)!;
                defaults[type] = table.GetDefaultDatabase() ??
                    throw Error("MAGDB3002", $"Table '{type.Name}' has no default database.");
            }
            catch (MagicSchemaException) { throw; }
            catch (Exception error)
            {
                throw new MagicSchemaException("MAGDB3001", $"Could not evaluate table '{type.Name}' at design time.", error);
            }
        }

        var protectedTypes = new HashSet<Type>();
        foreach (var type in types)
        {
            try { if (EncryptionConfiguration.RequiresProtection(type)) protectedTypes.Add(type); }
            catch (Exception error)
            {
                throw new MagicSchemaException("MAGDB3011", $"Invalid protection declaration for '{type.Name}'. " + error.Message, error);
            }
        }
        var membership = definitions.ToDictionary(definition => definition, definition =>
        {
            var protectedDatabase = protectedDatabases.Contains(definition.DatabaseName);
            if (!protectedDatabase && definition.IncludedTypes.Any(protectedTypes.Contains))
                throw Error("MAGDB3011", $"Schema '{definition.SchemaId}' explicitly includes a protected table, but its database is not in ProtectedDatabases.");
            var selected = definition.DiscoverDefaultTables
                ? types.Where(type => defaults[type].DatabaseName == definition.DatabaseName &&
                    (protectedDatabase || !protectedTypes.Contains(type))).ToHashSet()
                : [];
            selected.UnionWith(definition.IncludedTypes);
            selected.ExceptWith(definition.ExcludedTypes);
            return selected;
        });
        var selectedTypes = membership.Values.SelectMany(selected => selected).Distinct().ToArray();
        // Explicitly scoped callers may reuse a model without registering its
        // default database. The runtime checks default routing when Query<T>() is
        // actually used; membership resolution must not prevent this reuse.

        try { MagicValidator.ValidateTables(selectedTypes.ToList()); }
        catch (Exception error)
        {
            throw new MagicSchemaException("MAGDB3001", "The persisted model failed Magic table validation. " + error.Message, error);
        }

        var result = new List<ResolvedMagicDatabase>();
        foreach (var definition in definitions.OrderBy(definition => definition.SchemaId, StringComparer.Ordinal))
        {
            var mode = options.NumericModes.GetValueOrDefault(definition.DatabaseName);
            if (!Enum.IsDefined(mode)) throw Error("MAGDB3010", $"Database '{definition.DatabaseName}' has an unknown numeric format.");
            var tableRows = membership[definition].Select(type => (Type: type, Native: SchemaHelper.GetStoreSchema(type)))
                .OrderBy(row => row.Native.TableName, StringComparer.Ordinal).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            options.RecordPolicies.TryGetValue(definition.DatabaseName, out var policies);
            foreach (var row in tableRows)
                if (!names.Add(row.Native.TableName))
                    throw Error("MAGDB3005", $"Two table types map to store '{row.Native.TableName}' in '{definition.SchemaId}'.");
            if (policies is not null && policies.Keys.Any(name => !names.Contains(name)))
                throw Error("MAGDB3011", $"Record protection for '{definition.SchemaId}' names a store outside its membership.");

            var stores = tableRows.Select(row => BuildStore(row.Type, row.Native, mode, policies)).ToArray();
            var schema = new MagicSchemaDocument
            {
                SchemaId = definition.SchemaId,
                NumericFormat = mode == MagicNumericStorageMode.Lossless ? "lossless-v1" : "legacy",
                Stores = Array.AsReadOnly(stores)
            };
            result.Add(new ResolvedMagicDatabase(definition, schema,
                tableRows.Select(row => row.Type).ToArray(), tableRows.Select(row => row.Native).ToArray()));
        }
        return result.AsReadOnly();
    }

    public static void ValidateSchemaId(string schemaId)
    {
        if (schemaId is null || schemaId.Length > 128 ||
            !Regex.IsMatch(schemaId, "\\A[A-Za-z0-9][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant))
            throw Error("MAGDB3004", "Schema IDs must contain 1–128 ASCII letters, digits, dots, underscores or hyphens and begin with a letter or digit. Use WithSchemaId for a database name with other characters.");
    }

    private static MagicStoreDefinition BuildStore(Type type, StoreSchema native, MagicNumericStorageMode mode,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? policies)
    {
        ValidateName(native.TableName, "store");
        var keys = native.ColumnNamesInCompoundKey;
        if (keys.Count == 0) throw Error("MAGDB3006", $"Store '{native.TableName}' has no primary key.");
        if (native.PrimaryKeyAuto && keys.Count != 1)
            throw Error("MAGDB3006", $"Store '{native.TableName}' cannot auto-increment a compound key.");
        foreach (var key in keys) ValidatePath(key);
        var indexes = new Dictionary<string, MagicIndexDefinition>(StringComparer.Ordinal);
        foreach (var member in native.Indexes.Concat(native.UniqueIndexes).Distinct(StringComparer.Ordinal))
        {
            ValidatePath(member);
            indexes.Add(member, new() { Name = member, KeyPath = member, Unique = native.UniqueIndexes.Contains(member) });
        }
        foreach (var parts in native.ColumnNamesInCompoundIndex)
        {
            if (parts.Count == 0 || parts.Distinct(StringComparer.Ordinal).Count() != parts.Count)
                throw Error("MAGDB3006", $"Store '{native.TableName}' has an empty or repeated compound index member.");
            foreach (var part in parts) ValidatePath(part);
            var name = $"[{string.Join('+', parts)}]";
            if (!indexes.TryAdd(name, new() { Name = name, KeyPath = parts.ToArray() }))
                throw Error("MAGDB3005", $"Store '{native.TableName}' has duplicate index '{name}'.");
        }

        var mapped = PropertyMappingCache.GetTypeOfTProperties(type).propertyEntries.Values
            .Where(entry => !entry.NotMapped && entry.Property.GetIndexParameters().Length == 0).ToArray();
        var encrypted = EncryptionConfiguration.ProtectedFields(type).Order(StringComparer.Ordinal).ToArray();
        IReadOnlyCollection<string>? configured = null;
        policies?.TryGetValue(native.TableName, out configured);
        var annotation = type.GetCustomAttribute<MagicEncryptRecordAttribute>(true);
        var annotated = annotation?.ExposedProperties.Select(property => mapped
                .SingleOrDefault(entry => entry.CsharpPropertyName == property).Property is { } member
                    ? PropertyMappingCache.GetJsPropertyName(member, type)
                    : throw Error("MAGDB3011", $"Protection for '{native.TableName}' names unknown property '{property}'."))
            .Order(StringComparer.Ordinal).ToArray();
        if (configured is not null && configured.Any(name => !mapped.Any(entry => entry.JsPropertyName == name)))
            throw Error("MAGDB3011", $"Protection for '{native.TableName}' names an unknown persisted property.");
        if (annotated is not null && configured is not null && !annotated.ToHashSet(StringComparer.Ordinal).SetEquals(configured))
            throw Error("MAGDB3011", $"Record policy for '{native.TableName}' conflicts with its annotation.");
        var exposed = annotated ?? configured?.Order(StringComparer.Ordinal).ToArray();
        var protectedStore = encrypted.Length > 0 || exposed is not null;
        if (protectedStore)
        {
            if (native.PrimaryKeyAuto) throw Error("MAGDB3011", $"Protected store '{native.TableName}' needs an explicit key.");
            foreach (var member in keys.Concat(native.Indexes).Concat(native.UniqueIndexes).Concat(native.ColumnNamesInCompoundIndex.SelectMany(parts => parts)))
                if (encrypted.Contains(member) || (exposed is not null && !exposed.Contains(member)))
                    throw Error("MAGDB3011", $"Key/index '{native.TableName}.{member}' must remain public.");
            if (exposed is not null && encrypted.Intersect(exposed).Any())
                throw Error("MAGDB3011", $"Store '{native.TableName}' both encrypts and exposes a property.");
        }

        string? recordCodec = null;
        var declaredRecordCodec = type.GetCustomAttribute<MagicStorageCodecAttribute>(true);
        if (MagicConverterHelper.HasCustomConverter(type, new JsonSerializerOptions()))
        {
            recordCodec = DeclaredCodec(declaredRecordCodec, type.Name);
            if (encrypted.Length > 0 && exposed is null)
                throw Error("MAGDB3011", $"Custom record codec for '{native.TableName}' needs whole-record protection instead of field-only encryption.");
        }
        else if (declaredRecordCodec is not null)
            throw Error("MAGDB3012", $"Storage codec on '{type.Name}' has no JSON converter to describe.");
        var fields = recordCodec is null
            ? BuildFields(type, mode, new Dictionary<(Type, bool), string> { [(type, false)] = "$" }, "$", native.PrimaryKeyAuto ? keys.Single() : null)
            : [];
        return new()
        {
            Name = native.TableName,
            KeyPath = keys.Count == 1 ? keys[0] : keys.ToArray(),
            AutoIncrement = native.PrimaryKeyAuto,
            Indexes = indexes.Values.OrderBy(index => index.Name, StringComparer.Ordinal).ToArray(),
            Fields = fields,
            EncryptedFields = protectedStore ? encrypted : null,
            ExposedFields = exposed,
            RecordCodec = recordCodec
        };
    }

    private static IReadOnlyList<MagicFieldDefinition> BuildFields(Type type, MagicNumericStorageMode mode,
        Dictionary<(Type, bool), string> path, string currentPath, string? autoKey = null)
    {
        var properties = PropertyMappingCache.GetTypeOfTProperties(type).propertyEntries.Values
            .Where(entry => !entry.NotMapped && entry.Property.GetIndexParameters().Length == 0 && entry.Property.CanRead).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();
        var fields = new List<MagicFieldDefinition>();
        foreach (var entry in properties)
        {
            var name = entry.NeverCamelCase ? entry.JsPropertyName :
                char.ToLowerInvariant(entry.JsPropertyName[0]) + entry.JsPropertyName[1..];
            if (!names.Add(name)) throw Error("MAGDB3005", $"Type '{type.Name}' contains duplicate persisted field '{name}'.");
            ValidatePath(name);
            var property = entry.Property;
            var nullabilityInfo = nullability.Create(property);
            var nullable = IsNullable(property.PropertyType, nullabilityInfo);
            var field = DescribeValue(property.PropertyType, name, nullable, mode, path, currentPath + "." + name,
                property.GetCustomAttribute<JsonConverterAttribute>(true), property.GetCustomAttribute<MagicStorageCodecAttribute>(true),
                nullability: nullabilityInfo);
            if (name == autoKey && mode == MagicNumericStorageMode.Lossless)
            {
                if (MagicConverterHelper.HasCustomConverter(property))
                    throw Error("MAGDB3012", $"Auto-increment key '{type.Name}.{property.Name}' cannot have a custom converter in lossless mode.");
                field = field with { Codec = "native-safe-integer" };
            }
            fields.Add(field);
        }
        return fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
    }

    private static MagicFieldDefinition DescribeValue(Type original, string name, bool nullable, MagicNumericStorageMode mode,
        Dictionary<(Type, bool), string> path, string currentPath, JsonConverterAttribute? propertyConverter = null,
        MagicStorageCodecAttribute? propertyCodec = null, bool systemTextJson = false, NullabilityInfo? nullability = null)
    {
        var type = Nullable.GetUnderlyingType(original) ?? original;
        var converter = propertyConverter ?? type.GetCustomAttribute<JsonConverterAttribute>(true);
        var codec = propertyCodec ?? type.GetCustomAttribute<MagicStorageCodecAttribute>(true);
        var isStringEnum = type.IsEnum && converter?.ConverterType is { } converterType &&
            (converterType == typeof(JsonStringEnumConverter) ||
             converterType.IsGenericType && converterType.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>));
        var field = new MagicFieldDefinition { Name = name, Type = Kind(type), Nullable = nullable };
        if (converter is not null && !isStringEnum)
            return field with { Type = "custom", Codec = DeclaredCodec(codec, currentPath) };
        if (codec is not null && converter is null)
            throw Error("MAGDB3012", $"Storage codec at '{currentPath}' has no JSON converter to describe.");

        if (type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(type);
            var values = Enum.GetNames(type).Order(StringComparer.Ordinal).ToDictionary(member => member,
                member => Convert.ChangeType(Enum.Parse(type, member), underlying, CultureInfo.InvariantCulture)!.ToString()!, StringComparer.Ordinal);
            return field with
            {
                Type = "enum",
                Codec = isStringEnum ? "json:string-enum" :
                    mode == MagicNumericStorageMode.Lossless && (underlying == typeof(long) || underlying == typeof(ulong))
                        ? "numeric:lossless-v1" : "json:number-enum",
                EnumValues = values,
                EnumUnderlyingType = Kind(underlying),
                EnumFlags = type.IsDefined(typeof(FlagsAttribute), false),
                EnumNames = isStringEnum ? Enum.GetNames(type).Order(StringComparer.Ordinal).ToDictionary(member => member,
                    member => type.GetField(member)!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? member,
                    StringComparer.Ordinal) : null
            };
        }
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(decimal))
            return field with { Codec = mode == MagicNumericStorageMode.Lossless ? "numeric:lossless-v1" : "json:number" };
        if (systemTextJson && type == typeof(byte[]))
            return field with { Type = "binary", Codec = "json:base64" };
        if (field.Type != "object" && field.Type != "array" && field.Type != "dictionary") return field;
        if (path.TryGetValue((type, systemTextJson), out var reference)) return field with { Reference = reference };
        var nextPath = new Dictionary<(Type, bool), string>(path) { [(type, systemTextJson)] = currentPath };
        if (field.Type is "array" or "dictionary")
        {
            var dictionary = DictionaryContract(type);
            var itemType = type.IsArray ? type.GetElementType()! : dictionary?.GenericTypeArguments[1] ??
                type.GetInterfaces().Append(type).FirstOrDefault(candidate => candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GenericTypeArguments[0] ?? typeof(object);
            var itemNullability = type.IsArray ? nullability?.ElementType :
                nullability?.GenericTypeArguments.ElementAtOrDefault(dictionary is null ? 0 : 1);
            var item = DescribeValue(itemType, "$item", IsNullable(itemType, itemNullability),
                mode, nextPath, currentPath + "[]", systemTextJson: systemTextJson || field.Type == "dictionary",
                nullability: itemNullability);
            var keyType = dictionary?.GenericTypeArguments[0] ?? typeof(object);
            var keys = field.Type == "dictionary" ? DescribeValue(keyType, "$key", false, mode, nextPath,
                currentPath + "{key}", systemTextJson: true) : null;
            if (keys is not null && keys.Type != "custom")
                keys = keys with
                {
                    Codec = keyType.IsEnum ? "json:enum-property-name" : "json:property-name",
                    EnumNames = keyType.IsEnum ? Enum.GetNames(keyType).Order(StringComparer.Ordinal)
                        .ToDictionary(member => member, member => keyType.GetField(member)!
                            .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? member, StringComparer.Ordinal) : null
                };
            return field with { Items = item, DictionaryKeys = keys, Codec = field.Type == "dictionary" ? "system-text-json" : "json" };
        }
        return field with { Properties = systemTextJson ? BuildPassthroughFields(type, mode, nextPath, currentPath) :
            BuildFields(type, mode, nextPath, currentPath) };
    }

    // Dictionaries delegate to System.Text.Json, which obeys JsonPropertyName /
    // JsonIgnore and public-property rules rather than Magic's mapped-property rules.
    private static IReadOnlyList<MagicFieldDefinition> BuildPassthroughFields(Type type, MagicNumericStorageMode mode,
        Dictionary<(Type, bool), string> path, string currentPath)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        if (type.IsDefined(typeof(JsonPolymorphicAttribute), false) || type.IsDefined(typeof(JsonDerivedTypeAttribute), false))
            throw Error("MAGDB3012", $"Polymorphic System.Text.Json data at '{currentPath}' needs an explicit JSON converter and versioned MagicStorageCodec.");
        JsonTypeInfo contract;
        try { contract = new DefaultJsonTypeInfoResolver().GetTypeInfo(type, options); }
        catch (Exception error)
        {
            throw new MagicSchemaException("MAGDB3012", $"Cannot describe System.Text.Json's persisted contract at '{currentPath}'.", error);
        }
        if (contract.NumberHandling is not null and not JsonNumberHandling.Strict)
            throw Error("MAGDB3012", $"Custom JSON number handling at '{currentPath}' needs an explicit JSON converter and versioned MagicStorageCodec.");
        var nullability = new NullabilityInfoContext();
        var fields = new List<MagicFieldDefinition>();
        foreach (var property in contract.Properties.Where(property => property.Get is not null))
        {
            var attributes = property.AttributeProvider;
            var ignore = attributes?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).OfType<JsonIgnoreAttribute>().SingleOrDefault();
            if (ignore?.Condition is JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenWriting) continue;
            if (property.IsExtensionData || property.NumberHandling is not null and not JsonNumberHandling.Strict)
                throw Error("MAGDB3012", $"Extension data or custom JSON number handling at '{currentPath}.{property.Name}' needs an explicit JSON converter and versioned MagicStorageCodec.");
            ValidatePath(property.Name);
            var nullabilityInfo = attributes switch
            {
                PropertyInfo member => nullability.Create(member),
                FieldInfo member => nullability.Create(member),
                _ => null
            };
            var field = DescribeValue(property.PropertyType, property.Name,
                IsNullable(property.PropertyType, nullabilityInfo), mode, path, currentPath + "." + property.Name,
                attributes?.GetCustomAttributes(typeof(JsonConverterAttribute), true).OfType<JsonConverterAttribute>().SingleOrDefault(),
                attributes?.GetCustomAttributes(typeof(MagicStorageCodecAttribute), true).OfType<MagicStorageCodecAttribute>().SingleOrDefault(),
                systemTextJson: true, nullability: nullabilityInfo);
            field = field with { WriteCondition = ignore?.Condition switch
            {
                JsonIgnoreCondition.WhenWritingNull => "omit-null",
                JsonIgnoreCondition.WhenWritingDefault => "omit-default",
                _ => null
            } };
            fields.Add(field);
        }
        return fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
    }

    private static bool IsNullable(Type type, NullabilityInfo? nullability) =>
        Nullable.GetUnderlyingType(type) is not null || (!type.IsValueType && nullability?.ReadState != NullabilityState.NotNull);

    private static Type? DictionaryContract(Type type) => type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
        candidate.IsGenericType && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                                   candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

    private static string Kind(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(char)) return "character";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(sbyte)) return "integer8";
        if (type == typeof(byte)) return "unsigned8";
        if (type == typeof(short)) return "integer16";
        if (type == typeof(ushort)) return "unsigned16";
        if (type == typeof(int)) return "integer32";
        if (type == typeof(uint)) return "unsigned32";
        if (type == typeof(long)) return "integer64";
        if (type == typeof(ulong)) return "unsigned64";
        if (type == typeof(float)) return "float32";
        if (type == typeof(double)) return "float64";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(DateTime)) return "datetime";
        if (type == typeof(DateTimeOffset)) return "datetime-offset";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(TimeOnly)) return "time";
        if (type == typeof(TimeSpan)) return "duration";
        if (type == typeof(Guid)) return "uuid";
        if (type == typeof(Uri)) return "uri";
        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument)) return "any";
        if (DictionaryContract(type) is not null || typeof(IDictionary).IsAssignableFrom(type)) return "dictionary";
        if (typeof(IEnumerable).IsAssignableFrom(type)) return "array";
        return "object";
    }

    private static string DeclaredCodec(MagicStorageCodecAttribute? codec, string location) => codec is null
        ? throw Error("MAGDB3012", $"Custom converter at '{location}' requires [MagicStorageCodec(\"portable-codec-id\", version: 1)]. Increment its version when persisted semantics change.")
        : $"{codec.Id}:{codec.Version}";

    private static void ValidateName(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name.StartsWith("__magic_", StringComparison.Ordinal))
            throw Error("MAGDB3006", $"Invalid or reserved {kind} name '{name}'.");
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Split('.').Any(part => string.IsNullOrEmpty(part) ||
            part is "__proto__" or "prototype" or "constructor") || path.Any(char.IsControl))
            throw Error("MAGDB3006", $"Invalid or reserved persisted path '{path}'.");
    }

    private static MagicSchemaException Error(string code, string message) => new(code, message);
}
