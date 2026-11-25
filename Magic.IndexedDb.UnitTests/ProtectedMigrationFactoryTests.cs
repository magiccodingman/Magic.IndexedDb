using System.Text.Json;
using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ProtectedMigrationFactoryTests
{
    [TestMethod]
    public async Task AuthorizationPrecedesManagedInitialization()
    {
        var browser = new Browser();
        var provider = new Provider();
        await using var factory = Create(browser, provider);

        Assert.IsTrue(await factory.EnsureUnlockedAsync(Repository.Database));

        CollectionAssert.AreEqual(new[] { IndexedDbFunctions.CREATE_LEGACY,
            "isProtectedDatabaseUnlocked", "unlockProtectedDatabase", "ensureProtectedMigration" }, browser.Calls.ToArray());
        Assert.AreEqual(1, provider.Requests);
        Assert.IsTrue(browser.MigrationRoute!.StartsWith("__magic_protected__:", StringComparison.Ordinal));
        Assert.AreEqual(browser.Registration!.Value.GetProperty("name").GetString(), browser.MigrationRoute);
    }

    [TestMethod]
    public async Task LockCanRevokeAuthorizationWhileMigrationIsWaiting()
    {
        var browser = new Browser { HoldMigration = true };
        await using var factory = Create(browser, new());
        var unlocking = factory.UnlockDetailedAsync(Repository.Database);
        await browser.MigrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // If migration execution held EncryptionUnlock's browser semaphore,
        // LockAsync would deadlock behind this deliberately unfinished operation.
        await factory.LockAsync(Repository.Database).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(MagicUnlockResult.Cancelled, await unlocking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(browser.Calls.Contains("lockProtectedDatabase"));
    }

    [TestMethod]
    public async Task RecoveryAuthorizesWithoutRetryingTheFailedOriginalMigration()
    {
        var browser = new Browser();
        var provider = new Provider();
        await using var factory = Create(browser, provider);
        var database = await factory.Database(Repository.Database);

        var outcome = await database.RecoverMigrationAsync("approved-repair", "sha256:" + new string('a', 64));

        Assert.AreEqual("recovered", outcome.GetProperty("outcome").GetString());
        Assert.AreEqual(1, provider.Requests);
        Assert.IsFalse(browser.Calls.Contains("ensureProtectedMigration"));
        Assert.IsTrue(browser.Calls.IndexOf("unlockProtectedDatabase") < browser.Calls.IndexOf("recoverMigration"));
        Assert.AreEqual(browser.Registration!.Value.GetProperty("name").GetString(),
            browser.Recovery!.Value.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task UnavailableAuthorizationDoesNotStartRecovery()
    {
        var browser = new Browser();
        await using var factory = Create(browser, new() { Available = false });
        var database = await factory.Database(Repository.Database);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            database.RecoverMigrationAsync("repair", "sha256:" + new string('a', 64)));

        StringAssert.Contains(error.Message, "MAGDB_AUTHORIZATION_REQUIRED");
        Assert.IsFalse(browser.Calls.Contains("recoverMigration"));
        Assert.IsFalse(browser.Calls.Contains("ensureProtectedMigration"));
    }

    [TestMethod]
    public async Task ExplicitPhysicalBindingIsRetainedAndDestroyCarriesItsRegisteredRoute()
    {
        var browser = new Browser();
        await using var factory = Create(browser, new(), "existing-browser-records");
        Assert.IsTrue(await factory.EnsureUnlockedAsync(Repository.Database));
        var registered = browser.Registration!.Value;
        Assert.AreEqual("existing-browser-records", registered.GetProperty("encryption").GetProperty("physicalName").GetString());
        Assert.IsTrue(registered.GetProperty("encryption").GetProperty("explicitPhysicalBinding").GetBoolean());
        Assert.IsTrue(registered.GetProperty("name").GetString()!.StartsWith("__magic_protected__:", StringComparison.Ordinal));

        await factory.DestroyProtectedDatabaseAsync(Repository.Database);

        Assert.AreEqual("existing-browser-records", browser.DestroyedPhysicalName);
        Assert.AreEqual(registered.GetProperty("name").GetString(), browser.DestroyedRoute);
    }

    private static MagicDbFactory Create(Browser browser, Provider provider, string? physicalName = null) => new(browser, 1024, false,
        tableTypes: [typeof(Row)], repositoryTypes: [typeof(Repository)],
        encryption: new Dictionary<string, EncryptionConfiguration>
        {
            [Repository.Database.DatabaseName] = new(new MagicEncryptionOptions(Repository.Database.DatabaseName, "account", "key")
                { PhysicalDatabaseName = physicalName })
        }, keyProvider: provider);

    private sealed class Provider : IMagicEncryptionKeyProvider
    {
        public bool Available { get; init; } = true;
        public int Requests { get; private set; }
        public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
        {
            Requests++;
            return ValueTask.FromResult(Available ? MagicEncryptionKey.FromBytes(new byte[32]) : null);
        }
    }

    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public JsonElement? Registration { get; private set; }
        public JsonElement? Recovery { get; private set; }
        public string? MigrationRoute { get; private set; }
        public string? DestroyedPhysicalName { get; private set; }
        public string? DestroyedRoute { get; private set; }
        public bool HoldMigration { get; init; }
        public TaskCompletionSource MigrationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> MigrationFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((T)(object)this);
            if (identifier is "configureDebug" or IndexedDbFunctions.CLOSE_ALL) return ValueTask.FromResult(default(T)!);
            if (identifier == "JsHandler")
            {
                var method = (string)args![2]!;
                var parameters = (object?[])args[3]!;
                Calls.Add(method);
                if (method == IndexedDbFunctions.CREATE_LEGACY) Registration = ((JsonElement)parameters[0]!).Clone();
                if (method == "lockProtectedDatabase" && HoldMigration)
                    MigrationFinished.TrySetException(new JSException("MAGDB_AUTHORIZATION_LOST: Authorization was revoked."));
                if (method == "destroyProtectedDatabase")
                {
                    DestroyedPhysicalName = (string)parameters[0]!;
                    DestroyedRoute = (string?)parameters[1];
                }
                return ValueTask.FromResult((T)(object)(method != "isProtectedDatabaseUnlocked"));
            }
            Calls.Add(identifier);
            if (identifier == "ensureProtectedMigration")
            {
                MigrationRoute = (string)args![0]!;
                MigrationStarted.TrySetResult();
                return HoldMigration ? WaitForMigration<T>() : ValueTask.FromResult(default(T)!);
            }
            if (identifier == "recoverMigration")
            {
                Recovery = ((JsonElement)args![0]!).Clone();
                return ValueTask.FromResult((T)(object)JsonSerializer.SerializeToElement(new { outcome = "recovered", revision = 2 }));
            }
            throw new InvalidOperationException($"Unexpected browser method: {identifier}.");
        }

        private async ValueTask<T> WaitForMigration<T>() { await MigrationFinished.Task; return default!; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class Repository : IMagicRepository
    {
        public static readonly IndexedDbSet Database = new IndexedDbSet("Managed.Protected")
            .WithMigrations("/migrations/managed-protected/manifest.json");
    }

    public sealed class Row : MagicTableTool<Row>, IMagicTable<Repository>
    {
        public int Id { get; set; }
        [MagicEncrypt] public string Secret { get; set; } = "";
        public Repository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => Repository.Database;
        public string GetTableName() => "rows";
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }
}
