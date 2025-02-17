﻿using System.Diagnostics;

namespace E2eTest;

[TestClass]
public static class Program
{
    private static Process? server = null;
    public static string BaseUrl { get; private set; } = "";

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        var server = new Process
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "dotnet",
                Arguments = "run --no-build --project ../../../../E2eTestWebApp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            if (!server.Start())
                throw new Exception("Failed to start E2eTestWebApp. Process.Start returns false.");
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
                        string.Join(Environment.NewLine, lines));
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
        if (server is not null)
        {
            if (!server.HasExited)
                server.Kill(true);
            server.Dispose();
        }
    }
}
