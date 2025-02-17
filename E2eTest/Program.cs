using E2eTestWebApp.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace E2eTest;

[TestClass]
public static class Program
{
    public static WebApplicationFactory<E2eTestWebApp.Program> WebApplication { get; }

    static Program()
    {
        WebApplication = new();
    }

    [AssemblyCleanup]
    public async static Task Cleanup()
    {
        await WebApplication.DisposeAsync();
    }
}
