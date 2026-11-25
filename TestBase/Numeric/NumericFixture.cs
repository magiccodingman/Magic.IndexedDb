using System.Linq.Expressions;
using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace TestBase.Numeric;

/// <summary>Shared real-interop scenarios for server-rendered, WASM, and AOT test hosts.</summary>
public static class NumericFixture
{
    private const long AboveSafeInteger = 9_007_199_254_740_993L;
    private const decimal SmallestDecimal = 0.0000000000000000000000000001m;

    public static async Task<string> RoundTripAndStream(IMagicIndexedDb magic)
    {
        var expected = Rows();
        var query = await SeedValues(magic, expected);
        var materialized = await query.ToListAsync();
        CheckRows(expected, materialized, "materialized");

        var streamed = new List<NumericValueRecord>();
        await foreach (var row in query.AsAsyncEnumerable()) streamed.Add(row);
        CheckRows(expected, streamed, "streamed");

        // The same model can retain the original wire format in another database.
        var legacy = await magic.Query<NumericValueRecord>(_ => NumericRepository.Legacy);
        await legacy.ClearTable();
        await legacy.AddAsync(new() { Id = 100, Signed = 42, Unsigned = 43, Amount = 1.5m });
        var legacyRow = (await legacy.ToListAsync()).Single();
        Require(legacyRow.Signed == 42 && legacyRow.Unsigned == 43 && legacyRow.Amount == 1.5m,
            "Legacy database isolation changed ordinary numeric values.");
        CheckRows(expected, await query.ToListAsync(), "lossless after legacy write");
        return "OK";
    }

    public static async Task<string> IndexedPredicatesAndOrdering(IMagicIndexedDb magic)
    {
        var rows = Rows();
        var query = await SeedValues(magic, rows);
        long[] signedMembership = [long.MinValue, AboveSafeInteger, long.MaxValue];
        decimal[] decimalMembership = [decimal.MinValue, SmallestDecimal, 1.0000000000000000000000000001m];
        long?[] nullableMembership = [null, AboveSafeInteger];
        Expression<Func<NumericValueRecord, bool>>[] predicates =
        [
            row => row.Signed == AboveSafeInteger,
            row => row.Signed > 9_007_199_254_740_992L,
            row => row.Signed >= -AboveSafeInteger && row.Signed < AboveSafeInteger,
            row => row.Unsigned == ulong.MaxValue,
            row => row.Unsigned > 9_007_199_254_740_992UL,
            row => row.Unsigned <= 9_007_199_254_740_993UL,
            row => row.Amount == 1.0000000000000000000000000001m,
            row => row.Amount > 0m && row.Amount < 1m,
            row => row.Amount <= -SmallestDecimal || row.Amount >= decimal.MaxValue,
            row => signedMembership.Contains(row.Signed),
            row => decimalMembership.Contains(row.Amount),
            row => nullableMembership.Contains(row.OptionalLong),
            row => row.SignedStatus == (NumericLongStatus)AboveSafeInteger,
            row => row.UnsignedStatus == (NumericUnsignedStatus)ulong.MaxValue
        ];
        foreach (var predicate in predicates)
        {
            var expected = rows.Where(predicate.Compile()).Select(row => row.Id).Order().ToArray();
            var indexed = (await query.Where(predicate).ToListAsync()).Select(row => row.Id).Order().ToArray();
            var cursor = (await query.Cursor(predicate).ToListAsync()).Select(row => row.Id).Order().ToArray();
            Require(expected.SequenceEqual(indexed) && expected.SequenceEqual(cursor),
                $"Predicate {predicate}: expected [{string.Join(',', expected)}], indexed [{string.Join(',', indexed)}], cursor [{string.Join(',', cursor)}].");
        }

        await CheckOrder(query, rows, row => row.Signed, row => row.Signed);
        await CheckOrder(query, rows, row => row.Unsigned, row => row.Unsigned);
        await CheckOrder(query, rows, row => row.Amount, row => row.Amount);

        // Leave a native compound-index trace for the host's planner assertion.
        var compound = await query.Where(row => row.Signed == AboveSafeInteger &&
                                                row.Amount == 9_007_199_254_740_993.000000000001m).ToListAsync();
        Require(compound.Count == 1 && compound[0].Id == 8, "The numeric compound index lost an exact tuple.");
        return "OK";
    }

