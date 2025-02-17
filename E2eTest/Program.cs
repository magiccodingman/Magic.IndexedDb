using Microsoft.AspNetCore.Mvc.Testing;

namespace E2eTest;

[TestClass]
public static class Program
{
    public static WebApplicationFactory<E2eTestWebApp.Program> WebApplication { get; private set; } = null!;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        WebApplication = new();
    }

    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        await WebApplication.DisposeAsync();
    }
}
