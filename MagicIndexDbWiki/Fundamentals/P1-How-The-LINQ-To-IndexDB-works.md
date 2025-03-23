# **Optimizing Queries in Magic IndexedDB**

## **1. Understanding Query Partitioning in IndexedDB**

### **What is Query Partitioning?**

Partitioning is the process of analyzing your LINQ query and **breaking it down** into optimized IndexedDB queries.

Magic IndexedDB **examines your predicates** and **determines** if they can:

1. **Use an Indexed Query** (Best performance)
2. **Leverage a Compound Index Query** (Optimized for multi-column lookups)
3. **Fallback to a Cursor Query** (Last resort when indexes can’t be used)

### **How Partitioning Works:**

When you write a LINQ expression, **Magic IndexedDB scans for OR (`||`) operations** and **breaks them into separate queries**.

#### **Example Query:**

```csharp
var results = await _MagicDb.Query<Person>()
                 .Where(x => x.Email == "user@example.com" && x.Age > 30 || x.IsReallyCool)
                 .ToListAsync();
```

This gets **partitioned** into:

1. **Query A:** `x.Email == "user@example.com" && x.Age > 30`
2. **Query B:** `x.IsReallyCool == true`

Each **AND (`&&`) condition inside an OR group** is analyzed **to determine**:

- Can it be an **Indexed Query**?
- Can it be a **Compound Index Query**?
- If not, **it falls back to a Cursor Query.**

---

## **2. The Three Types of Queries in IndexedDB**

### **✅ Indexed Queries (Best Performance)**

An **indexed query** happens when **all conditions** in an AND (`&&`) group **match a single field index**.

#### **Example:**

```csharp
await _MagicDb.Query<Person>().Where(x => x.Email == "user@example.com").ToListAsync();
```

✅ **Uses an Indexed Query** → Fast lookup using IndexedDB’s native `.where()` method.

> **Tip:** **Simple equality (`==`) operations on indexed fields** perform best.

---

### **🔗 Compound Index Queries (Optimized Multi-Column Lookups)**

A **compound index** lets you **query multiple fields efficiently**—but **you must query all fields in the correct order**.

#### **Example:**

```csharp
await _MagicDb.Query<Person>().Where(x => x.LastName == "Smith" && x.FirstName == "John").ToListAsync();
```

✅ **Uses a Compound Index Query** → If **LastName** and **FirstName** are indexed together.

> **Tip:** To benefit from **compound indexes**, always query **all indexed fields in the correct order**.

> **⚠️ If you query only one part of a compound index, IndexedDB will not optimize it!**

---

### **🚨 Cursor Queries (Last Resort, Worst Performance)**

A **cursor query** is used when a condition **cannot be optimized with an index**.

#### **Example:**

```csharp
await _MagicDb.Query<Person>().Where(x => x.Name.Contains("John")).ToListAsync();
```

❌ **Requires a Cursor Query** → IndexedDB **does not support** `.Contains()`, so the system **must scan every record manually**.

> **Avoid cursor queries whenever possible!**  
> Use **indexed fields and structured queries** to improve performance.

---

## **3. The Optimization Process**

### **Step 1: Partitioning the Query**

1. **Break apart the query by OR (`||`) conditions**.
2. **Process each AND (`&&`) group separately**.

### **Step 2: Checking for Compound Index Queries**

1. **Check if all AND conditions match a known compound index.**
2. **If yes, execute a Compound Query.**
3. **If no, continue to Step 3.**

### **Step 3: Checking for Indexed Queries**

1. **Check if all AND conditions can be optimized as indexed queries.**
2. **If yes, execute an Indexed Query.**
3. **If no, continue to Step 4.**

### **Step 4: Fallback to Cursor Query**

1. **If the query cannot be optimized, execute a Cursor Query.**
2. **A cursor query scans all records manually—this should be avoided.**

---

## **4. How Query Additions Affect Optimization**

### **Understanding Query Additions**

Query additions **modify your query structure** by introducing **sorting, pagination, or limits**. While some additions **fully utilize IndexedDB’s indexing**, others **require transformations or force a cursor fallback**.

Magic IndexedDB **intelligently optimizes query additions** to:

- **Keep queries indexed whenever possible**
- **Leverage compound indexes and order optimizations**
- **Only force a cursor when necessary**

