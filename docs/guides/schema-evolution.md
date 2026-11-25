# Schema evolution and migrations

Magic IndexedDB 3 provides a reviewed code-first migration workflow. Export the model, scaffold and review a transition, validate it in CI, and publish static migration assets. Read the [migration guide](migrations.md), [CLI guide](migration-cli.md), and [v3 adoption guide](../upgrading/v3.md) before changing existing installations.

## What creates the initial schema

Magic discovers repositories and tables, builds the IndexedDB store definitions, and creates the databases when they are first used. On later runs, changing those definitions does not transform the records that are already there.

## Changes that need explicit planning

Treat these as persisted-data changes:

- Renaming a database, table, property, index, or key path
- Adding or removing an index or compound index
- Changing a primary key or its auto-increment behavior
- Changing the serialized type or meaning of a property
- Changing an enum between numeric and named-string storage
- Changing a database between Legacy and lossless numeric storage, or changing a numeric codec version
- Adding required data that older records do not contain
- Changing constructor requirements for materialization

Test every such change against a copy of realistic data created by the previously released application.

String-backed enum names avoid ordinal changes when members are reordered, but enabling a string-enum converter does not rewrite existing numeric records. A database containing both `1` and `"Active"` requires an explicit migration strategy; a filter for one representation does not automatically query both.

## Keep persisted names stable

Use `[MagicName]` to decouple a C# property name from its stored name:

```csharp
[MagicName("customerNumber")]
public string Number { get; set; } = string.Empty;
```

You can later rename `Number` in C# while retaining `customerNumber` in IndexedDB. The attribute prevents an accidental stored-name change; it does not rename data that was already written under another name.

`GetTableName()` provides the same kind of explicit persisted name for the object store.

## Additive properties

Old rows may not contain newly added properties. Prefer safe defaults and nullable members where absence is meaningful:

```csharp
public string Notes { get; set; } = string.Empty;
public DateTimeOffset? ArchivedAt { get; set; }
```

When a cursor predicate accesses a missing field, the browser engine may be unable to evaluate that row and exclude it from the match. Backfill important new fields before depending on them in filters.

## Constructor compatibility

Changing the constructor Magic uses can break old rows if required parameters cannot be bound. Parameter names are matched to stored properties case-insensitively, optional defaults are honored, and remaining writable properties are populated after construction.

Use `[MagicConstructor]` when the automatic choice is not the constructor you want Magic to use. See [schema attributes and constructors](../reference/schema-attributes.md).

## Choose a transition deliberately

Use managed migration history for supported changes. For unsupported storage transitions, choose an explicit application-owned strategy appropriate to the data:

1. Keep the existing schema compatible and add optional/defaulted properties.
2. Read and rewrite records with explicit application code before switching behavior.
3. Introduce a new table or database name, copy/transform validated records, then switch the application.
4. For disposable cache data only, delete and recreate the database with clear user-facing consequences.

Never silently delete a user's IndexedDB database merely because the runtime model changed. Browser-local data may be the only copy available while the user is offline.

## Test checklist

- Create data with the currently released application.
- Upgrade without clearing site data.
- Exercise indexed and cursor queries that touch old and new fields.
- Verify unique constraints and primary keys.
- Verify immutable and constructor-bound models.
- Test multiple tabs, because an older open connection can block a version change.
- Test cancellation, offline startup, and recovery from an interrupted conversion.

See [browser storage and multiple tabs](../reference/browser-support-and-storage.md) for connection and deletion details, and [serialization](../reference/serialization.md) before changing numeric, date, enum, collection, or converter representations.

The former `DbStore.DbMigrations` placeholder is obsolete with a compile error. Version 3 uses `IndexedDbSet.WithMigrations()` and the separate CLI. Native IndexedDB version numbers are owned by the engine, not manually set in repositories.

For numeric format changes, see the [lossless storage cutover guide](lossless-numbers.md#existing-databases-and-cutover). Lossless storage uses a separate physical namespace and refuses to silently adopt an existing Legacy database.
