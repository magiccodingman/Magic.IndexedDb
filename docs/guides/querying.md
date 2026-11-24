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
- Collection containment with a literal constant: `person.Values.Contains(3)`
- Captured-sequence quantifiers in supported shapes: `values.Any(...)` and `values.All(...)`
- Length comparisons on supported strings or collections
- Null checks: `property == null` and `property != null`
- Date/time comparisons, including supported `Date`, `Year`, `Month`, `Day`, `DayOfWeek`, and `DayOfYear` member comparisons
- Enum comparisons, including explicit enum casts

Captured membership and stored collection containment are different operations. `values.Contains(person.Id)` expresses “the property equals any captured value” and may use an index. `person.TagIds.Contains(42)` inspects a collection stored in each record and requires cursor evaluation. The documented stored-collection shape uses a literal constant; a captured argument in that position is not currently a supported translation shape. An empty captured membership set matches no rows.

Constant boolean predicates preserve ordinary boolean identities. `true && predicate` is the predicate, `false || predicate` is the predicate, `true || predicate` matches everything, and `false && predicate` matches nothing. Supported `Any` and `All` expressions over an empty captured sequence preserve the usual semantics: `Any` is false and `All` is true.

Support does not mean every expression is indexable. Stored collection `Contains`, string `Contains`, `EndsWith`, null checks, component-level date operations, and case-insensitive matching generally require cursor evaluation. See the [query expression reference](../reference/query-expressions.md).

Unsupported method calls or expression shapes should be treated as translation errors, not as arbitrary C# code that Magic can execute inside IndexedDB.

Arithmetic, property-to-property comparisons, arbitrary helper methods, and most nested member access are not supported just because the expression compiles. Write a false check as `person.IsActive == false`; unary `!person.IsActive` is not translated.

## Indexed and cursor paths

Calling `Where` asks Magic to choose the best execution plan. An individual branch may use:

1. A native index lookup.
2. A compound-index lookup when the schema and predicate align.
3. The cursor engine when no compatible index path can represent the branch.

Use `Cursor` when you want the entire predicate processed by the cursor engine:

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

## Valid method order

The return type of each method controls what can come next. IntelliSense will show the valid choices. The important rules are:

- Apply all IndexedDB `Where` predicates before pagination.
- Use `Take(n).Skip(m)`, not `Skip(m).Take(n)`.
- `IMagicQueryStaging<T>` does not expose `OrderBy`. For a filtered ordered list, use `Cursor(predicate).OrderBy(...)` or load the filtered records and order them in .NET.
- Use `Cursor` when you need cursor-only additions such as `StableOrdering()`.
- Execute or use `WhereAsync` after a chain reaches `IMagicQueryFinal<T>`.

See [ordering and pagination](ordering-and-pagination.md) for examples.
