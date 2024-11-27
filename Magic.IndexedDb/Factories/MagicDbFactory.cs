using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Magic.IndexedDb
{
    public class MagicDbFactory : IMagicDbFactory
    {
        readonly IJSRuntime _jsRuntime;
        readonly IServiceProvider _serviceProvider;
        readonly IDictionary<string, IndexedDbManager> _dbs = new Dictionary<string, IndexedDbManager>();
        //private IJSObjectReference _module;

        public MagicDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime)
        {
            _serviceProvider = serviceProvider;
            _jsRuntime = jSRuntime;
        }
        //public async Task<IndexedDbManager> CreateAsync(DbStore dbStore)
        //{
        //    var manager = new IndexedDbManager(dbStore, _jsRuntime);
        //    var importedManager = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Magic.IndexedDb/magicDB.js");
        //    return manager;
        //}

        public async Task<IndexedDbManager> GetDbManagerAsync(string dbName)
        {
            if (!_dbs.Any())
                await BuildFromServicesAsync();
            if (_dbs.ContainsKey(dbName))
                return _dbs[dbName];

#pragma warning disable CS8603 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            return null;
#pragma warning restore CS8603 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        }

        public Task<IndexedDbManager> GetDbManagerAsync(DbStore dbStore)
            => GetDbManagerAsync(dbStore.Name);

        async Task BuildFromServicesAsync()
        {
            var dbStores = _serviceProvider.GetServices<DbStore>();
            if (dbStores != null)
            {
                foreach (var dbStore in dbStores)
                {
                    Console.WriteLine($"{dbStore.Name}{dbStore.Version}{dbStore.StoreSchemas.Count}");
                    var db = new IndexedDbManager(dbStore, _jsRuntime);
                    await db.OpenDbAsync();
                    _dbs.Add(dbStore.Name, db);
                }
            }
        }
    }
}