    public static async Task<string> ExplicitKeysAndBulkCrud(IMagicIndexedDb magic)
    {
        await CheckKeyCrud(await magic.Query<NumericLongKeyRecord>(),
            new long[] { long.MinValue, 0, 9_007_199_254_740_992L, AboveSafeInteger, long.MaxValue }
                .Select(key => new NumericLongKeyRecord { Key = key, Label = "original" }).ToArray(),
            row => row.Key, row => row.Label, (row, label) => row.Label = label);
        await CheckKeyCrud(await magic.Query<NumericUnsignedKeyRecord>(),
            new ulong[] { 0, 9_007_199_254_740_992UL, 9_007_199_254_740_993UL, ulong.MaxValue }
                .Select(key => new NumericUnsignedKeyRecord { Key = key, Label = "original" }).ToArray(),
            row => row.Key, row => row.Label, (row, label) => row.Label = label);
        await CheckKeyCrud(await magic.Query<NumericDecimalKeyRecord>(),
            new decimal[] { decimal.MinValue, 0m, 1m, 1.0000000000000000000000000001m, decimal.MaxValue }
                .Select(key => new NumericDecimalKeyRecord { Key = key, Label = "original" }).ToArray(),
            row => row.Key, row => row.Label, (row, label) => row.Label = label);
        return "OK";
    }

    public static async Task<string> CanonicalUniqueIndexes(IMagicIndexedDb magic)
    {
        var rows = Rows();
        var query = await SeedValues(magic, rows);
        await RequireFailure(() => query.AddAsync(new() { Id = 100, Amount = 1.00m }),
            "A numerically equal decimal with a different scale bypassed the unique index.");
        Require(await query.CountAsync() == rows.Length, "A rejected duplicate changed the table.");

        const decimal distinct = 1.0000000000000000000000000002m;
        await query.AddAsync(new() { Id = 101, Signed = 44, Unsigned = 44, Amount = distinct });
        var exact = await query.Where(row => row.Amount == distinct).ToListAsync();
        Require(exact.Count == 1 && exact[0].Amount == distinct, "Adjacent decimals collided in the unique index.");
        await RequireFailure(() => query.UpdateAsync(new() { Id = 101, Amount = 1.000m }),
            "Updating to an equal decimal bypassed the unique index.");
        var unchanged = await query.Where(row => row.Id == 101).ToListAsync();
        Require(unchanged.Single().Amount == distinct, "A failed unique update replaced the original value.");

        // Leave an indexed ordering trace; this must not require decrypting or
        // converting every stored numeric value to sort it.
        var ordered = await query.OrderBy(row => row.Amount).ToListAsync();
        var expected = rows.Select(row => row.Amount).Append(distinct).Order().ToArray();
        Require(expected.SequenceEqual(ordered.Select(row => row.Amount)), "Native decimal index order was not numeric.");
        return "OK";
    }

    public static async Task<string> NativeAutoIncrementKeys(IMagicIndexedDb magic)
    {
        var query = await magic.Query<NumericAutoKeyRecord>();
        await query.ClearTable();
        await query.AddAsync(new() { Label = "first" });
        await query.AddAsync(new() { Label = "second" });
        Require((await query.OrderBy(row => row.Key).ToListAsync()).Select(row => row.Key).SequenceEqual([1L, 2L]),
            "Fresh native auto-increment keys did not round-trip as 1 and 2.");

        await query.AddAsync(new() { Key = 9_007_199_254_740_990L, Label = "seed" });
        await query.AddAsync(new() { Label = "next" });
        await query.AddAsync(new() { Label = "last-exact" });
        var upper = await query.Where(row => row.Key >= 9_007_199_254_740_990L).ToListAsync();
        Require(upper.Select(row => row.Key).Order().SequenceEqual(
                [9_007_199_254_740_990L, 9_007_199_254_740_991L, 9_007_199_254_740_992L]),
            "The final exact native keys were encoded or rounded incorrectly.");

        int before = await query.CountAsync();
        await RequireFailure(() => query.AddAsync(new() { Label = "exhausted" }),
            "The exhausted native key generator accepted another row.");
        await RequireFailure(() => query.AddAsync(new() { Key = AboveSafeInteger, Label = "unsafe-explicit" }),
            "An explicit native key above 2^53 was accepted.");
        Require(await query.CountAsync() == before, "A rejected native key persisted a row.");
        return "OK";
    }

