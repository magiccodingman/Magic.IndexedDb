using System.Text.Json;
using Magic.IndexedDb.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TestBase.Models;
using TestBase.Repository;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ExplicitSchemaRegistrationTests
{
    [TestMethod]
    public async Task FirstQuery_UsesOnlyTheRegisteredSchemaInEveryRegisteredDatabase()
    {
        var runtime = new RecordingJsRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(runtime);
        Type[] tableTypes = [typeof(Person), typeof(CompositeRecord)];
        Type[] repositoryTypes = [typeof(IndexDbContext)];

        services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: true, tableTypes, repositoryTypes);
        tableTypes[0] = typeof(ContractRecord);
        repositoryTypes[0] = typeof(string);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IMagicIndexedDb>();
        Assert.AreEqual(0, runtime.CreatedDatabases.Count);

        await db.Query<Person>();
        await db.Query<CompositeRecord>();

        var stores = runtime.CreatedDatabases;
        CollectionAssert.AreEquivalent(new[] { "Client", "Employee", "Animal" },
            stores.Select(store => store.GetProperty("name").GetString()).ToArray());

        foreach (var store in stores)
        {
            Assert.AreEqual(1, store.GetProperty("version").GetInt32());
            var schemas = store.GetProperty("storeSchemas").EnumerateArray()
                .ToDictionary(schema => schema.GetProperty("tableName").GetString()!);
            CollectionAssert.AreEquivalent(new[] { "Person", "CompositeRecord" }, schemas.Keys.ToArray());
            Assert.AreEqual("_id", schemas["Person"].GetProperty("columnNamesInCompoundKey")[0].GetString());
            CollectionAssert.AreEqual(new[] { "Tenant", "Sequence" },
                schemas["CompositeRecord"].GetProperty("columnNamesInCompoundKey").EnumerateArray()
                    .Select(key => key.GetString()).ToArray());
        }
    }

    [TestMethod]
    public void Registration_RejectsInvalidTypeListsBeforeResolvingTheService()
    {
        Type[] tables = [typeof(Person)];
        Type[] repositories = [typeof(IndexDbContext)];
        var services = new ServiceCollection();

        var nullTables = Assert.ThrowsExactly<ArgumentNullException>(() =>
            services.AddMagicBlazorDB(64, false, null!, repositories));
        Assert.AreEqual("tableTypes", nullTables.ParamName);

        Type[][] invalidTableLists = [[], [null!], [typeof(string)], [typeof(Person), typeof(Person)]];
        foreach (var invalidTables in invalidTableLists)
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() =>
                services.AddMagicBlazorDB(64, false, invalidTables, repositories));
            Assert.AreEqual("tableTypes", error.ParamName);
        }

        Type[][] invalidRepositoryLists =
        [
            [],
            [typeof(IMagicRepository)],
            [typeof(EmptyRepository)],
            [typeof(IndexDbContext), typeof(IndexDbContext)]
        ];
        foreach (var invalidRepositories in invalidRepositoryLists)
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() =>
                services.AddMagicBlazorDB(64, false, tables, invalidRepositories));
            Assert.AreEqual("repositoryTypes", error.ParamName);
        }

        Assert.AreEqual(0, services.Count);
    }

    private sealed class EmptyRepository : IMagicRepository;

    private sealed class UnregisteredRepository : IMagicRepository
    {
        public static readonly IndexedDbSet Unregistered = new(nameof(Unregistered));
    }

    private sealed class RecordingJsRuntime : IJSRuntime, IJSObjectReference
    {
        public List<JsonElement> CreatedDatabases { get; } = [];

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, default, args);

        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                    return (T)(object)this;
                case "configureDebug":
                case IndexedDbFunctions.CLOSE_ALL:
                    return default!;
                case "streamedJsHandler":
                    var request = (DotNetStreamReference)args![0]!;
                    using (var json = await JsonDocument.ParseAsync(request.Stream, cancellationToken: cancellationToken))
                    {
                        Assert.AreEqual("createDb", json.RootElement.GetProperty("methodName").GetString());
                        CreatedDatabases.Add(json.RootElement.GetProperty("parameters")[0].Clone());
                    }
                    return (T)(object)new BooleanResponse();
                default:
                    throw new InvalidOperationException($"Unexpected JS call '{identifier}'.");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BooleanResponse : IJSStreamReference
    {
        public long Length => 4;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream("true"u8.ToArray()));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
