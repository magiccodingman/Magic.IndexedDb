using Magic.IndexedDb.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Magic.IndexedDb.BuildTools
{
    public class MagicIndexDbBuildTool
    {
        public static void GenerateSchemaJson(string projectPath)
        {
            // Get all schemas dynamically
            var allSchemas = SchemaHelper.GetAllSchemas();

            //// Construct paths
            //string wwwrootPath = Path.Combine(projectPath, "wwwroot");
            //string magicIndexDbPath = Path.Combine(wwwrootPath, "MagicIndexedDb");

            //// 🔹 Enforce `/wwwroot/` existence
            //if (!Directory.Exists(wwwrootPath))
            //{
            //    Console.WriteLine($"ERROR: The `wwwroot` directory is missing in {projectPath}.");
            //    Console.WriteLine("Please ensure this folder exists before building.");
            //    Environment.Exit(1); // FAIL the build
            //}

            //// Ensure `/wwwroot/MagicIndexedDb/` exists
            //if (!Directory.Exists(magicIndexDbPath))
            //{
            //    Directory.CreateDirectory(magicIndexDbPath);
            //}

            //// Iterate over each schema and save/update individual JSON files
            //foreach (var schema in allSchemas)
            //{
            //    string schemaFilePath = Path.Combine(magicIndexDbPath, $"{schema.TableName}.json");

            //    List<StoreSchema> schemaList = new();

            //    // If file already exists, read it and merge existing schemas
            //    if (File.Exists(schemaFilePath))
            //    {
            //        try
            //        {
            //            string existingJson = File.ReadAllText(schemaFilePath);
            //            schemaList = JsonSerializer.Deserialize<List<StoreSchema>>(existingJson) ?? new List<StoreSchema>();
            //        }
            //        catch (Exception ex)
            //        {
            //            Console.WriteLine($"Warning: Failed to read {schemaFilePath}. Overwriting...");
            //        }
            //    }

            //    // Check if this schema already exists in the list
            //    if (!schemaList.Any(s => s.TableName == schema.TableName))
            //    {
            //        schemaList.Add(schema);
            //    }

            //    // Write the updated schema list back to JSON
            //    string json = JsonSerializer.Serialize(schemaList, new JsonSerializerOptions { WriteIndented = true });
            //    File.WriteAllText(schemaFilePath, json);

            //    Console.WriteLine($"Updated: {schemaFilePath}");
            //}

            //Console.WriteLine("Magic IndexedDB schemas generated successfully!");
        }
    }
}
