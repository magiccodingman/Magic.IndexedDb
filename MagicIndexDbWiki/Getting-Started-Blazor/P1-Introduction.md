# **Introduction to LINQ to IndexedDB in Blazor**

## **1. Getting Started**

Before we dive into **queries**, let's first **set up Magic IndexedDB in your Blazor pages**.

### **🔹 Add Magic IndexedDB to `_Imports.razor`**

To avoid writing `@using Magic.IndexedDb` on every page, **add it to your `_Imports.razor`**:

```razor
@using Magic.IndexedDb
```

### **🔹 Inject Magic IndexedDB Into Your Pages**

In any **Blazor page or component** where you want to use **Magic IndexedDB**, inject the service at the top:

```razor
@inject IMagicIndexedDb _MagicDb
```

Boom! **You're plugged in** and ready to go! 🎉

---

## **2. Initializing a Query**

Now that you've injected `_MagicDb`, you can start querying IndexedDB!  
There are **four ways** to initialize a query:

### **✅ 1. Default Database Query**

```csharp
IMagicQuery<Person> personQuery = await _MagicDb.Query<Person>();
```

- **Targets the default database** associated with `Person`.
- If your `Person` table is linked to a **single database**, this is the best way to query it.

### **✅ 2. Querying an Assigned Database (Strongly Typed)**

```csharp
IMagicQuery<Person> employeeDbQuery = await _MagicDb.Query<Person>(x => x.Databases.Client);
```

- **Explicitly specifies which database** to query.
- `Person` is **configured** to exist in the `Client` database, so this is a **safe and recommended** approach.

### **⚠️ 3. Querying an Unassigned Database (Not Recommended)**

```csharp
IMagicQuery<Person> animalDbQuery = await _MagicDb.Query<Person>(IndexDbContext.Animal);
```

- `Person` does **not belong** to the `Animal` database.
- **Magic IndexedDB allows this for flexibility, but it's discouraged.**
- **Why?** It **bypasses safety checks** and may require **manual migrations**.

### **🚨 4. Full Override Query (Strongly Discouraged)**

```csharp
IMagicQuery<Person> unassignedDbQuery = await _MagicDb.QueryOverride<Person>("DbName", "SchemaName");
```

- **Completely overrides** database and schema restrictions.
- **Magic IndexedDB cannot protect you** if you use this.
- **DO NOT DO THIS unless you absolutely know what you're doing.**

> **❌ Avoid unassigned queries and full overrides unless necessary.**
> 
> - They **break the built-in migration system**.
> - They **require manual fixes** when schema updates occur.
> - You **lose all built-in protection mechanisms**.

### **Which One Should You Use?**

| **Query Type**                                                  | **Safety Level** | **When to Use?**                                         |
| --------------------------------------------------------------- | ---------------- | -------------------------------------------------------- |
| `await _MagicDb.Query<Person>();`                               | ✅ Safe           | When `Person` has **one** default database.              |
| `await _MagicDb.Query<Person>(x => x.Databases.Client);`        | ✅ Safe           | When `Person` has **multiple valid databases**.          |
| `await _MagicDb.Query<Person>(IndexDbContext.Animal);`          | ⚠️ Risky         | When `Person` is **not part of the specified database**. |
| `await _MagicDb.QueryOverride<Person>("DbName", "SchemaName");` | 🚨 Dangerous     | **Only for advanced niche use cases**.                   |

---

## **3. Basic CRUD Operations**

Now that you **know how to query**, let's look at **basic IndexedDB operations**.

### **🔹 Adding Data**

```csharp
await personQuery.AddAsync(new Person { Name = "John Doe", Age = 30 });
```

### **🔹 Adding Multiple Records (Batch Insert)**

```csharp
List<Person> persons = new List<Person>
{
    new Person { Name = "Alice", Age = 25 },
    new Person { Name = "Bob", Age = 28 }
};

await personQuery.AddRangeAsync(persons);
```

### **🔹 Updating Data**

```csharp
var person = new Person { Name = "John Doe", Age = 31 };
await personQuery.UpdateAsync(person);
```

### **🔹 Updating Multiple Records**

```csharp
await personQuery.UpdateRangeAsync(persons);
```

### **🔹 Deleting Data**

```csharp
await personQuery.DeleteAsync(person);
```

### **🔹 Deleting Multiple Records**

