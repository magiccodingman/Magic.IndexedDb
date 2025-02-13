using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb
{
    public class MagicDbFactory : IMagicDbFactory
    {
        readonly IJSRuntime _jsRuntime;
        readonly IServiceProvider _serviceProvider;
        readonly IDictionary<string, IndexedDbManager> _databases = new Dictionary<string, IndexedDbManager>();

        public MagicDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime)
        {
            _serviceProvider = serviceProvider;
            _jsRuntime = jSRuntime;
        }


        public async ValueTask<IndexedDbManager> OpenAsync(
            DbStore dbStore, bool force = false, 
            CancellationToken cancellationToken = default)
        {
            if (force || !_databases.ContainsKey(dbStore.Name))
            {
                var db = await IndexedDbManager.CreateAndOpenAsync(
                    dbStore, _jsRuntime, cancellationToken);
                _databases[dbStore.Name] = db;
            }
            return _databases[dbStore.Name];
        }

        public async Task<IndexedDbManager> GetDbManagerAsync(string dbName)
        {
            var registeredStores = _serviceProvider.GetServices<DbStore>();
            foreach (var db in registeredStores)
                _ = await this.OpenAsync(db);

            if (this._databases.ContainsKey(dbName))
                return this._databases[dbName];
            else
                throw new MagicException(
                    $"Failed to find a database called {dbName}. If you want to open or create a new database, please use OpenAsync instead.");
        }
    }
}