using Magic.IndexedDb.Migrations;

return MagicSchemaExporter.Run(args, [typeof(ProtectedRow)], [typeof(ProtectedRepository)],
    new MagicSchemaOptions { ProtectedDatabases = ["protected-wasm"] });