    public static async Task<string> ProtectedPublicAccess(IMagicIndexedDb magic, IMagicIndexedDbEncryption encryption)
    {
        var database = NumericProtectedRepository.Database;
        var query = await magic.Query<NumericProtectedRecord>();
        Require(await encryption.InitializeAsync(database) && await encryption.EnsureUnlockedAsync(database),
            "The numeric test vault did not unlock.");
        await query.ClearTable();
        var original = new NumericProtectedRecord
        {
            Id = AboveSafeInteger, PublicAmount = 100.0000000000000000000000001m,
            SecretSigned = long.MinValue, SecretUnsigned = ulong.MaxValue,
            SecretAmount = decimal.MaxValue, Nested = Payload(long.MinValue, ulong.MaxValue, -SmallestDecimal)
        };
        await query.AddAsync(original);
        await encryption.LockAsync(database);
        try
        {
            await query.ToListAsync();
            throw new InvalidOperationException("A locked numeric vault returned a complete protected record.");
        }
        catch (MagicEncryptionLockedException) { }

        var exposed = await query.Where(row => row.Id == AboveSafeInteger).SelectPublicAsync(row => row.PublicAmount);
        Require(exposed.Count == 1 && exposed[0] == original.PublicAmount,
            "A locked public projection lost decimal precision or the long key predicate.");
        Require(await query.PatchPublicAsync(new() { Id = AboveSafeInteger, PublicAmount = -SmallestDecimal },
            row => row.PublicAmount) == 1, "A locked public patch did not find the exact long primary key.");
        var patched = await query.SelectPublicAsync(row => row.PublicAmount);
        Require(patched.Single() == -SmallestDecimal, "A locked public patch rounded a decimal.");

        Require(await encryption.EnsureUnlockedAsync(database), "The numeric test vault did not unlock again.");
        var restored = (await query.Where(row => row.Id == AboveSafeInteger).ToListAsync()).Single();
        Require(restored.PublicAmount == -SmallestDecimal && restored.SecretSigned == original.SecretSigned &&
                restored.SecretUnsigned == original.SecretUnsigned && restored.SecretAmount == original.SecretAmount,
            "Protected numeric values changed after a locked public patch.");
        CheckPayload(original.Nested, restored.Nested, "protected nested values");
        await encryption.LockAsync(database);
        return "OK";
    }

    private static NumericValueRecord[] Rows()
    {
        long[] signed = [long.MinValue, -AboveSafeInteger, -1, 0, 1, 9_007_199_254_740_991L, 9_007_199_254_740_992L, AboveSafeInteger, long.MaxValue];
        ulong[] unsigned = [ulong.MaxValue, 0, 9_007_199_254_740_993UL, 1, 9_007_199_254_740_991UL, 9_007_199_254_740_992UL, 2, 9_223_372_036_854_775_808UL, ulong.MaxValue - 1];
        decimal[] amounts = [decimal.MinValue, -AboveSafeInteger, -SmallestDecimal, 0m, 1m, SmallestDecimal, 1.0000000000000000000000000001m, 9_007_199_254_740_993.000000000001m, decimal.MaxValue];
        return signed.Select((value, index) => new NumericValueRecord
        {
            Id = index + 1, Signed = value, Unsigned = unsigned[index], Amount = amounts[index],
            OptionalLong = index % 2 == 0 ? null : value,
            SignedStatus = (NumericLongStatus)value, UnsignedStatus = (NumericUnsignedStatus)unsigned[index],
            Nested = Payload(value, unsigned[index], amounts[index])
        }).ToArray();
    }

    private static NumericPayload Payload(long signed, ulong unsigned, decimal amount) => new()
    {
        Signed = signed, Unsigned = unsigned, Amount = amount, OptionalAmount = amount == 0m ? null : amount,
        SignedValues = [long.MinValue, signed, long.MaxValue], DecimalValues = [SmallestDecimal, amount, decimal.MaxValue],
        ByName = new() { ["exact"] = unsigned, ["maximum"] = ulong.MaxValue }
    };

