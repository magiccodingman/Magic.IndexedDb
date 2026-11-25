using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
#if MANAGED
using Magic.IndexedDb.Migrations;
#endif

public sealed class FixtureRepository : IMagicRepository
{
#if MANAGED
#if FORWARD
    private const string Release = "db/forward/";
#else
    private const string Release = "db/use/";
#endif
    public static readonly IndexedDbSet West = new IndexedDbSet("fixture-west")
        .WithSchemaId("west").Include<Audit>().WithMigrations(Release + "west/manifest.json");
    public static readonly IndexedDbSet East = new IndexedDbSet("fixture-east")
        .WithSchemaId("east").Include<Invoice>().WithMigrations(Release + "east/manifest.json");
#else
    public static readonly IndexedDbSet West = new("fixture-west");
    public static readonly IndexedDbSet East = new("fixture-east");
#endif
}

// Older packages discover repositories via instance properties on the table's selector.
public sealed class FixturePlaces
{
    public readonly IndexedDbSet West = FixtureRepository.West;
    public readonly IndexedDbSet East = FixtureRepository.East;
}

public sealed class Invoice : MagicTableTool<Invoice>, IMagicTable<FixturePlaces>
{
    public FixturePlaces Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => FixtureRepository.West;
    public string GetTableName() => "invoices";
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [CreateCompoundIndex(row => row.Account, row => row.Sequence)];

    [MagicName("invoice_id")] public int Id { get; set; }
    [MagicIndex] public string Account { get; set; } = "";
    public int Sequence { get; set; }
    public long AmountMinor { get; set; }
    public decimal Tax { get; set; }
    public InvoiceState State { get; set; }
#if CODECS
    [JsonConverter(typeof(ReferenceConverter))]
#if MANAGED
    [MagicStorageCodec("fixture-reference", 1)]
#endif
#endif
    public int Reference { get; set; }
    public string Description { get; set; } = "";
#if FORWARD
    [MagicIndex] public int MigrationCount { get; set; }
#endif
}

public enum InvoiceState { Issued, Settled, Voided }

public sealed class Audit : MagicTableTool<Audit>, IMagicTable<FixturePlaces>
{
    public FixturePlaces Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => FixtureRepository.East;
    public string GetTableName() => "audit";
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, true);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];

    public int Id { get; set; }
    [MagicIndex] public int InvoiceId { get; set; }
    public string Action { get; set; } = "";
}

public static class FixtureModel
{
    public static Type[] Tables => [typeof(Invoice), typeof(Audit)];
}

public sealed class ReferenceConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => int.Parse(reader.GetString()!.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteStringValue(FormattableString.Invariant($"INV{value}"));
}
