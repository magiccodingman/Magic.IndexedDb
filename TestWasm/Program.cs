using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TestWasm;
using TestWasm.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });


builder.Services.AddBlazorDB(BlazorInteropMode.WASM, builder.HostEnvironment.IsDevelopment());

await builder.Build().RunAsync();