    private static async Task<IMagicQuery<NumericValueRecord>> SeedValues(IMagicIndexedDb magic, NumericValueRecord[] rows)
    {
        var query = await magic.Query<NumericValueRecord>();
        await query.ClearTable();
        await query.AddRangeAsync(rows);
        return query;
    }

    private static void CheckRows(NumericValueRecord[] expected, IEnumerable<NumericValueRecord> actual, string path)
    {
        var byId = actual.ToDictionary(row => row.Id);
        Require(byId.Count == expected.Length, $"Wrong row count from {path}.");
        foreach (var row in expected)
        {
            Require(byId.TryGetValue(row.Id, out var stored), $"Missing row {row.Id} from {path}.");
            Require(stored!.Signed == row.Signed && stored.Unsigned == row.Unsigned && stored.Amount == row.Amount &&
                    stored.OptionalLong == row.OptionalLong && stored.SignedStatus == row.SignedStatus &&
                    stored.UnsignedStatus == row.UnsignedStatus, $"Numeric round-trip failed for row {row.Id} from {path}.");
            CheckPayload(row.Nested, stored.Nested, $"{path}, row {row.Id}");
        }
    }

    private static void CheckPayload(NumericPayload expected, NumericPayload actual, string path) =>
        Require(expected.Signed == actual.Signed && expected.Unsigned == actual.Unsigned && expected.Amount == actual.Amount &&
                expected.OptionalAmount == actual.OptionalAmount && expected.SignedValues.SequenceEqual(actual.SignedValues) &&
                expected.DecimalValues.SequenceEqual(actual.DecimalValues) &&
                expected.ByName.Count == actual.ByName.Count && expected.ByName.All(pair => actual.ByName.GetValueOrDefault(pair.Key) == pair.Value),
            $"Nested/collection numeric round-trip failed for {path}.");

    private static async Task CheckOrder<TKey>(IMagicQuery<NumericValueRecord> query, NumericValueRecord[] rows,
        Expression<Func<NumericValueRecord, object>> selector, Func<NumericValueRecord, TKey> key)
    {
        var ascending = rows.OrderBy(key).Select(row => row.Id).ToArray();
        var descending = rows.OrderByDescending(key).Select(row => row.Id).ToArray();
        Require(ascending.SequenceEqual((await query.OrderBy(selector).ToListAsync()).Select(row => row.Id)) &&
                ascending.SequenceEqual((await query.Cursor(row => true).OrderBy(selector).ToListAsync()).Select(row => row.Id)) &&
                descending.SequenceEqual((await query.OrderByDescending(selector).ToListAsync()).Select(row => row.Id)) &&
                descending.SequenceEqual((await query.Cursor(row => true).OrderByDescending(selector).ToListAsync()).Select(row => row.Id)),
            $"Indexed/cursor ordering differs from C# for {selector}.");
    }

    private static async Task CheckKeyCrud<T, TKey>(IMagicQuery<T> query, T[] rows, Expression<Func<T, TKey>> selector,
        Func<T, string> label, Action<T, string> setLabel) where T : class where TKey : notnull
    {
        var key = selector.Compile();
        await query.ClearTable();
        await query.AddRangeAsync(rows);
        foreach (var row in rows)
        {
            var predicate = Expression.Lambda<Func<T, bool>>(
                Expression.Equal(selector.Body, Expression.Constant(key(row), typeof(TKey))), selector.Parameters);
            var match = (await query.Where(predicate).ToListAsync()).Single();
            Require(EqualityComparer<TKey>.Default.Equals(key(row), key(match)), $"Explicit {typeof(TKey).Name} key was rounded.");
        }
        setLabel(rows[0], "updated-first");
        setLabel(rows[2], "updated-third");
        Require(await query.UpdateRangeAsync([rows[0], rows[2]]) == 2, "Bulk numeric-key update returned the wrong count.");
        var afterUpdate = (await query.ToListAsync()).ToDictionary(key);
        Require(label(afterUpdate[key(rows[0])]) == "updated-first" && label(afterUpdate[key(rows[2])]) == "updated-third",
            "Bulk update did not address exact numeric keys.");
        Require(await query.DeleteRangeAsync([rows[1], rows[^1]]) == 2, "Bulk numeric-key delete returned the wrong count.");
        await query.DeleteAsync(rows[0]);
        var expected = rows.Skip(2).SkipLast(1).Select(key).OrderBy(value => value).ToArray();
        var actual = (await query.ToListAsync()).Select(key).OrderBy(value => value).ToArray();
        Require(expected.SequenceEqual(actual), "Single/bulk delete removed the wrong numeric keys.");
    }

