# Schema evolution and migrations

Automated schema migrations are still under construction in Magic IndexedDB. Do not rely on the library to generate or run a migration simply because a C# model changed.

## What creates the initial schema

Magic discovers repository database sets and table definitions, builds the store schema, and creates or opens the declared databases when the service is first used. This is initial deployment, not a complete migration protocol.

## Changes that need explicit planning

Treat these as persisted-data changes:

- Renaming a database, table, property, index, or key path
- Adding or removing an index or compound index
- Changing a primary key or its auto-increment behavior
- Changing the serialized type or meaning of a property
- Adding required data that older records do not contain
- Changing constructor requirements for materialization

Test every such change against a copy of realistic data created by the previously released application.

## Keep persisted names stable

Use `[MagicName]` to decouple a C# property name from its stored name:

```csharp
[MagicName("customerNumber")]
public string Number { get; set; } = string.Empty;
```

You can later rename `Number` in C# while retaining `customerNumber` in IndexedDB. The attribute prevents an accidental storage-contract rename; it does not migrate data from an already different name.

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

Use `[MagicConstructor]` only when the automatic choice is not the desired persistence contract. See [schema attributes and constructors](../reference/schema-attributes.md).

## Deployment strategies today

Until a supported automatic migration workflow is released, choose an application-owned strategy appropriate to the data:

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

The migration model classes and JavaScript migration scaffolding currently present in the repository are implementation groundwork, not a published automatic migration API.