```csharp
await personQuery.DeleteRangeAsync(persons);
```

### **🚀 Bulk Clearing a Table**

```csharp
await personQuery.ClearTable();
```

- **Deletes all data** inside the table **instantly**.
- **Use with caution!** 🔥

---

# **Using LINQ to IndexedDB – Query Syntax & Best Practices**

## **1. Supported Query Operations**

Magic IndexedDB **fully supports LINQ-style queries**, enabling **powerful data retrieval and filtering** while enforcing **best practices** to ensure optimal IndexedDB performance.

There are **two primary query operations** in Magic IndexedDB:

1️⃣ **`.Where(x => YourPredicate)`** → **Optimized for Indexed Queries**  
2️⃣ **`.Cursor(x => YourPredicate)`** → **Meta-data Driven Cursor Queries**

Before we dive in, there's something **critical** you must know:

### **🚨 IndexedDB Has a Backwards Take/Skip Order!**

In **every language and database system**, `Skip()` is always **before** `Take()`.  
✅ **Example in SQL:**

```sql
SELECT * FROM People ORDER BY Age LIMIT 10 OFFSET 5;
```

Here, **`OFFSET 5` (skip)** happens **before** **`LIMIT 10` (take)**.

**🚨 In IndexedDB, this is reversed.**

- **If you call `Skip(5).Take(10)`, only `Skip(5)` will apply** (IndexedDB ignores `Take`).
- **If you call `Take(10).Skip(5)`, it works correctly** (Skip will execute **after** Take).

🔥 **Magic IndexedDB handles this for you**—but you **must always write** `Take()` **before** `Skip()` to avoid unexpected behavior. **This is also an enforced behavior by the interfaces to protect you.** As the interfaces on the query won't allow you to append any `Take` or `TakeLast` after a `Skip`.

---

## **2. Using `.Where()` for Optimized Queries**

The `.Where()` method **translates LINQ expressions into the most efficient IndexedDB queries possible**.  
Multiple `.Where()` statements **can be chained** together.

### **✅ Basic `.Where()` Query**

```csharp
await personQuery.Where(x => x.Age > 30)
                 .Where(x => x.TestInt == 9 || x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                 .ToListAsync();
```

- **Multiple `.Where()` clauses are supported.**
- **Deferred execution works identically to LINQ to SQL**.
- Queries execute with **`ToListAsync()`** or **`AsAsyncEnumerable()`**.

### **🔹 Streaming Results with `AsAsyncEnumerable()`**

```csharp
await foreach (var person in personQuery.Where(x => x.Age > 30).AsAsyncEnumerable())
{
    Console.WriteLine($"Name: {person.Name}");
}
```

- **Yields results as they come in**.
- **Reduces memory usage** when processing large datasets.
- **Enforces unique ID consistency** across results.

> **🚀 Magic IndexedDB’s custom-built interop layer enables real-time yielding.**  
> However, note that **yielding incurs additional serialization costs**, making bulk returns faster.

---

## **3. Query Execution Model – Understanding Indexed vs. Cursor Queries**

Magic IndexedDB automatically **categorizes queries** into one of three optimized execution types:

### **✅ Indexed Queries**

- **Fastest** queries.
- **Utilize IndexedDB’s built-in indexing**.
- Example:
    
    ```csharp
    await personQuery.Where(x => x.Name == "Alice").ToListAsync();
    ```
    
    - Since `Name` has an **index**, it fires an **Indexed Query**.

### **⚡ Combined Indexed Queries**

- **Utilizes multiple indexes together** for efficient searching.
- **More powerful than standard IndexedDB queries**.
- Example:
    
    ```csharp
    await personQuery.Where(x => x.Age > 30 && x.TestInt == 9).ToListAsync();
    ```
    
    - If both `Age` and `TestInt` are indexed, **this is an optimized combined indexed query**.

### **🛑 Cursor-Based Queries (Meta-Data Driven)**

- **Slower, but allows for more complex filtering.**
- **Keeps only meta-data in memory until the exact desired result is known.**
- Example:
    
    ```csharp
    await personQuery.Where(x => x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync();
    ```
    
    - Since `Contains()` is **not natively indexed**, a **cursor query fires instead**.

