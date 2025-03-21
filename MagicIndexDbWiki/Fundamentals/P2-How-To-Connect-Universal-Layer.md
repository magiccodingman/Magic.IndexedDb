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


Absolutely — here's the cleaned-up, fully updated, and **well-organized** version of your **📌 Supported Query Operators** table, broken into clear sections (comparison, string, array, date, type, null) for readability:

---

### 📌 **Supported Query Operators**

#### 🧮 Basic Comparison

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`==`|`Equal`|Exact match|✅ Yes|
|`!=`|`NotEqual`|Not equal|✅ Yes|
|`>`|`GreaterThan`|Greater than|✅ Yes|
|`>=`|`GreaterThanOrEqual`|Greater than or equal|✅ Yes|
|`<`|`LessThan`|Less than|✅ Yes|
|`<=`|`LessThanOrEqual`|Less than or equal|✅ Yes|

---

#### 🔤 String Matching

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`.startsWith()`|`StartsWith`|String starts with|✅ Yes* (case-insensitive = 🚫)|
|`!x.startsWith()`|`NotStartsWith`|String does **not** start with|🚫 Cursor Required|
|`.endsWith()`|`EndsWith`|String ends with|🚫 Cursor Required|
|`!x.endsWith()`|`NotEndsWith`|String does **not** end with|🚫 Cursor Required|
|`.contains()`|`Contains`|String/array contains value OR value in array|🚫 Cursor Required|
|`!x.contains()`|`NotContains`|String/array does **not** contain value OR not in array|🚫 Cursor Required|


> **🚨 Important Notes:**
> 
> - **Indexed queries are optimized**, but operations like `.Contains()` always require a **cursor scan**.
> - **`StartsWith()` is indexable in IndexedDB** if the comparison is **case-sensitive** (`caseSensitive: true` in the universal layer).
> - **If `StringComparison.OrdinalIgnoreCase` or `caseSensitive: false` is used, the query falls back to a cursor**, as IndexedDB does not support case-insensitive indexes.
> - **This may not be everything!** Please validate connections on, "QUERY_OPERATIONS" within the queryConstants.js


---

#### 📚 Array & Length Operations

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`.In([a, b, c])`|`In`|Matches any value in array|✅ Yes|
|`.length == X`|`LengthEqual`|Length of string/array equals X|🚫 Cursor Required|
|`.length != X`|`NotLengthEqual`|Length of string/array **not** equal to X|🚫 Cursor Required|
|`.length > X`|`LengthGreaterThan`|Length greater than X|🚫 Cursor Required|
|`.length >= X`|`LengthGreaterThanOrEqual`|Length greater than or equal to X|🚫 Cursor Required|
|`.length < X`|`LengthLessThan`|Length less than X|🚫 Cursor Required|
|`.length <= X`|`LengthLessThanOrEqual`|Length less than or equal to X|🚫 Cursor Required|

---

#### 📅 Date Operations

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`x.Day == X`|`GetDay`|Day of the month (1-31)|🚫 Cursor Required|
|`x.DayOfWeek == X`|`GetDayOfWeek`|Day of week (Sunday = 0, Saturday = 6)|🚫 Cursor Required|
|`x.DayOfYear == X`|`GetDayOfYear`|Day of year (1-366)|🚫 Cursor Required|

---

#### 🧪 Type Checks

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`typeof x === "number"`|`TypeOfNumber`|Value is a number|🚫 Cursor Required|
|`typeof x === "string"`|`TypeOfString`|Value is a string|🚫 Cursor Required|
|`x instanceof Date`|`TypeOfDate`|Value is a valid Date|🚫 Cursor Required|
|`Array.isArray(x)`|`TypeOfArray`|Value is an array|🚫 Cursor Required|
|`typeof x === "object"`|`TypeOfObject`|Value is a plain object|🚫 Cursor Required|
|`x instanceof Blob`|`TypeOfBlob`|Value is a Blob|🚫 Cursor Required|
|`x instanceof ArrayBuffer`|`TypeOfArrayBuffer`|Value is an ArrayBuffer or typed array|🚫 Cursor Required|
|`x instanceof File`|`TypeOfFile`|Value is a File|🚫 Cursor Required|
|`!(typeof x === "number")`|`NotTypeOfNumber`|Value is **not** a number|🚫 Cursor Required|
|`!(typeof x === "string")`|`NotTypeOfString`|Value is **not** a string|🚫 Cursor Required|
|`!(x instanceof Date)`|`NotTypeOfDate`|Value is **not** a valid Date|🚫 Cursor Required|
|`!Array.isArray(x)`|`NotTypeOfArray`|Value is **not** an array|🚫 Cursor Required|
|`!(typeof x === "object")`|`NotTypeOfObject`|Value is **not** a plain object|🚫 Cursor Required|
|`!(x instanceof Blob)`|`NotTypeOfBlob`|Value is **not** a Blob|🚫 Cursor Required|
|`!(x instanceof ArrayBuffer)`|`NotTypeOfArrayBuffer`|Value is **not** an ArrayBuffer or typed array|🚫 Cursor Required|
|`!(x instanceof File)`|`NotTypeOfFile`|Value is **not** a File|🚫 Cursor Required|

---

#### 🚫 Null Checks

|**Operator**|**JavaScript Key**|**Description**|**IndexedDB Optimized?**|
|---|---|---|---|
|`x == null`|`IsNull`|Value is `null` or `undefined`|🚫 Cursor Required|
|`x != null`|`IsNotNull`|Value is **not** `null` or `undefined`|🚫 Cursor Required|

---

### **📌 Query Additions (Sorting & Pagination)**

| **LINQ Operation**       | **JavaScript Key**  | **Description**       |
| ------------------------ | ------------------- | --------------------- |
| `.OrderBy()`             | `orderBy`           | Sort ascending        |
| `.OrderByDescending()`   | `orderByDescending` | Sort descending       |
| `.FirstOrDefaultAsync()` | `first`             | Get first item        |
| `.LastOrDefaultAsync()`  | `last`              | Get last item         |
| `.Skip(x)`               | `skip`              | Skip `x` results      |
| `.Take(x)`               | `take`              | Take `x` results      |
| `.TakeLast(x)`           | `takeLast`          | Take last `x` results |

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
                            "operation": "GreaterThan",
                            "value": 30
                        },
                        {
                            "property": "Name",
                            "operation": "StartsWith",
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
            "additionFunction": "orderBy",
            "property": "Age"
        },
        {
            "additionFunction": "take",
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

#### IMPORTANT NOTE
This section for the structure of the DB Schema is highly likely to change until the migration code is completely released. As the future migration implementation will require adjustments here.

---

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

This process is still under development.

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
