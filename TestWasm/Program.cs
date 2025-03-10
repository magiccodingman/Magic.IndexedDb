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
    options.StoreSchemas = SchemaHelper.GetAllSchemas(DbNames.Client);    
});

await builder.Build().RunAsync();
