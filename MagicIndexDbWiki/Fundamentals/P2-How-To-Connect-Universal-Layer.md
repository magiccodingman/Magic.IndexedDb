# **Magic IndexedDB – Universal Translation Layer**

_Bringing true LINQ to IndexedDB to any framework, language, or platform!_

---

## **1. What is the Universal Translation Layer?**

Magic IndexedDB **isn’t just a Blazor library**—it’s a fully universal **LINQ to IndexedDB translation system** that any language or framework can hook into.

Instead of directly wrapping IndexedDB APIs, **Magic IndexedDB acts as a middleware layer**, translating LINQ-based queries into **optimized IndexedDB requests** using **Dexie.js** as the underlying IndexedDB wrapper.

### **Why Use Dexie.js?**

- **Mature & well-supported** library.
- **Optimized IndexedDB access**.
- **Battle-tested** by thousands of developers.
- **Prevents reinventing the wheel**—Magic IndexedDB focuses on the translation, not the direct API.

> **🚀 The Goal:** Build a universal IndexedDB querying system **any language can implement**.

---

## **2. How Queries Are Translated**

### **Supported Query Operations**

Magic IndexedDB **converts LINQ-style operations** into a structured, universal query format, optimizing IndexedDB interactions wherever possible. However, due to IndexedDB's inherent limitations, certain operations must be handled using **cursors** instead of direct indexed queries.

### **📌 Supported Query Operators**

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`==`|`EQUAL`|Exact match|✅ Yes|
|`!=`|`NOT_EQUAL`|Not equal|✅ Yes|
|`>`|`GREATER_THAN`|Greater than|✅ Yes|
|`>=`|`GREATER_THAN_OR_EQUAL`|Greater or equal|✅ Yes|
|`<`|`LESS_THAN`|Less than|✅ Yes|
|`<=`|`LESS_THAN_OR_EQUAL`|Less or equal|✅ Yes|
|`.StartsWith()`|`STARTS_WITH`|String starts with (IndexedDB supports only exact case)|✅ Yes* (unless case-insensitive)|
|`.Contains()`|`CONTAINS`|String contains|🚫 Cursor Required|
|`.In()`|`IN`|Matches any value in a list|✅ Yes|

> **🚨 Important Notes:**
> 
> - **Indexed queries are optimized**, but operations like `.Contains()` always require a **cursor scan**.
> - **`StartsWith()` is indexable in IndexedDB** if the comparison is **case-sensitive** (`caseSensitive: true` in the universal layer).
> - **If `StringComparison.OrdinalIgnoreCase` or `caseSensitive: false` is used, the query falls back to a cursor**, as IndexedDB does not support case-insensitive indexes.


---

### **📌 Query Additions (Sorting & Pagination)**

|**LINQ Operation**|**JavaScript Key**|**Description**|
|---|---|---|
|`.OrderBy()`|`ORDER_BY`|Sort ascending|
|`.OrderByDescending()`|`ORDER_BY_DESCENDING`|Sort descending|
|`.FirstOrDefaultAsync()`|`FIRST`|Get first item|
|`.LastOrDefaultAsync()`|`LAST`|Get last item|
|`.Skip(x)`|`SKIP`|Skip `x` results|
|`.Take(x)`|`TAKE`|Take `x` results|
|`.TakeLast(x)`|`TAKE_LAST`|Take last `x` results|

> **⚠ IndexedDB has a reversed order for `.Take()` and `.Skip()`!**  
> Always write **`.Take()` before `.Skip()`** for correct execution.

---

## **3. How Queries Are Structured**

To integrate **any language or framework**, you need to **convert expressions into the universal query format**.

### **Example of a Universal Query Structure**

```json
{
    "orGroups": [
        {
            "andGroups": [
                {
                    "conditions": [
                        {
                            "property": "Age",
                            "operation": "GREATER_THAN",
                            "value": 30
                        },
                        {
                            "property": "Name",
                            "operation": "STARTS_WITH",
                            "value": "J",
                            "isString": true,
                            "caseSensitive": false
                        }
                    ]
                }
            ]
        }
    ],
    "queryAdditions": [
        {
            "additionFunction": "ORDER_BY",
            "property": "Age"
        },
        {
            "additionFunction": "TAKE",
            "intValue": 10
        }
    ]
}
```

**This query is equivalent to:**

```csharp
await personQuery.Where(x => x.Age > 30 && x.Name.StartsWith("J", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(x => x.Age)
                 .Take(10)
                 .ToListAsync();
```

---

## **4. IndexedDB Store Structure (DB Schema & Configuration)**

Before executing queries, **Magic IndexedDB** must understand how your database is structured. The system defines database stores dynamically based on schemas, ensuring proper indexing, compound keys, and validation for optimized queries.

### **📌 Example of a DB Store Schema**

