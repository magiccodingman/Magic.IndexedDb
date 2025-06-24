using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;

namespace TestBase.SnapshotBuilder;

public static class BuildTools
{
    /// <summary>
    ///  This is wrong find again!
    /// </summary>
    /// <param name="projectPath"></param>
    public static void GenerateSchemaJson(string projectPath)
    {
        Console.WriteLine("Starting schema generation...");

        // Ensure the build path exists
        string buildOutputPath = Path.Combine(projectPath, "bin", "Debug", "net8.0");

        // Load the consuming project's assemblies
        LoadProjectAssemblies(buildOutputPath);

        // Get schemas dynamically
        var allSchemas = SchemaHelper.GetAllSchemas();

        string wwwrootPath = Path.Combine(projectPath, "wwwroot");
        string magicIndexDbPath = Path.Combine(wwwrootPath, "MagicIndexedDb");

        if (!Directory.Exists(wwwrootPath))
        {
            Console.WriteLine($"ERROR: `wwwroot` is missing in {projectPath}.");
            Environment.Exit(1);
        }

        if (!Directory.Exists(magicIndexDbPath))
        {
            Directory.CreateDirectory(magicIndexDbPath);
        }

        foreach (var schema in allSchemas)
        {
            string schemaFilePath = Path.Combine(magicIndexDbPath, $"{schema.TableName}.json");

            List<StoreSchema> schemaList = new();

            if (File.Exists(schemaFilePath))
            {
                try
                {
                    string existingJson = File.ReadAllText(schemaFilePath);
                    schemaList = JsonSerializer.Deserialize<List<StoreSchema>>(existingJson) ?? new List<StoreSchema>();
                }
                catch (Exception)
                {
                    Console.WriteLine($"Warning: Failed to read {schemaFilePath}. Overwriting...");
                }
            }

            if (!schemaList.Any(s => s.TableName == schema.TableName))
            {
                schemaList.Add(schema);
            }

            string json = JsonSerializer.Serialize(schemaList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(schemaFilePath, json);

            Console.WriteLine($"Updated: {schemaFilePath}");
        }

        Console.WriteLine("Magic IndexedDB schemas generated successfully.");
    }

    private static void LoadProjectAssemblies(string buildOutputPath)
    {
        if (!Directory.Exists(buildOutputPath))
        {
            Console.WriteLine($"Warning: Build output directory does not exist ({buildOutputPath}). Skipping assembly loading.");
            return;
        }

        var dllFiles = Directory.GetFiles(buildOutputPath, "*.dll");
        foreach (var dll in dllFiles)
        {
            try
            {
                Assembly.LoadFrom(dll);
                Console.WriteLine($"Loaded assembly: {dll}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load {dll}: {ex.Message}");
            }
        }
    }
}