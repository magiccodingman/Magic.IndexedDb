using System.Text.Json;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TestBase.Models;
using TestBase.Numeric;
using TestBase.Repository;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ExplicitSchemaRegistrationTests
{
    [TestMethod]
    public async Task FirstQuery_OpensOnlyItsDatabaseWithResolvedMembership()
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

        var client = runtime.CreatedDatabases.Single();
        Assert.AreEqual("Client", client.GetProperty("name").GetString());
        var schemas = client.GetProperty("storeSchemas").EnumerateArray()
            .ToDictionary(schema => schema.GetProperty("tableName").GetString()!);
        CollectionAssert.AreEquivalent(new[] { "Person", "CompositeRecord" }, schemas.Keys.ToArray());
        Assert.AreEqual("_id", schemas["Person"].GetProperty("columnNamesInCompoundKey")[0].GetString());
        CollectionAssert.AreEqual(new[] { "Tenant", "Sequence" },
            schemas["CompositeRecord"].GetProperty("columnNamesInCompoundKey").EnumerateArray()
                .Select(key => key.GetString()).ToArray());
        Assert.AreEqual("Client", client.GetProperty("migrationSchema").GetProperty("schemaId").GetString());
        Assert.AreEqual(1, client.GetProperty("migrationSchema").GetProperty("protocolVersion").GetInt32());

        await db.Query<Person>(person => person.Databases.Employee);
        var employee = runtime.CreatedDatabases.Single(database => database.GetProperty("name").GetString() == "Employee");
        CollectionAssert.AreEqual(new[] { "Person" }, employee.GetProperty("storeSchemas").EnumerateArray()
            .Select(schema => schema.GetProperty("tableName").GetString()).ToArray());
        Assert.AreEqual(2, runtime.CreatedDatabases.Count);
    }

    [TestMethod]
    public async Task NumericProjectionFixture_AllowsUnrelatedDatabaseStartupAndExportsItsConverterContract()
    {
        var runtime = new RecordingJsRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(runtime);
        services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
            [typeof(Person), typeof(NumericValueRecord)], [typeof(IndexDbContext), typeof(NumericRepository)]);
        services.AddMagicIndexedDbNumericStorage(new MagicNumericStorageOptions(NumericRepository.Lossless.DatabaseName));
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<IMagicIndexedDb>();

        // The first query resolves all registered models. A new unversioned
        // converter in the shared fixture must fail this test even when its own
        // database has not been requested yet.
        await db.Query<Person>();
        Assert.AreEqual("Client", runtime.CreatedDatabases.Single().GetProperty("name").GetString());

        var query = await db.Query<NumericValueRecord>();
        Assert.AreEqual(NumericRepository.Lossless.DatabaseName, query.DatabaseName);
        var numeric = runtime.CreatedDatabases.Single(database =>
            database.GetProperty("migrationSchema").GetProperty("schemaId").GetString() == NumericRepository.Lossless.SchemaId);
        var schema = numeric.GetProperty("migrationSchema");
        Assert.AreEqual("lossless-v1", schema.GetProperty("numericFormat").GetString());
        var fields = schema.GetProperty("stores").EnumerateArray()
            .Single(store => store.GetProperty("name").GetString() == nameof(NumericValueRecord))
            .GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("name").GetString()!);
        Assert.AreEqual("custom", fields[nameof(NumericValueRecord.Converted)].GetProperty("type").GetString());
        Assert.AreEqual("projection-int-prefix:1", fields[nameof(NumericValueRecord.Converted)].GetProperty("codec").GetString());
        Assert.AreEqual(2, runtime.CreatedDatabases.Count);
    }

    [TestMethod]
    public async Task UndeclaredTableOrDatabase_FailsBeforeBrowserMutation()
    {
        var runtime = new RecordingJsRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(runtime);
        services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
            [typeof(Person), typeof(CompositeRecord)], [typeof(IndexDbContext)]);
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<IMagicIndexedDb>();

        var member = await Assert.ThrowsExactlyAsync<MagicSchemaException>(async () =>
            await db.Query<CompositeRecord>(record => record.Databases.Employee));
        Assert.AreEqual("MAGDB3021", member.Code);
        var unregistered = await Assert.ThrowsExactlyAsync<MagicSchemaException>(async () =>
            await db.Query<Person>(_ => UnregisteredRepository.Unregistered));
        Assert.AreEqual("MAGDB3020", unregistered.Code);
        Assert.AreEqual(0, runtime.CreatedDatabases.Count);
    }

    [TestMethod]
    public async Task DynamicInstances_ShareTheSchemaWithoutChangingTheDeclaration()
    {
        var runtime = new RecordingJsRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(runtime);
        services.AddMagicBlazorDB(BlazorInteropMode.WASM, false, [typeof(Person)], [typeof(TemplateRepository)]);
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<IMagicIndexedDb>();

        var first = await db.Database(TemplateRepository.Template, "tenant-a");
        var second = await db.Database(TemplateRepository.Template, "tenant-b");
        Assert.AreEqual(0, runtime.CreatedDatabases.Count, "Selecting a handle does not create a database.");
        var firstQuery = await first.Query<Person>();
        var secondQuery = await second.Query<Person>();
        await firstQuery.CountAsync();
        await secondQuery.CountAsync();
        await firstQuery.CountAsync();
        await first.Query<Person>();

        CollectionAssert.AreEquivalent(new[] { "tenant-a", "tenant-b" }, runtime.CreatedDatabases
            .Select(database => database.GetProperty("name").GetString()).ToArray());
        var schemas = runtime.CreatedDatabases.Select(database =>
            database.GetProperty("migrationSchema").GetRawText()).ToArray();
        Assert.AreEqual(schemas[0], schemas[1]);
        Assert.AreEqual("template", TemplateRepository.Template.DatabaseName);
        Assert.AreEqual("template-v1", TemplateRepository.Template.SchemaId);
        Assert.AreEqual("migrations/template/manifest.json", TemplateRepository.Template.ManifestUrl);
        Assert.AreEqual("migrations/template/manifest.json", runtime.CreatedDatabases[0].GetProperty("migrationManifestUrl").GetString());
        CollectionAssert.AreEqual(new[] { "tenant-a", "tenant-b", "tenant-a" }, runtime.CountedDatabases.ToArray(),
            "Operations from one instance must never be redirected when another instance is selected.");
    }

    [TestMethod]
    public async Task DynamicInstance_CannotBeReboundToAnotherSchema()
    {
        var runtime = new RecordingJsRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(runtime);
        services.AddMagicBlazorDB(BlazorInteropMode.WASM, false, [typeof(Person)], [typeof(TemplateRepository)]);
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<IMagicIndexedDb>();

        var first = await db.Database(TemplateRepository.Template, "tenant-shared");
        await first.Query<Person>();
        var error = await Assert.ThrowsExactlyAsync<MagicSchemaException>(async () =>
        {
            var second = await db.Database(TemplateRepository.Other, "tenant-shared");
            await second.Query<Person>();
        });
        Assert.AreEqual("MAGDB3022", error.Code);
        Assert.AreEqual(1, runtime.CreatedDatabases.Count);
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

    private sealed class TemplateRepository : IMagicRepository
    {
        public static readonly IndexedDbSet Template = new IndexedDbSet("template").WithSchemaId("template-v1")
            .ExplicitTablesOnly().Include<Person>().WithMigrations("migrations/template/manifest.json");
        public static readonly IndexedDbSet Other = new IndexedDbSet("other-template").WithSchemaId("other-template-v1")
            .ExplicitTablesOnly().Include<Person>();
    }

    private sealed class RecordingJsRuntime : IJSRuntime, IJSObjectReference
    {
        public List<JsonElement> CreatedDatabases { get; } = [];
        public List<string> CountedDatabases { get; } = [];

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
                case "JsHandler":
                    if ((string)args![2]! == IndexedDbFunctions.CREATE_LEGACY)
                    {
                        CreatedDatabases.Add(((JsonElement)((object[])args[3]!)[0]).Clone());
                        return (T)(object)true;
                    }
                    Assert.AreEqual(IndexedDbFunctions.COUNT_TABLE, (string)args![2]!);
                    CountedDatabases.Add((string)((object[])args[3]!)[0]);
                    return (T)(object)0;
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
