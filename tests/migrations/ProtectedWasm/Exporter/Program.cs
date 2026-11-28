using Magic.IndexedDb.Migrations;

return MagicSchemaExporter.Run(args, [typeof(ProtectedRow), typeof(RotationRow)], [typeof(ProtectedRepository)],
    new MagicSchemaOptions { ProtectedDatabases = ["protected-wasm", "rotation-wasm"] });
