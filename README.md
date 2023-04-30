# Magic.IndexedDb

This open source library provides an IndexedDb wrapper for C# and Blazor WebAssembly applications. It simplifies working with IndexedDb and makes it similar to using LINQ to SQL.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
  - [Setting up the project](#setting-up-the-project)
  - [Creating a class with Magic attributes](#creating-a-class-with-magic-attributes)
  - [Using the DbManager](#using-the-dbmanager)
- [Attributes](#attributes)
- [Examples](#examples)

## Installation

1. Add the library to your project.
2. Add the following code to your `Program.cs` file:

```csharp
/*
 * This is an example encryption key. You must make your own 128 bit or 256 bit 
 * key! Do not use this example encryption key that I've provided here as that's
 * incredibly unsafe!
 */
string EncryptionKey = "zQfTuWnZi8u7x!A%C*F-JaBdRlUkXp2l";

builder.Services.AddBlazorDB(options =>
{
    options.Name = DbNames.Client;
    options.Version = "1";
    options.EncryptionKey = EncryptionKey;
    options.StoreSchemas = SchemaHelper.GetAllSchemas("DatabaseName"); // builds entire database schema for you based on attributes
    options.DbMigrations = new List<DbMigration>
{
        /*
         * The DbMigration is not currently working or setup!
         * This is an example and idea I'm thinking about, but 
         * this will very likely be depreciated so do not use or rely
         * on any of this syntax right now. If you want to have 
         * your own migration knowledge. Write JavaScript on the front end 
         * that will check the indedDb version and then apply migration code 
         * on the front end if needed. But this is only needed for complex 
         * migration projects.
         */
    new DbMigration
    {
        FromVersion = "1.1",
        ToVersion = "2.2",
        Instructions = new List<DbMigrationInstruction>
        {
            new DbMigrationInstruction
            {
                Action = "renameStore",
                StoreName = "oldStore",
                Details = "newStore"
            }
        }
    }
};
});
```

3. Add the following scripts to the end of the body tag in your `Index.html`:

```html
<script src="_content/Magic.IndexedDb/dexie.min.js"></script>
<script src="_content/Magic.IndexedDb/blazorDb.js"></script>
```

4. Add the following to your _Import.razor:

```csharp
@using Magic.IndexedDb
@inject IMagicDbFactory _MagicDb
```

### Creating a class with Magic attributes

Define your class with the `MagicTable` attribute and the appropriate magic attributes for each property. For example:

```csharp
[MagicTable("Person", "DatabaseName")]
public class Person
{
    [MagicPrimaryKey("id")]
    public int _Id { get; set; }

    [MagicIndex]
    public string Name { get; set; }

    [MagicIndex("Age")]
    public int _Age { get; set; }

    [MagicEncrypt]
    public string Secret { get; set; }

    [MagicNotMapped]
    public string SecretDecrypted { get; set; }
}
```

## Attributes

- `MagicTable(string, string)`: Associates the class with a table in IndexedDb. The first parameter is the table name, and the second parameter is the database name.
- `MagicPrimaryKey(string)`: Marks the property as the primary key. The parameter is the column name in IndexedDb.
- `MagicIndex(string)`: Creates a searchable index for the property. The parameter is the column name in IndexedDb.
- `MagicUniqueIndex(string)`: Creates a unique index for the property. The parameter is the column name in IndexedDb.
- `MagicEncrypt`: Encrypts the string property when it's stored in IndexedDb.
- `MagicNotMapped`: Excludes the property from being mapped to IndexedDb.

### Using the DbManager

1. Get the `DbManager` for your database:

```csharp
var manager = await _MagicDb.GetDbManager(DbNames.Client);
```

2. Perform operations with the `DbManager`, such as adding, updating, deleting, and querying data.
