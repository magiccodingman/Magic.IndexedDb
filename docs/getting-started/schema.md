# Define a schema

Magic IndexedDB uses a table-first model. A table class describes its object-store name, primary key, indexes, and the databases where it is valid. An `IMagicRepository` supplies the named databases.

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

`Query<T>()` uses `GetDefaultDatabase()`. The selector overload should point to one of the table's declared `Databases` fields.

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

## Nested data and collections

Stored models may contain nested objects, arrays, lists, sets, dictionaries, and nested collections. The current release preserves configured JSON converters, escaped strings, Unicode text, `MagicName` mappings inside nested objects, and supported concrete collection shapes when values are materialized.

Indexes and primary keys still need to describe values IndexedDB can use as keys. Do not assume an arbitrary nested object is indexable merely because it can be serialized.

## Constructors

Most mutable table models should keep a public parameterless constructor. Immutable or hybrid persisted types can select a constructor with `[MagicConstructor]`; existing `[JsonConstructor]` annotations remain supported. See [schema attributes and constructors](../reference/schema-attributes.md) for the complete precedence rules.

## Schema changes

Defining the C# schema does not provide an automatic migration protocol. Before changing table names, key paths, indexes, or persisted property names, read [schema evolution](../guides/schema-evolution.md).
