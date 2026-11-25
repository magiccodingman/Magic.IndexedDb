using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Magic.IndexedDb;

public enum BlazorInteropMode : long
{
    /// <summary>
    /// SignalR default interop send/receive is 32 KB. 
    /// This will default to 31 KB for safety.
    /// </summary>
    SignalR = 31 * 1024,       // 31 KB in bytes

    /// <summary>
    /// WASM default interop send/receive is 16 MB. 
    /// This will default to 15 MB for safety.
    /// </summary>
    WASM = 15 * 1024 * 1024    // 15 MB in bytes
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMagicBlazorDB(this IServiceCollection services, 
        BlazorInteropMode interoptMode, bool isDebug)
    {
        return services.AddMagicBlazorDB((long)interoptMode, isDebug);
    }

    public static IServiceCollection AddMagicBlazorDB(this IServiceCollection services,
        long jsMessageSizeBytes, bool isDebug) =>
        Register(services, jsMessageSizeBytes, isDebug);

    /// <summary>Registers Magic using only the supplied table and repository types for schema discovery.</summary>
    public static IServiceCollection AddMagicBlazorDB(this IServiceCollection services,
        BlazorInteropMode interoptMode, bool isDebug, IEnumerable<Type> tableTypes, IEnumerable<Type> repositoryTypes) =>
        services.AddMagicBlazorDB((long)interoptMode, isDebug, tableTypes, repositoryTypes);

    /// <summary>Registers Magic using only the supplied table and repository types for schema discovery.</summary>
    public static IServiceCollection AddMagicBlazorDB(this IServiceCollection services,
        long jsMessageSizeBytes, bool isDebug, IEnumerable<Type> tableTypes, IEnumerable<Type> repositoryTypes)
    {
        var tables = ValidateSchemaTypes(tableTypes, nameof(tableTypes),
            type => SchemaHelper.ImplementsIMagicTable(type) && type.GetConstructor(Type.EmptyTypes) is not null,
            "an IMagicTable<> implementation with a public parameterless constructor");
        var repositories = ValidateSchemaTypes(repositoryTypes, nameof(repositoryTypes),
            SchemaHelper.ImplementsIMagicRepository, "an IMagicRepository implementation");

        foreach (var repository in repositories)
        {
            var canCreateInstance = repository.GetConstructor(Type.EmptyTypes) is not null;
            if (!SchemaHelper.GetIndexedDbSetFields(repository).Any(field => field.IsStatic || canCreateInstance))
            {
                throw new ArgumentException(
                    $"Repository '{repository}' must declare an IndexedDbSet field. Instance fields require a public parameterless constructor.",
                    nameof(repositoryTypes));
            }
        }

        return Register(services, jsMessageSizeBytes, isDebug, tables, repositories);
    }

    /// <summary>Registers account-bound encryption policies and an application-supplied key provider.</summary>
    public static IServiceCollection AddMagicIndexedDbEncryption<TProvider>(this IServiceCollection services,
        Func<IServiceProvider, IEnumerable<MagicEncryptionOptions>> configure)
        where TProvider : class, IMagicEncryptionKeyProvider
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddScoped<IMagicEncryptionKeyProvider, TProvider>();
        services.AddScoped(sp => configure(sp).Select(options => new EncryptionConfiguration(options))
            .ToDictionary(config => config.Request.DatabaseName, StringComparer.Ordinal));
        services.AddScoped<IMagicIndexedDbEncryption>(sp =>
            (IMagicIndexedDbEncryption)sp.GetRequiredService<IMagicIndexedDb>());
        services.AddScoped<IMagicIndexedDbEncryptionLifecycle>(sp =>
            (IMagicIndexedDbEncryptionLifecycle)sp.GetRequiredService<IMagicIndexedDb>());
        services.AddScoped<IMagicEncryptionContexts>(sp =>
            new MagicEncryptionContexts((MagicDbFactory)sp.GetRequiredService<IMagicIndexedDb>()));
        return services;
    }

    /// <summary>Registers an application key provider for runtime account activation.</summary>
    public static IServiceCollection AddMagicIndexedDbEncryption<TProvider>(this IServiceCollection services)
        where TProvider : class, IMagicEncryptionKeyProvider =>
        services.AddMagicIndexedDbEncryption<TProvider>(_ => []);

    /// <summary>Registers per-database numeric storage. Unlisted databases retain the Legacy format.</summary>
    /// <remarks>Lossless databases use a separate physical namespace. Existing Legacy data requires an explicit migration or rebuild.</remarks>
    public static IServiceCollection AddMagicIndexedDbNumericStorage(this IServiceCollection services,
        params MagicNumericStorageOptions[] databases)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databases);
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(NumericStorageConfiguration))
            .Select(descriptor => (NumericStorageConfiguration)descriptor.ImplementationInstance!)
            .SelectMany(configuration => configuration.Modes.Select(pair => new MagicNumericStorageOptions(pair.Key, pair.Value)));
        var configuration = new NumericStorageConfiguration(existing.Concat(databases));
        services.RemoveAll<NumericStorageConfiguration>();
        services.AddSingleton(configuration);
        return services;
    }

    private static Type[] ValidateSchemaTypes(IEnumerable<Type> types, string parameterName,
        Func<Type, bool> isValidType, string expectedType)
    {
        ArgumentNullException.ThrowIfNull(types, parameterName);

        var snapshot = types.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Register at least one type.", parameterName);
        }

        var seen = new HashSet<Type>();
        foreach (var type in snapshot)
        {
            if (type is null || !type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !isValidType(type))
            {
                throw new ArgumentException(
                    $"'{type?.ToString() ?? "null"}' must be a concrete, closed class that is {expectedType}.",
                    parameterName);
            }

            if (!seen.Add(type))
            {
                throw new ArgumentException($"Type '{type}' was registered more than once.", parameterName);
            }
        }

        return snapshot;
    }

    private static IServiceCollection Register(IServiceCollection services, long jsMessageSizeBytes, bool isDebug,
        Type[]? tableTypes = null, Type[]? repositoryTypes = null)
    {
        services.AddScoped<IMagicIndexedDb>(sp =>
            new MagicDbFactory(sp.GetRequiredService<IJSRuntime>(), jsMessageSizeBytes, isDebug, tableTypes, repositoryTypes,
                sp.GetService<Dictionary<string, EncryptionConfiguration>>(), sp.GetService<IMagicEncryptionKeyProvider>(),
                numericStorage: sp.GetService<NumericStorageConfiguration>()));

        if (isDebug)
        {
            MagicValidator.ValidateTables(tableTypes?.ToList());
        }

        return services;
    }
}