```json
{
  "name": "MyDatabase",
  "version": 1,
  "storeSchemas": [
    {
      "tableName": "Users",
      "primaryKeyAuto": false,
      "columnNamesInCompoundKey": ["UserId", "TenantId"],
      "uniqueIndexes": ["Email"],
      "indexes": ["FirstName", "LastName", "CreatedAt"],
      "columnNamesInCompoundIndex": [["LastName", "FirstName"]]
    },
    {
      "tableName": "Orders",
      "primaryKeyAuto": true,
      "columnNamesInCompoundKey": ["OrderId"],
      "uniqueIndexes": [],
      "indexes": ["UserId", "Status"],
      "columnNamesInCompoundIndex": [["UserId", "Status"]]
    }
  ]
}
```

### **🔍 Breakdown of Each Property**

| **Property**                                | **Type**               | **Description**                                      |
| ------------------------------------------- | ---------------------- | ---------------------------------------------------- |
| `name`                                      | `string`               | Name of the database                                 |
| `version`                                   | `number`               | Database version (used for migrations)               |
| `storeSchemas`                              | `array`                | List of table definitions                            |
| `storeSchemas[].tableName`                  | `string`               | Name of the table (IndexedDB object store)           |
| `storeSchemas[].primaryKeyAuto`             | `boolean`              | Whether the primary key auto-increments              |
| `storeSchemas[].columnNamesInCompoundKey`   | `array<string>`        | List of columns that form a **compound primary key** |
| `storeSchemas[].uniqueIndexes`              | `array<string>`        | Columns that require **unique** constraints          |
| `storeSchemas[].indexes`                    | `array<string>`        | **Indexable** columns for optimized queries          |
| `storeSchemas[].columnNamesInCompoundIndex` | `array<array<string>>` | **Compound indexes** for faster lookups              |


> **🚀 Why This Matters:**
> 
> - The **schema is critical** for ensuring that indexed queries work properly.
> - **Unique indexes prevent duplicate values** in specific fields (e.g., `Email`).
> - **Compound indexes allow multi-column optimizations** for advanced filtering.
> - If your database schema is not properly structured, queries may **default to slower cursor-based scans** instead of optimized index lookups.

---

## **5. Executing Queries**

Once **validated**, queries are executed using **Dexie.js** and Magic IndexedDB’s **optimized universal translation layer**.

### **Yield-Based Query Execution**

```js
export async function* magicQueryYield(dbName, storeName, nestedOrFilter, queryAdditions = [], forceCursor = false) {
    if (!isValidFilterObject(nestedOrFilter)) {
        throw new Error("Invalid filter object provided.");
    }
    if (!isValidQueryAdditions(queryAdditions)) {
        throw new Error("Invalid query additions.");
    }
    
    // Execute Dexie.js query
}
```

> **🔹 Queries use `yield` to return results as they arrive.**  
> **🔹 A non-yield version (`magicQueryAsync`) is also available.**

---

## **6. Integrating Your Own Framework**

To **integrate a new language or framework**, follow these steps:

### **Step 1: Convert Expressions into Universal Query Format**

- Parse your language’s LINQ or query expressions.
- Map them to `QUERY_OPERATIONS` and `QUERY_ADDITIONS`.

### **Step 2: Send Queries to the Universal Layer**

Use `magicQueryAsync()` or `magicQueryYield()` to execute queries.

### **Step 3: Handle Results**

- Use **async iteration (`for await`)** for streaming data.
- Use **bulk fetching (`magicQueryAsync()`)** for standard queries.

---

## **7. Database & Table Handling**

Magic IndexedDB manages IndexedDB **using Dexie.js**, allowing seamless **database creation, versioning, and migrations**.

### **Example: Creating a Database**

```js
export function createDb(dbStore) {
    const db = new Dexie(dbStore.name);
    const stores = dbStore.storeSchemas.reduce((acc, schema) => {
        acc[schema.tableName] = `[${schema.columnNamesInCompoundKey.join("+")}]`;
        return acc;
    }, {});
    
    db.version(dbStore.version).stores(stores);
}
```

> **💡 The system automatically detects and handles compound keys!**

---

## **8. Contributing to the Universal Layer**

If you're building a wrapper for a new framework:

1. **Fork the repository.**
2. **Submit a PR** with your integration.
3. **Join the community!**

> **🔥 The dream?** A **universal** LINQ to IndexedDB library for **every programming language!**

Lets make LINQ to IndexedDB a thing for all languages! Lets make a better internet together.

---

## **9. Summary**

✅ **Magic IndexedDB is a universal LINQ to IndexedDB system**.  
✅ **Supports multiple languages & frameworks** through its **universal query format**.  
✅ **Validation & translation ensure queries are safe & optimized**.  
✅ **Dexie.js powers efficient IndexedDB interactions**.  
✅ **Integrate your framework with a simple expression parser!**

**🚀 Ready to contribute?** **Join us and help build the future of IndexedDB!**  
📖 **[GitHub Repo](https://github.com/magiccodingman/Magic.IndexedDb)**
