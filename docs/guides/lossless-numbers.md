# Lossless numeric storage

Enable lossless storage once for a database, then keep using ordinary `long`,
`ulong`, and `decimal` properties and typed queries. Magic encodes values before
they cross JavaScript interop and decodes them when records return to .NET.
Equality, ranges, ordering, membership, primary keys, compound indexes, and unique
indexes use the same representation. Compatible queries retain native IndexedDB
index execution.

## Enable it for a new database

Register the database's logical name after the usual Magic registration:

```csharp
using Magic.IndexedDb;

builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: false);
builder.Services.AddMagicIndexedDbNumericStorage(
    new MagicNumericStorageOptions(MyRepository.Offline.DatabaseName));
```

`MagicNumericStorageOptions` defaults to `MagicNumericStorageMode.Lossless`.
Pass several options in the same call to configure several databases. Unlisted
databases keep `MagicNumericStorageMode.Legacy`, preserving the existing wire
format. Registration order relative to encryption registration does not matter.

No new property attribute or application converter is necessary. For example,
given a registered table with indexed `long ExternalId` and `decimal Amount`
properties, the following values remain distinct and exact:

```csharp
var entries = await magic.Query<Entry>();
await entries.AddAsync(new Entry
{
    Id = "first",
    ExternalId = 9007199254740993L,
    Amount = 1.0000000000000000000000000001m
});

var exact = await entries
    .Where(entry => entry.ExternalId == 9007199254740993L)
    .ToListAsync();

var ordered = await entries.OrderBy(entry => entry.Amount).ToListAsync();
```

The normal [query API constraints](querying.md) still apply. Lossless storage does
not add `OrderBy` after `Where`, arithmetic expressions, or nested-property queries.

## Which values change

| Declared value | Lossless database representation |
|---|---|
| `long`, `ulong` | Exact, fixed-width, sortable strings |
| `decimal` | Exact, canonical, sortable strings |
| Enum with `long`/`ulong` underlying type | The corresponding wide-integer encoding |
| Nullable forms | Null or the encoded value |
| Nested objects and collection/dictionary values | The same numeric encoding recursively |
| `int`, `uint`, and smaller integers | Native numbers |
| `float`, `double` | Their existing representation |
| Ordinary strings | Unchanged; numeric-looking strings are not reinterpreted |
| Raw `JsonElement` or application-converter output | The representation supplied by the application |

Dictionary property names are already JSON strings, so wide numeric dictionary
keys remain exact without turning them into IndexedDB index values. An
`object`-typed value still follows the existing `System.Text.Json` contract when
read: it may return as a `JsonElement`. Declare the numeric type when automatic
numeric materialization is required.

Decimal equality follows .NET numeric equality: `1m`, `1.0m`, and `1.00m` produce
one index key, as do positive and negative zero. A read preserves the exact numeric
value; it does not preserve insignificant trailing zeros, the original decimal
scale bits, or a negative-zero sign. Store presentation precision separately if it
is part of the application's data.

## Native indexes and automatic IDs

The encoded strings sort in the same order as their numeric values. Magic applies
the encoding to both stored values and query operands, including compound-key
parts. It does not scan and convert the table merely because a field is a `long`
or `decimal`.

