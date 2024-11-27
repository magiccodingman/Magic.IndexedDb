using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBlazorDB(this IServiceCollection services, Action<DbStore> options)
        {
            var dbStore = new DbStore();
            options(dbStore);

            services.AddTransient<DbStore>((_) => dbStore);
            services.TryAddSingleton<IMagicDbFactory, MagicDbFactory>();

            return services;
        }

        // The registered service is not used in IndexDbManager at all
        /*
        public static IServiceCollection AddEncryptionFactory(this IServiceCollection services)
        {
            services.TryAddSingleton<IEncryptionFactory, EncryptionFactory>();

            return services;
        }
        */
    }
}
