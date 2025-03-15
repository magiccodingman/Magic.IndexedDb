using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
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
        public static IServiceCollection AddBlazorDB(this IServiceCollection services, BlazorInteropMode interoptMode)
        {
            return services.AddBlazorDB((long)interoptMode);
        }

        public static IServiceCollection AddBlazorDB(this IServiceCollection services, long jsMessageSizeBytes)
        {
            services.AddSingleton<IMagicDbFactory>(sp => new MagicDbFactory(jsMessageSizeBytes));

#if DEBUG
            MagicValidator.ValidateTables();

            var alltables = SchemaHelper.GetAllSchemas();
            var sdf = 3;
#endif

            return services;
        }
    }
}