An **auto-increment primary key remains a native number** because IndexedDB owns
its generator. `long`, `ulong`, and `decimal` auto-increment keys work with exact
integer values within the native generator's range. Magic validates explicit
wide auto-increment key values before interop. Fractional values and magnitudes
above **2^53 (9,007,199,254,740,992)** are rejected. The endpoint itself is exactly
representable; after generating it, IndexedDB reports exhaustion on the next
automatic insert. Use an explicit, non-auto-increment key to access the full
`long`, `ulong`, or `decimal` range. See the [IndexedDB key-generator specification](https://www.w3.org/TR/IndexedDB-3/#key-generator-concept).

Custom converters on an auto-increment key are rejected in a lossless database.
Explicit keys can use application converters under the normal converter contract.
Protected stores retain their existing requirement for explicit keys.

## Existing databases and cutover

Numeric storage is a versioned persisted-data policy, not a display setting.
Lossless databases use a separate physical namespace so an older Magic client
cannot accidentally write ordinary numeric JSON into the new database.
Application code continues to address the same logical `IndexedDbSet`; Magic
handles routing. Protected lossless databases also use a separate account-bound
physical identity.

If the Legacy source already exists and the new lossless target does not,
initialization fails with `MAGIC_NUMERIC_MIGRATION_REQUIRED`. It does not silently
present an empty database or rewrite the existing one. This applies even if the
Legacy source is currently empty. For an ordinary lossless database,
missing/incompatible numeric metadata or a different native schema fails with
`MAGIC_NUMERIC_POLICY_MISMATCH` rather than mixing formats. Protected databases
use the existing encryption-policy checks: a changed fingerprint reports
`MAGIC_ENCRYPTION_POLICY_MISMATCH`, and a missing sentinel at unlock reports
`MAGIC_ENCRYPTION_UNINITIALIZED`. Unknown codec versions are rejected.

Choose an explicit application-owned cutover:

1. **For authoritative server data**, finish or synchronize offline changes,
   provision a new logical database name in lossless mode, and fetch the source
   data again. Validate the new cache before switching the application's default
   database.
2. **For browser-only data**, retain a Legacy source and configure a distinct new
   logical destination in lossless mode. Read typed records from the source,
   validate them, write them to the destination in bounded batches, and verify
   counts, keys, and representative queries. Make the copy resumable and switch
   the application only after it completes. Keep the source until the cutover is
   confirmed.
3. **For a disposable cache**, explicitly discard the old database through the
   application's established reset flow and refetch into lossless storage.

No automatic in-place migration is supplied. A copied value that was already
rounded in Legacy storage remains rounded; only an authoritative source can
restore the missing precision. Selecting Legacy again addresses the Legacy
physical database, not the lossless target. Coordinate the application's version
and cutover so old tabs do not continue an obsolete workflow.

The [schema evolution guide](schema-evolution.md) applies to subsequent changes
to names, keys, indexes, numeric types, and converters.

## Encryption and converters

Use the same logical database name in numeric and encryption registration.
Encoding happens before encryption, so protected numeric values retain their
precision. Public numeric keys and indexes use the same sortable encoding.
Locked `SelectPublicAsync` and `PatchPublicAsync` preserve the registered numeric
format too.

The codec policy is included in the authenticated policy of lossless vaults.
Legacy encrypted policy fingerprints remain unchanged. The key provider still
receives the application's logical database/account identity.

Property converters continue to own their explicit representation. A converter
that calls `WriteNumberValue` itself may still emit a JavaScript number; lossless
mode does not rewrite opaque JSON supplied by application code. Queries against
custom-converted properties retain their existing supported-operation limits.
Converters for an entire table are rejected in lossless mode because they can
move key and numeric members outside the declared mapping. Use property
converters or a Legacy database for that shape.

## Direct serialization helpers

Tests and tools using `MagicSerializationHelper` directly can select the format:

```csharp
using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;

var settings = new MagicJsonSerializationSettings
{
    NumericStorageMode = MagicNumericStorageMode.Lossless
};
var json = MagicSerializationHelper.SerializeObject(value, settings);
var restored = MagicSerializationHelper.DeserializeObject<MyType>(json, settings);
```

These settings affect only that helper call. Configure database services using
`AddMagicIndexedDbNumericStorage` so records, key metadata, query operands, and
responses all share one policy.

## Format and verification

Version 1 uses a five-character type/version prefix followed by fixed-width ASCII
digits: 20 digits for signed/unsigned 64-bit integers and 58 digits for decimal.
The encoded values therefore contain 25 or 63 characters, before JSON quotes or
browser-specific storage overhead. This costs more storage than a native number
while retaining native ordered indexes.

Signed integers are biased by 2^63. Decimal values are scaled exactly by 10^28
using integer arithmetic, then biased into a non-negative range. Applying the
same monotone transformation and fixed width preserves numeric order. Decoders
reject unsupported tags, non-canonical widths, invalid digits, overflow, and
decimal values the CLR cannot represent exactly. The format is an implementation
contract; applications should use the typed API instead of constructing strings.

Regression coverage compares encodings with numeric ordering, tests every decimal
scale, exercises typed browser reads and writes, checks native planner selection,
and verifies protected public operations and storage-policy mismatches. The
[testing guide](../contributing/testing.md) describes the browser and WASM checks.
