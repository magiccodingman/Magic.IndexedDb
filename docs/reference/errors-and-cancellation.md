# Errors and cancellation

An error may come from the C# query translator, JSON serialization, JavaScript interop, IndexedDB, or the browser's storage system. Where it happens usually tells you whether retrying makes sense.

## Common errors

| Error | Likely cause | What to do |
|---|---|---|
| Table validation fails during startup | Invalid key, duplicate compound definition, or conflicting Magic attributes | Fix the model |
| `InvalidOperationException` while running a query | Magic cannot translate part of the expression | Rewrite the query using the [supported expressions](query-expressions.md) |
| `MagicConstructorException` | More than one constructor has the same constructor attribute | Leave only one selected constructor |
| JavaScript interop error | IndexedDB rejected an operation, the browser disconnected, or JavaScript failed | Check the inner browser error and operation |
| JSON error | A stored value no longer matches the model, or a converter failed | Check the stored data and recent model changes |
| `OperationCanceledException` | The supplied token was cancelled | Handle it as cancellation, then verify any write that was already sent |

`MagicException` is public, but Magic does not wrap every failure in it. Catch the error that matches the operation instead of relying on one library-wide exception type.

## Query errors happen when the query runs

`Where` saves the expression for later. Magic normally translates it when you call `ToListAsync`, `AsAsyncEnumerable`, `FirstOrDefaultAsync`, or `LastOrDefaultAsync`.

```csharp
try
{
    List<Person> result = await people
        .Where(person => UnsupportedApplicationMethod(person.Name))
        .ToListAsync();
}
catch (InvalidOperationException exception)
{
    LogTranslationFailure(exception);
}
```

Retrying the same unsupported expression will produce the same error. Rewrite it using a supported expression, or load the records and run that part in .NET.

## Which methods accept cancellation

| Operation | Cancellation token? |
|---|---|
| `AsAsyncEnumerable` | Yes |
| Add, update, and delete methods | Yes |
| `GetStorageEstimateAsync` | Yes |
| `ToListAsync`, first/last, and `CountAsync` | No |
| `ClearTable` | No |
| Database open, close, delete, and existence checks | No |

Cancelling a stream stops further items and cleans up its interop resources. Items already yielded remain processed.

Cancelling a write does not roll it back after it has reached the browser. Read the affected keys before retrying a cancelled bulk operation. See [adding, updating, and deleting records](writes-and-transactions.md).

## Handling stream errors

A stream can yield records and then fail. If each record triggers another side effect, make that work safe to resume or repeat:

```csharp
try
{
    await foreach (Person person in query.AsAsyncEnumerable(cancellationToken))
    {
        await ExportIdempotentlyAsync(person, cancellationToken);
    }
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Save a resume point here if the export needs one.
}
```

## Useful recovery rules

- Fix schema and translation errors instead of retrying them.
- Correct duplicate keys or unique values before writing again.
- Read records back after a failed or cancelled bulk write.
- Keep unsaved work available if the browser runs out of space.
- Do not delete the database just because an upgraded model cannot read an old record.

Development mode enables schema validation and extra JavaScript logging. Errors and warnings still appear when the extra logging is disabled.