---

### **🚨 When Does a Query Become a Cursor Query?**

A **query must be executed using a cursor when**:

1. **At least one AND (`&&`) group cannot be expressed as a single indexed or compound index query.**
2. **A non-indexed field is used in sorting (`OrderBy`) or filtering.**
3. **A query addition (like `Skip`) is used on an unindexed query.**
4. **A complex OR (`||`) operation cannot be compressed into fewer indexed queries.**

---

### **🚀 Query Addition Rules & IndexedDB Optimizations**

|**Addition**|**Effect**|
|---|---|
|`.OrderBy(x => x.Age)`|✅ **Optimized if `Age` is indexed**|
|`.OrderBy(x => x.NonIndexedField)`|❌ **Forces a cursor query** (Ordering requires an index)|
|`.Skip(10).Take(5)`|✅ **Optimized when query is indexed**|
|`.Take(10)`|✅ **Optimized if query is indexed**|
|`.TakeLast(10)`|✅ **Optimized via smart transformation**|
|`.FirstOrDefaultAsync()`|✅ **Indexed if ordering is indexed**|
|`.LastOrDefaultAsync()`|✅ **Optimized via reverse query transformation**|

---

### **💡 How TakeLast() is Indexed**

Normally, IndexedDB **does not natively support `TakeLast()`**, but Magic IndexedDB **transforms the query** to **achieve the same effect** using indexed operations:

1. **Reverses sorting order (`OrderByDescending`)**
2. **Applies `Take(n)` to retrieve the last `n` elements efficiently**
3. **Returns results in the correct order after retrieval**

✅ **Optimized Example (Indexed Query)**

```csharp
await _MagicDb.Query<Person>()
              .OrderBy(x => x.Age)
              .TakeLast(5)
              .ToListAsync();
```

🔹 **Translates into:**

```javascript
table.where("Age").above(0).reverse().limit(5)
```

🚀 **Efficient and fully indexed!**

---

### **⚠️ When a Query Becomes a Cursor**

Queries only **fall back to a cursor** if **they cannot be fully executed using indexed queries**.

#### **🚨 Cursor Example (Due to Non-Indexable Condition)**

```csharp
await _MagicDb.Query<Person>()
              .Where(x => x.Email == "user@example.com" || x.Age > 30 || x.Name.Contains("John"))
              .Take(5)
              .ToListAsync();
```

🚨 **Forces a cursor query** because:

1. `Email == "user@example.com"` → ✅ **Indexed**
2. `Age > 30` → ✅ **Indexed**
3. `Name.Contains("John")` → ❌ **Not indexed (Requires full scan)**
4. **Since one condition is unindexable, the entire query must be executed as a cursor.**

---

### **✅ Summary: Optimizing Queries with Additions**

To **keep queries optimized**:

- ✅ **Use indexed fields whenever possible**
- ✅ **Leverage `.TakeLast()` only when an indexed field is used for ordering**
- ✅ **Always use `OrderBy()` on an indexed field**
- 🚨 **Avoid `Contains()` unless absolutely necessary**

> **💡 Remember:**  
> Magic IndexedDB **pushes IndexedDB to the limit** by transforming queries intelligently, ensuring **maximum performance** while preserving **accurate intent**. 🚀

---

## **5. Best Practices for Writing Optimized Queries**

### **✅ Key Takeaways to Maximize Performance**

- **Use indexed fields** as much as possible.
- **Leverage compound indexes** for multi-field lookups.
- **Minimize OR (`||`) operations**—they create multiple queries.
- **Avoid `.Contains()` and `.StartsWith()` (unless case-sensitive is off).**
- **Never use `.OrderBy()` on a non-indexed field.**
- **Avoid `TakeLast()` if possible—it always forces a cursor.**

---

## **6. Deep Dive: The Magic IndexedDB Optimization Layer**

### **How Does Optimization Work?**

Magic IndexedDB includes an **advanced optimization layer** that:

1. **Compresses queries** into fewer IndexedDB requests.
2. **Rearranges conditions** to maximize index usage.
3. **Combines multiple queries** into a single efficient query when possible.

---

### **🔍 How Query Compression Works**

**For each `||` operation, the query will always result in the same or fewer queries.**