> **🔹 The more your query relies on cursors, the slower it will be.**  
> **To optimize performance, ensure your tables have proper indexes!**  
> 📖 **[Read the Fundamentals Guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Fundamentals/P1-How-The-LINQ-To-IndexDB-works.md)** for best indexing practices.

---

## **4. Query Interface Enforcements & Best Practices**

To prevent you from **accidentally breaking IndexedDB optimizations**, Magic IndexedDB **enforces best practices** using **interfaces that restrict unsafe query combinations**.

### **Query Flow – How Queries Are Structured**

Each query starts with:

```csharp
public interface IMagicQuery<T> : IMagicExecute<T> where T : class
```

- This enforces **safe** LINQ structures.

### **Query Staging – Ensuring IndexedDB-Compatible Queries**

```csharp
public interface IMagicQueryStaging<T> : IMagicExecute<T> where T : class
{
    IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate);
    IMagicQueryPaginationTake<T> Take(int amount);
    IMagicQueryFinal<T> Skip(int amount);
    IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate);
}
```

- **Once you call `.Skip()` or `.Take()`, further filtering must be done in memory**.
- **Order operations do not guarantee IndexedDB-native ordering**.

### **Final Query Stage – No More IndexedDB Execution Allowed**

```csharp
public interface IMagicQueryFinal<T> : IMagicExecute<T> where T : class
{
    Task<IEnumerable<T>> WhereAsync(Expression<Func<T, bool>> predicate);
}
```

- Once you reach **this stage**, **all further filtering happens in memory**.

> **🚀 Magic IndexedDB’s system ensures that your queries are optimized for IndexedDB execution wherever possible.**  
> It **prevents you from making mistakes** that would force everything into memory unnecessarily.

---

## **5. Using `.Cursor()` for Full Cursor-Based Queries**

If you **explicitly want to use a cursor query**, use `.Cursor()` instead of `.Where()`.

### **✅ Cursor Query Example**

```csharp
await personQuery.Cursor(x => x.Name == "Zack").ToListAsync();
```

- **Forces all operations into a cursor query**.
- **Allows deeper filtering than Indexed Queries**.
- **Can still use `.OrderBy()`, `.Take()`, `.Skip()`**, etc.

### **Cursor Query Interface**

```csharp
public interface IMagicCursor<T> : IMagicExecute<T> where T : class
{
    IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate);
    IMagicCursorStage<T> Take(int amount);
    IMagicCursorSkip<T> Skip(int amount);
}
```

- Cursor queries **unlock more capabilities** but **sacrifice performance**.

> **🚨 Only use `.Cursor()` when you know an indexed query won’t work!**  
> Otherwise, **stick with `.Where()` for better performance**.

---

## **6. Supported LINQ Operations**

Magic IndexedDB **supports a rich LINQ feature set**, including:

### **✅ Comparison Operators**

```csharp
await personQuery.Where(x => x.Age > 30).ToListAsync();
await personQuery.Where(x => x.Name == "Alice").ToListAsync();
```

### **✅ String Operations**

```csharp
await personQuery.Where(x => x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)).ToListAsync();
await personQuery.Where(x => x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync();
await personQuery.Where(x => x.Name.Equals("John", StringComparison.OrdinalIgnoreCase)).ToListAsync();
```

### **✅ Deeply Nested `||` Conditions**

```csharp
await personQuery.Where(x => (x.Age > 40 && x.TestInt == 9) || x.Name.Contains("bo")).ToListAsync();
```

- **IndexedDB does not natively support this!**
- **Magic IndexedDB’s flattening algorithm makes it possible**.

---

# **Executing Queries Directly Without `.Where()` or `.Cursor()`**

Instead of using `.Where()` or `.Cursor()`, you can execute queries **directly** on the query itself, just like LINQ to SQL.

### **🔹 Retrieving All Records**

```csharp
await _MagicDb.Query<Person>().ToListAsync();
```

- **Fetches the entire table**, equivalent to `SELECT * FROM Person;`.

### **🔹 Ordering & Pagination**

```csharp
await _MagicDb.Query<Person>().OrderBy(x => x.Age).Take(5).ToListAsync();
await _MagicDb.Query<Person>().OrderByDescending(x => x.Id).Skip(10).Take(5).ToListAsync();
```

- **Supports ordering, skipping, and taking records just like SQL.**
- **Reminder:** **IndexedDB requires `Take()` before `Skip()`.**

