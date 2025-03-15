using MagicIndexedDbBuildTool;

class Program
{
    static void Main(string[] args)
    {
        string projectPath;

        // Check if running in Debug mode
#if DEBUG
        if (args.Length == 0)
        {
            projectPath = FindMagicIndexedDbPath();
            Console.WriteLine($"[DEBUG] Automatically detected project path: {projectPath}");
        }
        else
#endif
        {
            if (args.Length < 2 || args[0] != "--generate-schema")
            {
                Console.WriteLine("ERROR: Invalid arguments. Expected format:");
                Console.WriteLine("dotnet run -- generate-schema <projectPath>");
                Environment.Exit(1);
            }

            projectPath = args[1];
        }

        BuildTools.GenerateSchemaJson(projectPath);
    }

    private static string FindMagicIndexedDbPath()
    {
        string currentDir = Directory.GetCurrentDirectory();

        // Keep moving up until we find `MagicIndexedDbBuildTool.csproj`
        while (!File.Exists(Path.Combine(currentDir, "MagicIndexedDbBuildTool.csproj")))
        {
            string parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == null)
            {
                throw new InvalidOperationException("ERROR: Could not find `MagicIndexedDbBuildTool.csproj`. Run this from within the project directory.");
            }
            currentDir = parentDir;
        }

        // Now move one level up to find `Magic.IndexedDb`
        string repoRoot = Directory.GetParent(currentDir)?.FullName;
        if (repoRoot == null)
        {
            throw new InvalidOperationException("ERROR: Could not determine the repository root.");
        }

        string indexedDbPath = Path.Combine(repoRoot, "Magic.IndexedDb");
        string indexedDbCsproj = Path.Combine(indexedDbPath, "Magic.IndexedDb.csproj");

        if (!File.Exists(indexedDbCsproj))
        {
            throw new InvalidOperationException("ERROR: Could not find `Magic.IndexedDb.csproj` inside `Magic.IndexedDb` folder.");
        }

        return indexedDbPath;
    }
}
