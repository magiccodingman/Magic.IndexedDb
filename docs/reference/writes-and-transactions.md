# Adding, updating, and deleting records

You write to a table through the same `IMagicQuery<T>` object used for queries.

## Quick reference

| Method | What it does | Return value |
|---|---|---|
| `AddAsync(item)` | Inserts one record | None |
| `AddRangeAsync(items)` | Inserts several records | None |
| `UpdateAsync(item)` | Updates the record with the same primary key | `1` if found, otherwise `0` |
| `UpdateRangeAsync(items)` | Inserts or replaces each record | Number of requested records |
| `DeleteAsync(item)` | Deletes the matching primary key | None |
| `DeleteRangeAsync(items)` | Deletes the requested primary keys | Number of requested keys |
| `ClearTable()` | Deletes every record in the table | None |

Updates and deletes read the primary key from the object you pass in. Make sure every part of a compound key has a value.

## `UpdateAsync` and `UpdateRangeAsync` differ

`UpdateAsync` only updates an existing record:

```csharp
int updated = await people.UpdateAsync(person, cancellationToken);

if (updated == 0)
{
    // Nothing had this primary key.
}
```

`UpdateRangeAsync` uses upsert behavior. If a key does not exist, the method inserts that record:

```csharp
int processed = await people.UpdateRangeAsync(batch, cancellationToken);
```

The returned number is the size of the batch. It does not tell you how many records were inserted, replaced, or changed.

`DeleteRangeAsync` works the same way: its return value is the number of keys requested, not the number that existed.

## Bulk operations are not transactions

Magic does not expose a transaction API yet. Do not assume a failed bulk operation rolled back the whole batch. Some records may have been written before the failure.

After a failed or cancelled bulk write, query the affected keys before retrying. It also helps to validate keys and unique values before sending the batch.

## Cancellation does not undo a write

The add, update, and delete methods accept a cancellation token. Cancellation can stop serialization or stop .NET from waiting for the result, but it does not undo work that has already reached IndexedDB.

`ClearTable()` does not accept a cancellation token.

## Unique indexes

`[MagicUniqueIndex]` makes IndexedDB reject duplicate values. A failed insert does not leave the table unusable; you can correct the value and try another operation.

With a bulk insert or update, read the affected records after a unique-index failure instead of guessing which items were written.

## Auto-incremented keys

`AddAsync` and `AddRangeAsync` do not return an auto-generated key, and they do not write it back to your object.

If you need the new key, give the record another unique value and query it back after insertion. An application-generated key such as a `Guid` is often simpler when you need the identity immediately.

See [your first workflow](../getting-started/first-application.md) for basic CRUD examples and [errors and cancellation](errors-and-cancellation.md) for recovery advice.