✅ **Optimized Example:**

```csharp
await _MagicDb.Query<Person>()
              .Where(x => x.Age > 30 || x.Age < 20 || x.Age == 25)
              .ToListAsync();
```

**🔹 This will be optimized into a single query:**

```javascript
table.where("Age").anyOf([25]).or(table.where("Age").above(30)).or(table.where("Age").below(20))
```

This **combines multiple queries** into a **single efficient IndexedDB query**.

---

## **⚡ Query Compression Techniques Used**

The optimization layer **automatically applies** advanced compression techniques:

|**Optimization**|**How It Works**|**Example Transformation**|
|---|---|---|
|**Merges Equality Conditions**|`x.Age == 30||
|**Converts Ranges to BETWEEN**|`x.Age > 30 && x.Age < 40` → Uses `between(30, 40)`|`WHERE Age BETWEEN 30 AND 40`|
|**Combines Queries for Efficiency**|`x.Name == "John"||
|**Avoids Redundant Queries**|**Removes unnecessary conditions**|`x.Age > 30|

> **🚀 These optimizations make queries up to 10x faster!**

---

## **🎯 How IndexedDB Limitations Affect Optimization**

IndexedDB **is powerful but has some limitations** that impact query optimization.

### **🚀 Things IndexedDB is Good At**

✅ **Fast Indexed Lookups** (e.g., `.where("ID").equals(5)`)  
✅ **Efficient Range Queries** (e.g., `.where("Age").between(20, 30)`)  
✅ **Compound Indexes** (e.g., `.where(["LastName", "FirstName"]).equals(["Smith", "John"])`)  
✅ **Sorting & Pagination (when indexed)**

### **⚠️ IndexedDB Limitations**

❌ **No Native `LIKE` or `Contains()` Queries**  
❌ **No `ORDER BY` on non-indexed fields**  
❌ **No `TakeLast()` or Reverse Pagination**  - Well normally... We got you covered though!
❌ **No Joins or Complex Aggregates**

> **Magic IndexedDB works around these limitations** by:
> 
> - **Using cursors where needed.**
> - **Rewriting queries for optimal execution.**
> - **Applying query compression techniques.**

---

# **The Cursor Query: How Magic IndexedDB Handles Non-Indexed Queries**

## **🧐 What is a Cursor?**

A **cursor** in IndexedDB is similar to how SQL processes row-by-row searches when no index is available. It **scans the entire dataset**, checking each record to see if it meets your query conditions. Since IndexedDB does not support **complex filtering** (like case-insensitive searches or `Contains()` on non-indexed fields), a cursor **must be used** to process those queries.

In **Magic IndexedDB**, the cursor **only runs when absolutely necessary**, and when it does, it **does so in the most efficient way possible**. By leveraging **meta-data partitioning** and **batching optimizations**, Magic IndexedDB makes cursor queries **as performant as possible** while maintaining **low memory overhead**.

---

## **🚀 How the Cursor Works in Magic IndexedDB**

When a query **contains non-indexable operations**, Magic IndexedDB **translates** it into a **single cursor query** that **efficiently** finds and processes the required data. Here's how it works:

### **🔍 Step 1: Partitioning the Query**

- First, your query is **broken into multiple AND (`&&`) and OR (`||`) groups**.
- Any **AND group that cannot be expressed as an indexed or compound indexed query must go into the cursor**.
- If **any part of an OR group** contains a non-indexable condition, **the entire OR group must be processed by the cursor**.

### **🧠 Step 2: Collecting Only Meta-Data**

Instead of **loading full database records into memory**, Magic IndexedDB **only collects meta-data** during the cursor scan: ✅ **Primary Keys** (for fetching actual data later)  
✅ **Indexed Fields** (to preserve ordering & filtering intent)  
✅ **Fields involved in sorting or pagination**

🚨 **No full data is loaded yet!** This **minimizes memory usage** and keeps things efficient.

### **📑 Step 3: Processing the Meta-Data**

Once the **cursor has finished scanning**, the collected **meta-data** undergoes **memory-based filtering**:

- **Filters out unnecessary records immediately**
- **Applies sorting (`OrderBy`, `OrderByDescending`)**
- **Handles pagination (`Take`, `TakeLast`, `Skip`)**
- **Only retains the necessary primary keys**

At this stage, **Magic IndexedDB knows exactly what records need to be fetched**.

### **📦 Step 4: Bulk Fetching in Batches**

Once the required **primary keys** have been determined, **Magic IndexedDB sends out bulk queries in batches of 500 records per request**:

- **Avoids overwhelming IndexedDB with massive single queries**
- **Optimizes `anyOf()` performance when dealing with OR (`||`) conditions**
- **Efficiently pulls the remaining required data for final processing**

### **⏳ Step 5: Yielding Data Efficiently**

Once the **bulk queries start returning results**, **Magic IndexedDB immediately starts yielding results**:

- ✅ **No need to wait for the full query to finish**
- ✅ **Each batch is processed and returned in real-time**
- ✅ **Keeps memory footprint low by never loading unnecessary data**

---

## **🛠️ Why the Cursor is Powerful in Magic IndexedDB**

Unlike traditional IndexedDB cursors, **Magic IndexedDB transforms how cursors work**: ✔️ **Supports case-insensitive searches (e.g., `StringComparison.OrdinalIgnoreCase`)**  
✔️ **Handles unsupported IndexedDB operations (e.g., `Contains()`)**  
✔️ **Ensures that even cursor-based queries follow LINQ-style ordering & pagination rules**  
✔️ **Optimized for memory efficiency using meta-data filtering**  
✔️ **Smart batching prevents IndexedDB from slowing down under heavy OR queries**

---

## **💡 Cursor Performance: What You Need to Know**

While **Magic IndexedDB optimizes cursor queries**, **they are still slower than indexed queries**. **Your goal should always be to write queries that take full advantage of indexing** whenever possible.

### **🔹 Best Practices for Faster Queries**

✅ **Use indexed fields whenever possible**  
✅ **Leverage compound indexes for multi-condition queries**  
✅ **Avoid `Contains()` on large datasets unless necessary**  
✅ **Minimize OR (`||`) operations, as each OR condition can trigger separate queries**

> **🚀 Remember:** Magic IndexedDB **gives you maximum flexibility**, but **indexed queries are always faster than cursor queries**. The more you optimize your query structure, the **faster your queries will run**.

---

## Cursor Undefined Columns Danger

A major philosophy of this project is to never fail client side. Because failure would be catastrophic for disconnected client side storage. So, lets go over an example of a real world scenario that the cursor can handle, but how it handles it is important.

#### Example
You create a table with the columns of, "id, name, age". This is now on the table for all users. But then you add a new column in a future update that isn't indexed and is called, "dateOfBirth". If you then write a LINQ query looking for "dateOfBirth", the cursor will try to access this column that's undefined. Since part of the users database in history had only 3 columns, but now all future additions have 4 columns, this creates a disconnect where the table has a difference in columns between rows.

### Cursor Fallback
In this scenario, the cursor will recognize that it's about to access a column that's undefined. It will proceed to skip this entire row from the query entirely. If it can't fire all your desired predicate operations, then none will fire on that row. This is to prevent client side failure and retrieval of data that is unwanted.

### The Fix?
Well there isn't necessary a "fix". Instead best practices should always be at play. As the Magic IndexedDB migration system is rolling out, this will help handle and prevent these scenarios by updating old records on your behalf to defaults during the migration process, making this a non issue.

But until that's fully released or if you're not utilizing Magic IndexedDB's migration system. Then you will want to be sure when updating your schema to always make sure columns are defined and set to proper defaults.

---

## **💡 Magic IndexedDB is Evolving—Help Make it Even Better!**

Magic IndexedDB **pushes IndexedDB to its absolute limits**, but **there’s always room for improvement**! Want to see **even more optimizations?** Have an idea for **new features**? **Join the project** and help make IndexedDB the **powerful database it should be!** 🚀

---

# Final Thoughts – Ordering Logic

You've nearly learned all the fundamentals of Magic IndexedDB! **But there's just one more thing, I promise (fingers crossed)**! Did you know that the **ordering isn't applied to the returned results**? Sounds crazy right?! But it makes a ton of sense, but it's an important thing to understand. 

🔗 **Learn More: [Read the 'Understanding Ordering'](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Fundamentals/P3-Understanding-Ordering.md)**.