### **🔹 Fetching a Single Record**

```csharp
await _MagicDb.Query<Person>().FirstOrDefaultAsync();
await _MagicDb.Query<Person>().LastOrDefaultAsync();
```

- Retrieves the **first** or **last** record efficiently.

This allows for **simple, SQL-like querying** while **leveraging IndexedDB optimizations** behind the scenes! 🚀

---

# **LINQ to IndexedDB – Full Querying Guide & Operations**

## **1. Understanding Query Operations in Magic IndexedDB**

Magic IndexedDB provides a **true LINQ to IndexedDB experience**, translating LINQ expressions into **optimized IndexedDB queries**.

There are **two primary querying methods**:

|**Query Method**|**Purpose**|**Best For**|
|---|---|---|
|`.Where(x => Predicate)`|**Optimized IndexedDB Queries**|**Fast queries using indexed fields**|
|`.Cursor(x => Predicate)`|**Full Cursor Querying**|**Advanced filtering when indexing isn’t possible**|

> **TL;DR:** Always prefer `.Where()` **for performance**. Use `.Cursor()` **only when needed**.

---

## **2. Query Execution – IndexedDB vs. Cursor Queries**

Before diving into **syntax**, it's **important to understand how queries execute**.

- **IndexedDB only supports certain query patterns efficiently**.
- **Magic IndexedDB automatically categorizes your query** into:
    - **✅ Indexed Query**
    - **⚡ Combined Indexed Query**
    - **🛑 Cursor Query** (Meta-data driven)

Each `||` condition **is broken down into independent queries**.

- **If an `&&` operation cannot be fully indexed**, that entire `&&` block becomes a **Cursor Query**.
- **Using `StringComparison.OrdinalIgnoreCase` forces a cursor**.

📖 **[Read the LINQ to IndexedDB Fundamentals](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Fundamentals/P1-How-The-LINQ-To-IndexDB-works.md)** to understand **how your queries are optimized!**

---

## **3. Supported LINQ Operations**

Magic IndexedDB **supports** a rich set of **LINQ expressions** for querying your data.

### **✅ Comparison Operators**

```csharp
await personQuery.Where(x => x.Age > 30).ToListAsync();
await personQuery.Where(x => x.Name == "Alice").ToListAsync();
```

### **✅ String Operations**

```csharp
await personQuery.Where(x => x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)).ToListAsync();
await personQuery.Where(x => x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync();
await personQuery.Where(x => x.Name.Equals("John", StringComparison.OrdinalIgnoreCase)).ToListAsync();
```

> **⚠ Using `StringComparison.OrdinalIgnoreCase` forces a cursor query.**  
> **Use indexed fields whenever possible!**

### **✅ Deeply Nested `||` Conditions**

```csharp
await personQuery.Where(x => (x.Age > 40 && x.TestInt == 9) || x.Name.Contains("bo")).ToListAsync();
```

> **IndexedDB does NOT support deeply nested `||` operations** by default.  
> **Magic IndexedDB flattens and optimizes them for you**.

---

## **4. Query Operations – Order of Execution**

The following **table defines the operations available** and how they interact.

### **🔹 Indexed Query (`.Where()`) Execution Order**

|**Operation**|**Allowed Before**|**Allowed After**|**Notes**|
|---|---|---|---|
|`.Where()`|**Start**|`.Where()`, `.OrderBy()`, `.ToListAsync()`|Supports **indexing optimizations**.|
|`.OrderBy()`|`.Where()`|`.Take()`, `.Skip()`, `.ToListAsync()`|**Ordering is NOT guaranteed in IndexedDB**. Must reapply after query.|
|`.Take()`|`.Where()`, `.OrderBy()`|`.Skip()`, `.ToListAsync()`|**Must be before `.Skip()` in IndexedDB**.|
|`.Skip()`|`.Take()`|`.ToListAsync()`|**Skipping before taking ignores take!**|
|`.FirstOrDefaultAsync()`|`.Where()`|**End**|Returns **first matching** record.|
|`.LastOrDefaultAsync()`|`.Where()`|**End**|Returns **last matching** record.|

### **🔹 Cursor Query (`.Cursor()`) Execution Order**

