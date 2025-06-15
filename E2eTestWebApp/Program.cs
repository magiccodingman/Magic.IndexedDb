using Magic.IndexedDb;
using System.Diagnostics;

namespace E2eTestWebApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        Process? parentProcess = null;
        if (args.Length >= 2 && args[0] == "--E2eTest")
        {
            parentProcess = Process.GetProcessById(int.Parse(args[1]));
            args = args[2..];
        }

        var builder = WebApplication.CreateBuilder(args);
        _ = builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        _ = builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, true);
        _ = builder.Services.AddSingleton(new DbStore()
        {
            Name = "OpenTest.RegisteredOpen2",
            Version = 3,
            StoreSchemas = []
        });
        
        var app = builder.Build();
        _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
        _ = app.UseAntiforgery();
        _ = app.MapStaticAssets();
        _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        if (parentProcess is null)
        {
            await app.RunAsync();
        }
        else
        {
            await app.StartAsync();
            await parentProcess.WaitForExitAsync();
            await app.DisposeAsync();
        }
    }
}
