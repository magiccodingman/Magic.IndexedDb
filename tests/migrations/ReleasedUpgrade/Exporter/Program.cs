using Magic.IndexedDb;
using Magic.IndexedDb.Migrations;

return MagicSchemaExporter.Run(args, FixtureModel.Tables, [typeof(FixtureRepository)], new()
{
#if CODECS
    NumericModes = new Dictionary<string, MagicNumericStorageMode>
    {
        [FixtureRepository.East.DatabaseName] = MagicNumericStorageMode.Lossless
    }
#endif
});
