# Errors, cancellation, and recovery

Magic IndexedDB crosses expression translation, JSON serialization, Blazor interop, JavaScript, Dexie, IndexedDB, and browser storage. Failures can originate at any of those boundaries. Applications should handle operations as browser-backed I/O, not as infallible in-memory LINQ.

## Failure stages

| Stage | Typical cause | When it appears |
|---|---|---|
| Registration validation | Invalid keys, conflicting Magic attributes, or duplicate compound definitions | During `AddMagicBlazorDB` when `isDebug` is `true` |
| Query composition | Invalid `OrderBy` selector shape | When the operator is added |
| Query translation | Unsupported expression node, member, conversion, or method | Usually when the query is executed |
| Serialization | Unsupported collection construction, converter failure, malformed JSON, or incompatible stored value | Before browser dispatch or while materializing a response |
| Browser operation | Duplicate key, unique-index violation, invalid IndexedDB key, quota/storage failure, or blocked lifecycle operation | While awaiting JavaScript interop |
| Streaming transport | JavaScript producer failure, cancellation, incomplete stream, or chunk deserialization failure | During `await foreach` |
| Object lifetime | Use of a disposed scoped service | Before an operation is dispatched |

Unsupported query syntax normally surfaces as an `InvalidOperationException` containing expression context. Constructor ambiguity uses `MagicConstructorException`. Browser and interop failures can arrive through the Blazor JavaScript interop exception path. Do not make recovery depend on the exact text of an exception message.

`MagicException` remains public for compatibility, but it is not a universal wrapper around every failure produced by the library.

## Deferred query failures

`Where` records an expression; it does not execute the query. Translation therefore commonly occurs at `ToListAsync`, `AsAsyncEnumerable`, `FirstOrDefaultAsync`, or `LastOrDefaultAsync`:

```csharp
try
{
    List<Person> result = await people
        .Where(person => UnsupportedApplicationMethod(person.Name))
        .ToListAsync();
}
catch (InvalidOperationException exception)
{
    // Treat this as an unsupported query contract, not a transient storage failure.
    LogTranslationFailure(exception);
}
```

Do not automatically retry deterministic translation or schema-validation failures. Change the query or model.

## Cancellation coverage

Cancellation support varies by method:

| Operation | Public token | Contract |
|---|---|---|
| `AsAsyncEnumerable` | Yes | Stops enumeration/transport waiting and cleans up the stream instance. Already yielded items remain consumed. |
| Add, update, and delete methods | Yes | Can cancel .NET serialization or waiting; does not promise rollback after browser dispatch. |
| `GetStorageEstimateAsync` | Yes | Cancels the streamed interop request/response path. |
| `ToListAsync`, first/last, and `CountAsync` | No | No public cancellation token in the current API. |
| `ClearTable` | No | No public cancellation token in the current API. |
| Database open, close, delete, and existence checks | No | No public cancellation token in the current API. |

Cancellation means the caller stopped waiting or consuming. It does not establish whether a dispatched write committed. See [writes and transactions](writes-and-transactions.md).

## Streaming failures

An async stream can yield valid items and then fail. Consumers that create external side effects should decide whether those effects are resumable or idempotent:

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
    // Expected cancellation; retain a resume checkpoint if required.
}
```

Concurrent streams use independent transport identities, but that isolation does not turn a stream into a snapshot transaction. Records can still be affected by ordinary application activity while browser operations are running.

## Recovery guidance

- Translation or model-contract failure: fix the expression or schema; do not retry unchanged.
- Unique/key failure: correct the key or conflicting value, then issue a new operation.
- Failed or cancelled bulk write: read affected keys before retrying because all-or-nothing behavior is not promised.
- Quota/storage failure: preserve unsaved application state, reduce usage or ask the user for an explicit recovery action.
- Interop or disconnected-circuit failure: wait until the application is interactive/connected and create a fresh operation.
- Materialization failure after an application upgrade: retain the original data and use an explicit migration or compatible model rather than deleting it automatically.

## Logging

Development registration enables table validation and informational JavaScript diagnostics. Errors and warnings remain visible when informational debug logging is disabled. Log enough application context to identify the database, table, operation, and query shape, but do not log complete offline records when they may contain sensitive data.
