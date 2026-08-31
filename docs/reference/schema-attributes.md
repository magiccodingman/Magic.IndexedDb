# Schema attributes and constructors

Magic IndexedDB separates C# member names, IndexedDB schema configuration, and object materialization through a small set of attributes.

## `MagicName`

Namespace: `Magic.IndexedDb.SchemaAnnotations`

```csharp
[MagicName("customerId")]
public int Id { get; set; }
```

`MagicName` changes the persisted JavaScript property name. Use it when storage names must remain stable across C# refactors. The same mapping is honored while serializing nested complex objects.

Changing or removing an existing persisted name does not migrate old data.

## `MagicIndex`

Namespace: `Magic.IndexedDb.SchemaAnnotations`

```csharp
[MagicIndex]
public string Email { get; set; } = string.Empty;

[MagicIndex("createdAt")]
public DateTimeOffset Created { get; set; }
```

`MagicIndex` creates a non-unique IndexedDB index. Its optional name overrides the stored/indexed column name.

Indexes speed compatible equality, range, membership, starts-with, and ordering paths. They do not make every operation on the property indexable; for example, substring matching still needs cursor evaluation.

## `MagicUniqueIndex`

Namespace: `Magic.IndexedDb`

```csharp
[MagicUniqueIndex("externalId")]
public Guid ExternalId { get; set; }
```

`MagicUniqueIndex` creates a unique index. IndexedDB rejects a write that would duplicate the indexed key.

## `MagicNotMapped`

Namespace: `Magic.IndexedDb.SchemaAnnotations`

```csharp
[MagicNotMapped]
public string DisplayLabel => $"{LastName}, {FirstName}";
```

`MagicNotMapped` leaves a public property out of the stored record. It is useful for calculated, decrypted, or UI-only values.

## Attribute combinations

Development-time validation allows at most one Magic mapping attribute on a property. Do not combine `[MagicName]` with `[MagicIndex]` or `[MagicUniqueIndex]`. Both index attributes accept the persisted column name directly:

```csharp
[MagicIndex("email_address")]
public string Email { get; set; } = string.Empty;
```

The protected primary-key helpers are not attributes, so a primary-key property may use `[MagicName]` to keep its persisted key path stable.

## Compound indexes and keys

Compound configuration uses the protected `MagicTableTool<T>` helpers rather than attributes:

```csharp
public IMagicCompoundKey GetKeys() =>
    CreateCompoundKey(x => x.TenantId, x => x.CustomerId);

public List<IMagicCompoundIndex> GetCompoundIndexes() =>
[
    CreateCompoundIndex(x => x.TenantId, x => x.Email)
];
```

## Constructor selection

Most mutable models need no constructor annotation. Magic selects a constructor in this order:

1. The single constructor marked `[MagicConstructor]`.
2. The single constructor marked `[JsonConstructor]`.
3. A public parameterless constructor.
4. The only public constructor, when exactly one exists.
5. The legacy public constructor with the most parameters, with deterministic tie-breaking.

Constructor parameters bind to persisted properties case-insensitively. Optional parameter defaults are honored, constructor-bound read-only properties are supported, and remaining writable properties are populated afterward.

### Select a constructor for Magic

```csharp
using Magic.IndexedDb.SchemaAnnotations;

public sealed class Customer
{
    public int Id { get; }
    public string Name { get; }
    public string? Notes { get; set; }

    [MagicConstructor]
    public Customer(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
```

### Use the same constructor for JSON and Magic

`[JsonConstructor]` alone is enough: Magic honors it, and System.Text.Json uses it.

```csharp
using System.Text.Json.Serialization;

[JsonConstructor]
public Customer(int id, string name)
{
    Id = id;
    Name = name;
}
```

You may stack both attributes when you want the shared intent to be unmistakable:

```csharp
[MagicConstructor]
[JsonConstructor]
public Customer(int id, string name)
{
    Id = id;
    Name = name;
}
```

### Use different constructors

```csharp
[JsonConstructor]
public Customer(int id, string name, string? wireOnlyValue)
{
    Id = id;
    Name = name;
}

[MagicConstructor]
public Customer(int id, string name)
{
    Id = id;
    Name = name;
}
```

System.Text.Json ignores `[MagicConstructor]`. When Magic sees separate attributes, `[MagicConstructor]` has precedence for database materialization.

Only one constructor of each annotation type is allowed. Multiple Magic or JSON constructor annotations cause a `MagicConstructorException` identifying the affected type.

## Visual Basic syntax

Visual Basic uses angle brackets for attributes:

```vbnet
<MagicConstructor>
<JsonConstructor>
Public Sub New(id As Integer, name As String)
    Me.Id = id
    Me.Name = name
End Sub
```
