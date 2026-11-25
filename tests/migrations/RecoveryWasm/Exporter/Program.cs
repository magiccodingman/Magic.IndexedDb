using Magic.IndexedDb.Migrations;

return MagicSchemaExporter.Run(args, [typeof(RecoveryRow)], [typeof(RecoveryRepository)]);
