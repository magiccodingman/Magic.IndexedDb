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
        var output = new ConcurrentQueue<string>();
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
            for (; ; )
            {
                var line = await server.StandardOutput.ReadLineAsync();
                if (line is null)
                {
                    throw new Exception(
                        $"Failed to start E2eTestWebApp. The output stream ended accidentally.{Environment.NewLine}" +
                        $"The previous message is:{Environment.NewLine}" +
                        string.Join(Environment.NewLine, output) + Environment.NewLine +
                        $"Standard error:{Environment.NewLine}" +
                        string.Join(Environment.NewLine, errors));
                }

                EnqueueRecent(output, line);
                line = line.TrimStart();
                if (line.StartsWith("Now listening on: http://"))
                {
                    BaseUrl = line.Substring("Now listening on: ".Length).TrimEnd();
                    Program.server = server;
                    _ = DrainOutputAsync(server.StandardOutput, output);
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

    private static async Task DrainOutputAsync(StreamReader reader, ConcurrentQueue<string> output)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
                EnqueueRecent(output, line);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or IOException)
        {
            // Assembly cleanup owns the process and its redirected streams.
        }
    }

    private static void EnqueueRecent(ConcurrentQueue<string> output, string line)
    {
        output.Enqueue(line);
        while (output.Count > 200)
            output.TryDequeue(out _);
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