    private static async Task RequireFailure(Func<Task> action, string message)
    {
        try { await action(); }
        catch { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed class NumericRepository : IMagicRepository
{
    public static readonly IndexedDbSet Lossless = new("Numeric.Lossless");
    public static readonly IndexedDbSet Legacy = new("Numeric.Legacy");
}

public sealed class NumericProtectedRepository : IMagicRepository
{
    public static readonly IndexedDbSet Database = new("Numeric.Protected");
}

public enum NumericLongStatus : long { None }
public enum NumericUnsignedStatus : ulong { None }

public sealed class NumericPayload
{
    public long Signed { get; set; }
    public ulong Unsigned { get; set; }
    public decimal Amount { get; set; }
    public decimal? OptionalAmount { get; set; }
    public long[] SignedValues { get; set; } = [];
    public List<decimal> DecimalValues { get; set; } = [];
    public Dictionary<string, ulong> ByName { get; set; } = [];
}

public sealed class NumericValueRecord : MagicTableTool<NumericValueRecord>, IMagicTable<NumericRepository>
{
    public int Id { get; set; }
    [MagicIndex] public long Signed { get; set; }
    [MagicIndex] public ulong Unsigned { get; set; }
    [MagicUniqueIndex] public decimal Amount { get; set; }
    [MagicIndex] public long? OptionalLong { get; set; }
    [MagicIndex] public NumericLongStatus SignedStatus { get; set; }
    [MagicIndex] public NumericUnsignedStatus UnsignedStatus { get; set; }
    public NumericPayload Nested { get; set; } = new();
    public NumericRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericRepository.Lossless;
    public string GetTableName() => nameof(NumericValueRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [CreateCompoundIndex(row => row.Signed, row => row.Amount)];
}

public sealed class NumericLongKeyRecord : MagicTableTool<NumericLongKeyRecord>, IMagicTable<NumericRepository>
{
    public long Key { get; set; }
    public string Label { get; set; } = "";
    public NumericRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericRepository.Lossless;
    public string GetTableName() => nameof(NumericLongKeyRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Key, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

public sealed class NumericUnsignedKeyRecord : MagicTableTool<NumericUnsignedKeyRecord>, IMagicTable<NumericRepository>
{
    public ulong Key { get; set; }
    public string Label { get; set; } = "";
    public NumericRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericRepository.Lossless;
    public string GetTableName() => nameof(NumericUnsignedKeyRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Key, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

public sealed class NumericDecimalKeyRecord : MagicTableTool<NumericDecimalKeyRecord>, IMagicTable<NumericRepository>
{
    public decimal Key { get; set; }
    public string Label { get; set; } = "";
    public NumericRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericRepository.Lossless;
    public string GetTableName() => nameof(NumericDecimalKeyRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Key, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

public sealed class NumericAutoKeyRecord : MagicTableTool<NumericAutoKeyRecord>, IMagicTable<NumericRepository>
{
    public long Key { get; set; }
    public string Label { get; set; } = "";
    public NumericRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericRepository.Lossless;
    public string GetTableName() => nameof(NumericAutoKeyRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Key, true);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

[MagicEncryptRecord(nameof(Id), nameof(PublicAmount))]
public sealed class NumericProtectedRecord : MagicTableTool<NumericProtectedRecord>, IMagicTable<NumericProtectedRepository>
{
    public long Id { get; set; }
    [MagicIndex] public decimal PublicAmount { get; set; }
    public long SecretSigned { get; set; }
    public ulong SecretUnsigned { get; set; }
    public decimal SecretAmount { get; set; }
    public NumericPayload Nested { get; set; } = new();
    public NumericProtectedRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => NumericProtectedRepository.Database;
    public string GetTableName() => nameof(NumericProtectedRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}
