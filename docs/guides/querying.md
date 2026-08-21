# Querying

An `IMagicQuery<T>` represents one table in one database. Query composition is deferred: Magic IndexedDB does not execute the read until you call an execution method such as `ToListAsync()`, `AsAsyncEnumerable()`, `FirstOrDefaultAsync()`, or `LastOrDefaultAsync()`.

```csharp
IMagicQuery<Person> people = await MagicDb.Query<Person>();

List<Person> results = await people
    .Where(person => person.Age >= 18)
    .Where(person => person.IsActive)
    .ToListAsync();
```

Chained `Where` expressions are combined with logical AND.

## Complex predicates

Predicates may mix nested `&&` and `||` groups:

```csharp
List<Person> results = await people.Where(person =>
    (person.Age >= 18 && person.Age <= 30) &&
    (
        person.City == "New York" ||
        person.City == "San Francisco" ||
        person.Name.StartsWith("Ada")
    )).ToListAsync();
```

Magic converts the expression tree into a universal predicate tree, partitions compatible branches into indexed or compound-indexed operations, and sends incompatible branches through the cursor engine. You do not need to rewrite the predicate into raw IndexedDB calls.

The optimizer may transform equivalent conditions. For example, several equality alternatives on one indexed property can become an IndexedDB `anyOf` lookup, and compatible lower/upper bounds can become a range.

## Common supported expressions

The C# translator supports these principal expression families:

- Equality and inequality: `==`, `!=`
- Ranges: `>`, `>=`, `<`, `<=`
- Boolean properties: `person => person.IsActive`
- Logical composition: `&&`, `||`, and supported negation forms
- Strings: `Equals`, `StartsWith`, `EndsWith`, and `Contains`, including supported `StringComparison` overloads
- Membership: `values.Contains(person.Property)`
- Collection containment: `person.Values.Contains(value)`
- Length comparisons on supported strings or collections
- Null checks: `property == null` and `property != null`
- Date/time comparisons, including supported `Date`, `Year`, `Month`, `Day`, `DayOfWeek`, and `DayOfYear` member comparisons
- Enum comparisons, including explicit enum casts

Support does not mean every expression is indexable. `Contains`, `EndsWith`, null checks, component-level date operations, and case-insensitive matching generally require cursor evaluation. See the [query expression reference](../reference/query-expressions.md).

Unsupported method calls or expression shapes should be treated as translation errors, not as arbitrary C# code that Magic can execute inside IndexedDB.

## Indexed and cursor paths

Calling `Where` asks Magic to choose the best execution plan. An individual branch may use:

1. A native index lookup.
2. A compound-index lookup when the schema and predicate align.
3. The cursor engine when no compatible index path can represent the branch.

Use `Cursor` only when you intentionally want the entire predicate processed by the cursor engine:

```csharp
List<Person> results = await people
    .Cursor(person => person.Name.Contains(
        searchText,
        StringComparison.OrdinalIgnoreCase))
    .ToListAsync();
```

Read [`Where` versus `Cursor`](where-vs-cursor.md) before making forced-cursor queries the default.

## Execute a query

Materialize all matching records:

```csharp
List<Person> results = await people
    .Where(person => person.IsActive)
    .ToListAsync();
```

Process results progressively:

```csharp
await foreach (Person person in people
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    await ProcessAsync(person, cancellationToken);
}
```

Find one record:

```csharp
Person? first = await people.FirstOrDefaultAsync(
    person => person.Email == email);

Person? last = await people.LastOrDefaultAsync(
    person => person.IsActive);
```

You may also order before selecting the first or last record:

```csharp
Person? youngest = await people
    .OrderBy(person => person.Age)
    .FirstOrDefaultAsync();
```

## Query the whole table

The root query can execute without a predicate:

```csharp
List<Person> all = await people.ToListAsync();
int count = await people.CountAsync();
```

`CountAsync()` counts the entire table. There is currently no filtered `CountAsync()` on the staged query interfaces.

## In-memory filtering after a finalized query

Some operations return `IMagicQueryFinal<T>`. Its `WhereAsync` method first materializes the IndexedDB result and then evaluates the supplied predicate in .NET:

```csharp
IEnumerable<Person> result = await people
    .Take(100)
    .WhereAsync(person => ExpensiveDotNetOnlyCheck(person));
```

This is deliberately different from `Where`: it is an in-memory operation and cannot improve the IndexedDB execution plan.

## Fluent-interface guardrails

Magic's staged interfaces intentionally limit which operations are available next. Let IntelliSense and compile-time return types guide the chain. In particular:

- Apply all IndexedDB `Where` predicates before pagination.
- Use `Take(n).Skip(m)`, not `Skip(m).Take(n)`.
- Use `Cursor` when you need cursor-only additions such as `StableOrdering()`.
- Execute or use `WhereAsync` after a chain reaches `IMagicQueryFinal<T>`.

See [ordering and pagination](ordering-and-pagination.md) for the complete rationale and examples.
