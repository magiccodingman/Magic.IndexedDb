# Streaming results

Magic IndexedDB provides two execution styles:

- `ToListAsync()` materializes the result as a `List<T>`.
- `AsAsyncEnumerable()` progressively transfers and yields records.

## Materialize with `ToListAsync`

```csharp
List<Person> people = await table
    .Where(person => person.IsActive)
    .ToListAsync();
```

Use materialization when you need the complete collection, final query ordering, repeated enumeration, a known count, or subsequent in-memory LINQ.

The full result must remain live as a list, so application memory grows with the result size.

## Process progressively

```csharp
await foreach (Person person in table
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    await ExportAsync(person, cancellationToken);
}
```

Version 3's streaming transport drains chunks while JavaScript is producing them, measures chunk limits in UTF-8 bytes, avoids splitting Unicode code points, and disposes interop stream resources. JavaScript failures and incomplete streams propagate to .NET instead of silently producing a partial success.

Streaming reduces the need to hold the complete returned collection at once, but it is not a promise of constant memory for every query. The browser engine may still need metadata, keys, de-duplication state, ordered subsets, or batches to evaluate the plan.

## Ordering contract

Progressive delivery does not promise final arrival order. If ordering matters after streaming, buffer and sort explicitly:

```csharp
List<Person> received = [];

await foreach (Person person in table
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    received.Add(person);
}

List<Person> ordered = received
    .OrderBy(person => person.LastName)
    .ThenBy(person => person.Id)
    .ToList();
```

If you already need the whole collection solely to sort it, `ToListAsync()` is usually the clearer execution choice.

## Cancellation and errors

Pass a cancellation token and use it in downstream work:

```csharp
await foreach (Person person in query.AsAsyncEnumerable(cancellationToken))
{
    cancellationToken.ThrowIfCancellationRequested();
    await ProcessAsync(person, cancellationToken);
}
```

Wrap the enumeration in the same error handling you would use for any browser-backed operation. Exceptions may represent translation errors, JavaScript failures, browser storage errors, cancellation, or serialization failures.

## Message-size configuration

The limit supplied to `AddMagicBlazorDB` controls interop chunk sizing; it is not a result-set limit. Increasing it trades fewer messages for larger peak chunks. Start with the built-in `BlazorInteropMode` value and adjust only after measuring your application and transport.
