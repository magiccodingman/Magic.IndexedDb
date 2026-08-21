using System.Collections.Concurrent;
using System.Diagnostics;

namespace E2eTest;

[TestClass]
public static class Program
{
    private static Process? server = null;
    public static string BaseUrl { get; private set; } = "";

    // https://stackoverflow.com/questions/4029886/
    private static int count = 0;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        count++;
        if (count is not 1)
            return;

        using var currentProcess = Process.GetCurrentProcess();
        var appDll = Path.Combine(AppContext.BaseDirectory, "E2eTestWebApp.dll");
        var appContentRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "E2eTestWebApp"));
        var webAppArguments = $"--E2eTest {currentProcess.Id}";
        var errors = new ConcurrentQueue<string>();
        var server = new Process
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "dotnet",
                Arguments = $"\"{appDll}\" {webAppArguments} --urls http://127.0.0.1:0 --contentRoot \"{appContentRoot}\"",
                WorkingDirectory = appContentRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        server.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
                errors.Enqueue(eventArgs.Data);
        };
        server.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        try
        {
            if (!server.Start())
                throw new Exception("Failed to start E2eTestWebApp. Process.Start returns false.");
            server.BeginErrorReadLine();
        }
        catch
        {
            server.Dispose();
            throw;
        }

        try
        {
            var lines = new List<string>();
            for (; ; )
            {
                var line = await server.StandardOutput.ReadLineAsync();
                if (line is null)
                {
                    throw new Exception(
                        $"Failed to start E2eTestWebApp. The output stream ended accidentally.{Environment.NewLine}" +
                        $"The previous message is:{Environment.NewLine}" +
                        string.Join(Environment.NewLine, lines) + Environment.NewLine +
                        $"Standard error:{Environment.NewLine}" +
                        string.Join(Environment.NewLine, errors));
                }

                lines.Add(line);
                line = line.TrimStart();
                if (line.StartsWith("Now listening on: http://"))
                {
                    BaseUrl = line.Substring("Now listening on: ".Length).TrimEnd();
                    Program.server = server;
                    return;
                }
            }
        }
        catch
        {
            if (!server.HasExited)
                server.Kill(true);
            server.Dispose();
            throw;
        }
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        count--;
        if (count is not 0)
            return;

        if (server is not null)
        {
            if (!server.HasExited)
                server.Kill(true);
            server.Dispose();
        }
    }
}
