using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb.Factories
{
    public class MagicDbFactory : IMagicDbFactory, IAsyncDisposable
    {
        readonly Task<IJSObjectReference> _jsRuntime;
        readonly IServiceProvider _serviceProvider;
        readonly IDictionary<string, IndexedDbManager> _databases = new Dictionary<string, IndexedDbManager>();

        public MagicDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime)
        {
            _serviceProvider = serviceProvider;
            this._jsRuntime = jSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Magic.IndexedDb/magicDB.js").AsTask();
        }
        public async ValueTask DisposeAsync()
        {
            var js = await _jsRuntime;
            try
            {
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await js.InvokeVoidAsync(IndexedDbFunctions.CLOSE_ALL, timeout.Token);
            }
            catch
            {
                // do nothing here
            }
            await js.DisposeAsync();
        }

        public async ValueTask<IndexedDbManager> OpenAsync(
            DbStore dbStore, bool force = false, 
            CancellationToken cancellationToken = default)
        {
            if (force || !_databases.ContainsKey(dbStore.Name))
            {
                var db = await IndexedDbManager.CreateAndOpenAsync(
                    dbStore, await _jsRuntime, cancellationToken);
                _databases[dbStore.Name] = db;
            }
            return _databases[dbStore.Name];
        }

        public IndexedDbManager Get(string dbName)
        {
            if (_databases.TryGetValue(dbName, out var db))
                return db;
            throw new MagicException(
                $"Failed to find a opened database called {dbName}. " +
                $"If you want to open or create a new database, " +
                $"please use {nameof(OpenAsync)} or {nameof(GetRegisteredAsync)} instead.");
        }

        public async ValueTask<IndexedDbManager> GetRegisteredAsync(string dbName, CancellationToken cancellationToken = default)
        {
            var registeredStores = _serviceProvider.GetServices<DbStore>();
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

        public async Task<IndexedDbManager> GetDbManagerAsync(string dbName) => await GetRegisteredAsync(dbName);
        public Task<IndexedDbManager> GetDbManagerAsync(DbStore dbStore) => GetDbManagerAsync(dbStore.Name);
    }
}