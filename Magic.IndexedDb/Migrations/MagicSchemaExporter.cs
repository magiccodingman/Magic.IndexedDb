namespace Magic.IndexedDb.Migrations;

/// <summary>Public entry point for an isolated, executable design-time export project.</summary>
public static class MagicSchemaExporter
{
    public static string Export(IEnumerable<Type> tableTypes, IEnumerable<Type> repositoryTypes,
        MagicSchemaOptions? options = null) =>
        Export(MagicSchemaResolver.Resolve(tableTypes, repositoryTypes, options));

    public static string Export(IEnumerable<ResolvedMagicDatabase> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        return MagicSchemaJson.Serialize(new
        {
            ProtocolVersion = 1,
            Schemas = databases.Select(database => database.Schema)
                .OrderBy(schema => schema.SchemaId, StringComparer.Ordinal).ToArray()
        });
    }

    /// <summary>Runs a CLI-invoked schema export without starting the consuming application.</summary>
    /// <returns>0 on success, 2 for invalid invocation, or 1 for an invalid schema.</returns>
    public static int Run(string[] args, IEnumerable<Type> tableTypes, IEnumerable<Type> repositoryTypes,
        MagicSchemaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != 1 || args[0] != "--magic-schema-export")
        {
            Console.Error.WriteLine("This design-time project must be invoked with --magic-schema-export.");
            return 2;
        }
        try
        {
            Console.Out.WriteLine(Export(tableTypes, repositoryTypes, options));
            return 0;
        }
        catch (MagicSchemaException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.DocumentationUrl);
            return 1;
        }
    }
}
