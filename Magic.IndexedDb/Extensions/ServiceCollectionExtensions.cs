﻿using Magic.IndexedDb.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Magic.IndexedDb.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBlazorDB(this IServiceCollection services, Action<DbStore> options)
        {
            services.TryAddScoped<IMagicDbFactory, MagicDbFactory>();

            var dbStore = new DbStore();
            options(dbStore);
            _ = services.AddSingleton(dbStore);

            return services;
        }
    }
}
