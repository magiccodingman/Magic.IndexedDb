using Magic.IndexedDb.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.Factories
{
    public class MagicDbFactory : IMagicDbFactory, IAsyncDisposable
    {
        private readonly Lazy<Task<IJSObjectReference>> _jsRuntime;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDictionary<string, IndexedDbManager> _databases = new Dictionary<string, IndexedDbManager>();

        public MagicDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime)
        {
            this._serviceProvider = serviceProvider;
            this._jsRuntime = new Lazy<Task<IJSObjectReference>>(() =>
            {
                return jSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/Magic.IndexedDb/magicDB.js").AsTask();
            });
        }
        public async ValueTask DisposeAsync()
        {
            if (!this._jsRuntime.IsValueCreated)
                return;
            var js = await this._jsRuntime.Value;
            try
            {
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await js.InvokeVoidAsync(IndexedDbFunctions.CLOSE_ALL, timeout.Token);
                await js.DisposeAsync();
            }
            catch
            {
                // do nothing here
            }
        }

        public async ValueTask<IndexedDbManager> OpenAsync(
            DbStore dbStore, bool force = false,
            CancellationToken cancellationToken = default)
        {
            if (force || !this._databases.ContainsKey(dbStore.Name))
            {
                var db = await IndexedDbManager.CreateAndOpenAsync(
                    dbStore, await this._jsRuntime.Value, cancellationToken);
                this._databases[dbStore.Name] = db;
            }
            return this._databases[dbStore.Name];
        }

        public IndexedDbManager Get(string dbName)
        {
            if (this._databases.TryGetValue(dbName, out var db))
                return db;
            throw new MagicException(
                $"Failed to find a opened database called {dbName}. " +
                $"If you want to open or create a new database, " +
                $"please use {nameof(OpenAsync)} or {nameof(GetRegisteredAsync)} instead.");
        }

        public async ValueTask<IndexedDbManager> GetRegisteredAsync(string dbName, CancellationToken cancellationToken = default)
        {
            var registeredStores = this._serviceProvider.GetServices<DbStore>();
            foreach (var db in registeredStores)
            {
                if (db.Name == dbName)
                    return await this.OpenAsync(db, false, cancellationToken);
            }
            throw new MagicException(
                $"Failed to find a registered database called {dbName}. " +
                $"If you want to dynamically open a new database, " +
                $"please use {nameof(OpenAsync)} instead.");
        }

        public async Task<IndexedDbManager> GetDbManagerAsync(string dbName) => await this.GetRegisteredAsync(dbName);
        public Task<IndexedDbManager> GetDbManagerAsync(DbStore dbStore) => this.GetDbManagerAsync(dbStore.Name);
    }
}