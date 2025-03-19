# **Setting Up Magic IndexedDB in Blazor**

## **1. Install the NuGet Package**

The first step is to install the **latest version** of **Magic IndexedDB** from NuGet:  
🔗 **[Magic.IndexedDb on NuGet](https://www.nuget.org/packages/Magic.IndexedDb/)**

Before updating, it's **highly recommended** to review the latest **release notes** to check for any important changes or enhancements:  
🔗 **[Release Notes & Updates](https://github.com/magiccodingman/Magic.IndexedDb/releases)**

---

## **2. Register the Magic IndexedDB Service**

Once installed, you must register **Magic IndexedDB** in your Blazor application's dependency injection container. Add the following to your **Program.cs** file:

### **🚀 Default Safe Message Limits**

```csharp
// Default safe message limit for WASM applications
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, builder.HostEnvironment.IsDevelopment());

// Default safe message limit for Blazor Hybrid applications (SignalR mode)
builder.Services.AddMagicBlazorDB(BlazorInteropMode.SignalR, builder.HostEnvironment.IsDevelopment());
```

### **📏 Custom Message Limit (Advanced)**

If you need to **customize** the message size limit (in bytes), you can specify it manually:

```csharp
// Custom message limit of 35MB
long customMessageLimitBytes = 35 * 1024 * 1024;
builder.Services.AddMagicBlazorDB(customMessageLimitBytes, builder.HostEnvironment.IsDevelopment());
```

### **🔹 Understanding Interop Modes**

|**Interop Mode**|**Use Case**|
|---|---|
|`BlazorInteropMode.WASM`|Used for **standalone Blazor WebAssembly applications**|
|`BlazorInteropMode.SignalR`|Recommended for **Blazor Hybrid applications** where SignalR is used for communication|

The interop mode determines **how** the JavaScript and C# layers communicate. The **message limit** controls **how much data** can be sent between IndexedDB and C#. **A higher limit increases speed but also memory usage**, so tune it based on your needs.

---

## **3. Debug vs. Production Mode**

The second parameter in `AddMagicBlazorDB` is:

```csharp
builder.HostEnvironment.IsDevelopment()
```

This boolean **indicates whether the application is in Debug mode or not**.

### **🛠 Why This Matters?**

- When **enabled in development**, Magic IndexedDB **validates your database schema** at startup.
- It performs **system reflection-based validation** to **detect potential issues early**.
- In **production mode**, validation is **skipped** to avoid unnecessary performance overhead.
- **AOT Compatibility**: Reflection-based validation may not work in **Ahead-of-Time (AOT) compiled scenarios**. Keeping it enabled **only in development** ensures a smooth experience.

> **TL;DR:** In **development mode**, Magic IndexedDB **protects you from mistakes** by validating your setup **before you run into issues**. In **production mode**, it prioritizes **speed and efficiency**.

---

## **4. Defining Your IndexedDB Schema**

### **Understanding IndexedDB Repositories**

Unlike traditional databases, **IndexedDB operates differently**. **Think of repositories like defining tables** rather than databases.

To create a schema, **define a repository** in your Blazor project or any referenced project.

---

### **5. Creating the IndexedDB Context**

Inside your Blazor project (or a referenced project), create a new **C# file** (e.g., `IndexedDbContext.cs`) and **implement `IMagicRepository`**:

```csharp
public class IndexedDbContext : IMagicRepository
{
    public static readonly IndexedDbSet Client = new("Client");
    public static readonly IndexedDbSet Employee = new("Employee");
    public static readonly IndexedDbSet Animal = new("Animal");
}
```

### **🔍 How It Works**

- The system will **automatically detect** this repository **no matter where it is**, even if it resides in a **referenced project**. It's detected by the **`IMagicRepository`** interface itself being attached.
- Why this is exists will be shown below.

> **💡 Important:**  
> This setup **alone** is enough to define basic IndexedDB tables.  
> **For complex migration support, additional steps will be needed later**.

# **Defining Tables in Magic IndexedDB**

## **1. Understanding Tables in Magic IndexedDB**

Tables in **Magic IndexedDB** are **universally reusable** across any IndexedDB database you deploy. Defining a table is as simple as **creating a C# class** that represents your data and appending it with the appropriate **Magic IndexedDB interfaces and tools**.

Each table:

- **Defines its schema** with properties.
- **Registers its database associations**.
- **Supports compound keys, indexes, and unique constraints**.
- **Automatically migrates when you modify its structure**.

---

## **2. Creating a Table (Basic Example)**

To define a table, you must:

1. Create a **class** that represents your data.
2. Inherit from `MagicTableTool<T>`.
3. Implement `IMagicTable<TDbSets>`.
4. Define indexes, keys, and other configurations as needed.

### **📌 Example: Defining a `Person` Table**

```csharp
public class Person : MagicTableTool<Person>, IMagicTable<DbSets>
{
    public static readonly IndexedDbSet Client = IndexDbContext.Client;

    public IMagicCompoundKey GetKeys() =>
        CreatePrimaryKey(x => x.Id, true); // Auto-incrementing primary key

    public string GetTableName() => "Person";
    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;

    public DbSets Databases { get; } = new();
    public sealed class DbSets
    {
        public readonly IndexedDbSet Client = IndexDbContext.Client;
        public readonly IndexedDbSet Employee = IndexDbContext.Employee;
    }

    [MagicIndex] // Creates an index on this field
    public string Name { get; set; }

    [MagicUniqueIndex("guid")] // Unique constraint
    public Guid UniqueGuid { get; set; } = Guid.NewGuid();

    public int Age { get; set; }

    [MagicNotMapped] // Exclude from IndexedDB schema
    public string Secret { get; set; }
}
```

---

## **3. Breaking Down the Table Definition**

### **🛠 Understanding `DbSets`**

The `<TDbSets>` type parameter in `IMagicTable<TDbSets>` exists to enforce **clean C# code structure**.

- You **define `DbSets` however you like**, but it **must include**:
    
    ```csharp
    public TDbSets Databases { get; } = new();
    ```
    
- This makes the query system **strongly typed**, allowing clean LINQ queries like:
    
    ```csharp
    await _MagicDb.Query<Person>(x => x.Databases.Client);
    ```
    
    If you're unfamiliar with this pattern, **review the [Introduction Page](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Getting-Started-Blazor/P1-Introduction.md)**.

---

## **4. Defining Keys & Indexes**

### **🔑 Setting the Primary Key**

```csharp
public IMagicCompoundKey GetKeys() =>
    CreatePrimaryKey(x => x.Id, true);
```

- The **first parameter** is the **primary key property**.
- The **second parameter** (`true` or `false`) sets **auto-incrementing** behavior.

### **🗝 Defining a Compound Key**

```csharp
public IMagicCompoundKey GetKeys() =>
    CreateCompoundKey(x => x.Field1, x => x.Field2);
```

- **Compound keys combine multiple fields** as a unique key.
- **Auto-incrementing** is **not allowed** on compound keys.

---

## **5. Additional Table Configurations**

### **📌 `GetTableName()` – Table Naming**

Sets the table name in IndexedDB:

```csharp
public string GetTableName() => "Person";
```

This lets you **rename** your C# class **without breaking migrations**.

### **📌 `GetDefaultDatabase()` – Default Storage Location**

```csharp
public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
```

Tells the system which **database** this table belongs to by default.

---

## **6. Using Attributes for IndexedDB Optimization**

### **🔍 `MagicName` – Rename Columns in IndexedDB**

```csharp
[MagicName("_id")]
public int Id { get; set; }
```

- **Ensures column names stay consistent** in IndexedDB.
- **Highly recommended** to prevent migration issues when renaming C# properties.

### **📌 `MagicIndex` – Create an Indexed Column**

```csharp
[MagicIndex]
public string Name { get; set; }
```

Speeds up queries using this field.

### **📌 `MagicUniqueIndex` – Unique Constraints**

```csharp
[MagicUniqueIndex("guid")]
public Guid UniqueGuid { get; set; } = Guid.NewGuid();
```

Prevents duplicate values in this column.

### **📌 `MagicNotMapped` – Exclude Fields from IndexedDB**

```csharp
[MagicNotMapped]
public string Secret { get; set; }
```

Keeps the property **in C# but out of IndexedDB**.

---

## **7. Nested Objects in IndexedDB**

Yes, **Magic IndexedDB supports nested objects**!

```csharp
public class Address
{
    public string City { get; set; }
    public string State { get; set; }
}

public class Person : MagicTableTool<Person>, IMagicTable<DbSets>
{
    public Address HomeAddress { get; set; } = new Address();
}
```

- **Fully supported** by the custom-built serializer.
- **Validations ensure** that your schema remains stable.

---

## **8. Schema Validation & Protection**

Magic IndexedDB **validates your schema** to **prevent broken tables**: ✔ **Ensures compound keys don’t have forbidden names like `id`**.  
✔ **Warns you if you rename fields without using `[MagicName]`**.  
✔ **Protects you from invalid IndexedDB constraints**.

---

# **Razor Setup**

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
## **9. Next Steps – Handling Migrations**

Once you’ve defined your tables, the **next critical step is handling migrations**.  
Schema changes need to be **tracked and managed** so you don’t lose data.

🔥 **Learn how to handle migrations:**  
➡ **[Check out the Magic IndexedDB Migrations Guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/MagicIndexDbWiki/Getting-Started-Blazor/P3-Migrations.md)**