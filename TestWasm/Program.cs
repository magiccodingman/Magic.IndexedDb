using Magic.IndexedDb;
using Magic.IndexedDb.Extensions;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TestWasm;
using TestWasm.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });


/*
 * This is an example encryption key. You must make your own 128 bit or 256 bit 
 * key! Do not use this example encryption key that I've provided here as that's
 * incredibly unsafe!
 */
string EncryptionKey = "zQfTuWnZi8u7x!A%C*F-JaBdRlUkXp2l";

builder.Services.AddBlazorDB(options =>
{
    options.Name = DbNames.Client;
    options.Version = 1;
    options.EncryptionKey = EncryptionKey;
    options.StoreSchemas = SchemaHelper.GetAllSchemas(DbNames.Client);
    options.DbMigrations = new List<DbMigration>
{
        /*
         * The DbMigration is not currently working or setup!
         * This is an example and idea I'm thinking about, but 
         * this will very likely be depreciated so do not use or rely
         * on any of this syntax right now. If you want to have 
         * your own migration knowledge. Write JavaScript on the front end 
         * that will check the indedDb version and then apply migration code 
         * on the front end if needed. But this is only needed for complex 
         * migration projects.
         */
    new DbMigration
    {
        FromVersion = "1.1",
        ToVersion = "2.2",
        Instructions = new List<DbMigrationInstruction>
        {
            new DbMigrationInstruction
            {
                Action = "renameStore",
                StoreName = "oldStore",
                Details = "newStore"
            }
        }
    }
};
});

await builder.Build().RunAsync();