|**Operation**|**Allowed Before**|**Allowed After**|**Notes**|
|---|---|---|---|
|`.Cursor()`|**Start**|`.OrderBy()`, `.Take()`, `.Skip()`, `.ToListAsync()`|**Allows full flexibility but is slower**.|
|`.OrderBy()`|`.Cursor()`|`.Take()`, `.Skip()`, `.ToListAsync()`|**Order will not be enforced at IndexedDB level**.|
|`.TakeLast()`|`.OrderBy()`|`.ToListAsync()`|Forces **in-memory** filtering.|
|`.Skip()`|`.Take()`|`.ToListAsync()`|**Always apply `.Take()` before `.Skip()`**.|
|`.FirstOrDefaultAsync()`|`.Cursor()`|**End**|Returns **first matching** record.|
|`.LastOrDefaultAsync()`|`.Cursor()`|**End**|Returns **last matching** record.|

> **🚨 Once you use `.Skip()`, `.Take()`, `.OrderBy()`, filtering becomes memory-based!**  
> IndexedDB **does not allow additional `.Where()` operations** **after these**.

---

## **5. Cursor Queries – Unlocking Full IndexedDB Power**

A `.Cursor()` query **forces everything into a cursor-based scan**, removing restrictions but also **removing optimizations**.

### **✅ Cursor Query Example**

```csharp
await personQuery.Cursor(x => x.Name == "Zack").ToListAsync();
```

- **Forces all operations into a cursor query**.
- **Unlocks `.TakeLast()`, `.Skip()`, and `.OrderBy()`**.
- **Can be combined with `.Where()` but will convert entire query into a cursor**.

---

## **6. Query Interface Pathways**

The following **flowchart** defines the **order in which query operations are enforced**.

### **🔹 Query Flowchart**

```
Start → Where() → OrderBy() → Take() → Skip() → Execution (.ToListAsync())
                   ↘ Cursor() → TakeLast() → Execution
```

> **🔥 Magic IndexedDB enforces these rules at the interface level!**  
> You can’t accidentally write **non-performant IndexedDB queries**.

---

## **7. Full Query Examples**

```csharp
// Basic WHERE statement with optimized indexing
await personQuery.Where(x => x.Age > 30).ToListAsync();

// WHERE with OR conditions (flattened optimization)
await personQuery.Where(x => (x.Age > 40 && x.TestInt == 9) || x.Name.Contains("bo")).ToListAsync();

// ORDER BY, TAKE, and SKIP (Indexed)
await personQuery.Where(x => x.Age > 30)
                 .OrderBy(x => x.Age)
                 .Take(3)
                 .Skip(2)
                 .ToListAsync();

// Cursor-based querying (Allows `.TakeLast()`)
await personQuery.Cursor(x => x.Age > 30)
                 .OrderBy(x => x.Age)
                 .TakeLast(2)
                 .ToListAsync();

// FirstOrDefault and LastOrDefault
await personQuery.OrderBy(x => x.Age).FirstOrDefaultAsync();
await personQuery.OrderBy(x => x.Age).LastOrDefaultAsync();
```

---

## **8. Summary – Writing Efficient LINQ to IndexedDB Queries**

✅ **Use `.Where()` for optimal IndexedDB queries.**  
✅ **Use `.Cursor()` only when necessary** (e.g., advanced filtering).  
✅ **Always write `.Take()` before `.Skip()` (IndexedDB enforces reversed behavior).**  
✅ **Use `StringComparison.OrdinalIgnoreCase` carefully—it forces a cursor query.**  
✅ **Once `.Skip()`, `.Take()`, `.OrderBy()` are used, further `.Where()` calls are ignored.**

📖 **[Read the Fundamentals Guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Fundamentals/P1-How-The-LINQ-To-IndexDB-works.md)** to master IndexedDB optimizations! 🚀

---
# Final Thoughts – Optimizing Your Queries**

To **write the most efficient** IndexedDB queries: ✅ **Use indexed fields whenever possible**.  
✅ **Avoid deeply nested `||` conditions unless necessary**.  
✅ **Use `.Where()` instead of `.Cursor()` whenever possible**.  
✅ **Always write `Take()` before `Skip()`!**

> **🚀 True LINQ to IndexedDB is now a reality.**  
> 🔥 Magic IndexedDB **translates** your intent into the most optimized IndexedDB queries possible.

🔗 **Learn More: [Read the Fundamentals Guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Fundamentals/P1-How-The-LINQ-To-IndexDB-works.md)**.