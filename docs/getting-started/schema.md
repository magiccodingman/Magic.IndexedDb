# Define a schema

Magic IndexedDB uses a table-first model. A table class describes its object-store name, primary key, indexes, and strongly typed database selectors. An `IMagicRepository` supplies the named databases.

## Create a repository

```csharp
using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

public sealed class IndexedDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Client = new("Client");
    public static readonly IndexedDbSet Employee = new("Employee");
}
```

Magic IndexedDB discovers `IMagicRepository` implementations and their static `IndexedDbSet` fields through assembly scanning.

## Create a table

```csharp
using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;

public sealed class Person : MagicTableTool<Person>, IMagicTable<Person.DbSets>
{
    public List<IMagicCompoundIndex> GetCompoundIndexes() =>
    [
        CreateCompoundIndex(x => x.LastName, x => x.FirstName)
    ];

    public IMagicCompoundKey GetKeys() =>
        CreatePrimaryKey(x => x.Id, autoIncrement: true);

    public string GetTableName() => "people";

    public IndexedDbSet GetDefaultDatabase() => IndexedDbContext.Client;

    public DbSets Databases { get; } = new();

    public sealed class DbSets
    {
        public readonly IndexedDbSet Client = IndexedDbContext.Client;
        public readonly IndexedDbSet Employee = IndexedDbContext.Employee;
    }

    [MagicName("id")]
    public int Id { get; set; }

    [MagicIndex("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [MagicIndex("lastName")]
    public string LastName { get; set; } = string.Empty;

    [MagicIndex("age")]
    public int Age { get; set; }

    [MagicIndex("city")]
    public string City { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    [MagicIndex("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MagicUniqueIndex("externalId")]
    public Guid ExternalId { get; set; } = Guid.NewGuid();

    public Address Address { get; set; } = new();

    [MagicNotMapped]
    public string DisplayLabel => $"{LastName}, {FirstName}";
}

public sealed class Address
{
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
```

The generic parameter of `IMagicTable<TDbSets>` makes database selection strongly typed:

```csharp
IMagicQuery<Person> defaultQuery = await MagicDb.Query<Person>();

IMagicQuery<Person> employeeQuery =
    await MagicDb.Query<Person>(person => person.Databases.Employee);
```

`Query<T>()` uses `GetDefaultDatabase()`. The selector overload should point to one of the table's declared `Databases` fields. The selector is a strongly typed database choice, not an authorization boundary; Magic does not use it to enforce which callers may access a database.

Magic currently creates every discovered table in every discovered `IndexedDbSet`. The table's `Databases` property gives you strongly typed choices when querying, but it does not remove that table from the other Magic databases. See [browser storage and multiple tabs](../reference/browser-support-and-storage.md#which-tables-are-created).

## Primary keys

A single primary key can optionally auto-increment:

```csharp
public IMagicCompoundKey GetKeys() =>
    CreatePrimaryKey(x => x.Id, autoIncrement: true);
```

A compound primary key combines multiple fields and cannot auto-increment:

```csharp
public IMagicCompoundKey GetKeys() =>
    CreateCompoundKey(x => x.TenantId, x => x.PersonId);
```

A single primary-key property cannot be nullable. If it auto-increments, it must also be numeric. Development-time schema validation rejects nullable single keys, non-numeric auto-increment keys, duplicate compound-key column names, and duplicate compound-index definitions.

## Compound indexes

Return every compound index from `GetCompoundIndexes()`:

```csharp
public List<IMagicCompoundIndex> GetCompoundIndexes() =>
[
    CreateCompoundIndex(x => x.TenantId, x => x.Email),
    CreateCompoundIndex(x => x.LastName, x => x.FirstName)
];
```

Compound indexes let the optimizer satisfy compatible multi-field predicates with one IndexedDB index path.

The order of fields in a compound key or compound index is part of the persisted schema. A component of a compound primary key is not automatically a standalone index. Add `[MagicIndex]` to a component when the application must order or query that property through its own index path.

## Nested data and collections

Stored models may contain nested objects, arrays, lists, sets, dictionaries, and nested collections. Dictionaries stay as JSON objects even though they implement `IEnumerable`. Escaped strings, Unicode text, `MagicName` mappings inside nested objects, and supported concrete collection types are preserved when records are read.

Indexes and primary keys still need to describe values IndexedDB can use as keys. Do not assume an arbitrary nested object is indexable merely because it can be serialized.

For numeric precision, collection reconstruction, custom converters, and the difference between storing and querying a type, see [serialization](../reference/serialization.md).

## Constructors

Most mutable table models should keep a public parameterless constructor. Immutable or hybrid types can select a constructor with `[MagicConstructor]`, and existing `[JsonConstructor]` annotations also work. See [schema attributes and constructors](../reference/schema-attributes.md).

## Attribute validation

A property may have at most one Magic mapping attribute among `[MagicName]`, `[MagicIndex]`, `[MagicUniqueIndex]`, and `[MagicNotMapped]`. When an indexed property also needs a persisted name, use the index attribute's optional name instead of stacking attributes:

```csharp
[MagicIndex("email_address")]
public string Email { get; set; } = string.Empty;

[MagicUniqueIndex("external_id")]
public Guid ExternalId { get; set; }
```

Keep development validation enabled so conflicting attributes and invalid key definitions fail during startup rather than surfacing later as browser schema errors.

## Schema changes

Changing the C# schema does not update existing records automatically. Before changing table names, key paths, indexes, or stored property names, read [schema evolution](../guides/schema-evolution.md).
