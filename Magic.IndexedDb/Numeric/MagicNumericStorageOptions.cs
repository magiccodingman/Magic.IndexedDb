namespace Magic.IndexedDb;

/// <summary>Selects the persisted representation of wide .NET numeric values.</summary>
public enum MagicNumericStorageMode
{
    /// <summary>Preserves the original JavaScript-number storage format.</summary>
    Legacy = 0,
    /// <summary>Stores Int64, UInt64, and Decimal as exact, sortable versioned strings.</summary>
    Lossless = 1
}

/// <summary>Opts a logical database into a numeric storage format. Existing data requires an explicit cutover.</summary>
public sealed record MagicNumericStorageOptions(
    string DatabaseName,
    MagicNumericStorageMode Mode = MagicNumericStorageMode.Lossless);
