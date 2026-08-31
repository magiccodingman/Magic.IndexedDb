# Writes, bulk operations, and transactions

An `IMagicQuery<T>` is also the write surface for its table and selected database. This page defines the current write contracts, including distinctions that are easy to miss when moving between the single-record and range methods.

## Operation matrix

| Method | Browser operation | Success result | Important contract |
|---|---|---|---|
| `AddAsync(item)` | Insert | `Task` | Fails for an existing primary key or violated unique index. A generated key is not returned or assigned to `item`. |
| `AddRangeAsync(items)` | Bulk insert | `Task` | Uses insert semantics. It does not return generated keys and is not documented as all-or-nothing. |
| `UpdateAsync(item)` | Update existing key | `0` or `1` | Returns `0` when the key does not identify an existing row; it does not insert that row. |
| `UpdateRangeAsync(items)` | Bulk put | Input item count after success | Uses upsert semantics: a missing key can create a row. The count is not a count of rows that previously existed or whose values changed. |
| `DeleteAsync(item)` | Delete key | `Task` | Deleting an absent key is not reported as a not-found result. |
| `DeleteRangeAsync(items)` | Bulk delete | Input item count after success | The count is the number of requested keys, not proof that every key previously existed. |
| `ClearTable()` | Clear object store | `Task` | Removes every row from the selected table while retaining its schema. |

All update and delete methods derive the key from the supplied object. Populate every component of a compound key. For a generated single key, re-query the inserted record before trying to update or delete it because add methods do not populate the source object.

## Single update versus range update

The similarly named update methods do not have identical missing-row behavior:

```csharp
int updated = await people.UpdateAsync(person, cancellationToken);
if (updated == 0)
{
    // No row had this primary key.
}
```

`UpdateRangeAsync` currently uses a browser bulk-put operation. Treat each supplied record as an upsert:

```csharp
int requested = await people.UpdateRangeAsync(batch, cancellationToken);
```

On success, `requested` equals the number of supplied records. It does not distinguish inserts from replacements. Use `UpdateAsync` when the application must detect that one expected row is missing.

## Atomicity boundary

Magic IndexedDB does not currently expose a public transaction API. Range methods call the browser's bulk table operations directly, but the public contract does not promise that an entire range is rolled back when one item fails.

Consequently:

- Do not use a range call as an application transaction.
- Do not assume that catching an exception means no records were written.
- Re-query authoritative keys after a failed range operation before retrying.
- Make retries idempotent where possible.
- Validate keys, required values, and likely unique-index conflicts before dispatching a batch.

Several calls made sequentially are separate operations. For example, an `AddRangeAsync` followed by `DeleteRangeAsync` is not one atomic unit.

## Cancellation is not rollback

`AddAsync`, `AddRangeAsync`, `UpdateAsync`, `UpdateRangeAsync`, `DeleteAsync`, and `DeleteRangeAsync` accept a cancellation token. The token can stop .NET-side serialization, response reading, or awaiting. Once work has been dispatched to JavaScript and IndexedDB, cancellation is not a transaction rollback guarantee.

After cancellation of a write whose result matters, query the affected keys to establish the browser's actual state before retrying.

`ClearTable()` does not currently accept a cancellation token.

## Unique constraints and recovery

`[MagicUniqueIndex]` delegates uniqueness enforcement to IndexedDB. A duplicate write fails rather than replacing the existing row. A failed single insert does not make the table unusable; subsequent valid operations can continue.

For range operations, combine the non-atomicity rule with unique constraints: a unique-index failure means the caller should not infer the state of the other requested records without reading them back.

## No generated-key return path

The browser produces an auto-incremented key for a successful insert, but the public add methods intentionally return only `Task`. The current API neither returns that key nor mutates the input model.

When the application needs immediate identity, prefer an application-generated key such as a `Guid`. If an auto-increment key is required, include another unique value that can locate the newly added row, then query it back.

## Related documentation

- [Your first workflow](../getting-started/first-application.md) for ordinary CRUD examples
- [Errors and cancellation](errors-and-cancellation.md) for failure categories and recovery
- [Schema attributes and constructors](schema-attributes.md) for keys and unique indexes
- [Database management](../guides/database-management.md) for table clearing and database deletion
