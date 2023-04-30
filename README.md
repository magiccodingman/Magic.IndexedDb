# Magic.IndexedDb

This open source library provides an IndexedDb wrapper for C# and Blazor WebAssembly applications. It simplifies working with IndexedDb and makes it similar to using LINQ to SQL.

**Nuget Package Link**: https://www.nuget.org/packages/Magic.IndexedDb/1.0.0

**NOTE:**
This code is still very young. I will be making updates for this code as I come across it. I will try my best to not depreciate or break syntax already in use. But please take note of any version updates before you use my code if you're updating it in the future. As I will not guarentee right now that I won't break your stuff if you download this version and then come back in a year and get the latest version.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
  - [Setting up the project](#setting-up-the-project)
  - [Creating a class with Magic attributes](#creating-a-class-with-magic-attributes)
  - [Using the DbManager](#using-the-dbmanager)
- [Attributes](#attributes)
- [Where Method MagicQuery syntax](#Where-Method-MagicQuery-syntax)
- [Standard Operations](#standard-operations)
- [String Comparison Functions](#string-comparison-functions)
  - [Contains](#contains)
  - [StartsWith](#startswith)
  - [Equals](#equals)
- [Case Insensitive String Comparison](#case-insensitive-string-comparison)
- [Examples](#examples)
- [Acknowledgements](#acknowledgements)

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
<script src="_content/Magic.IndexedDb/magicDB.js"></script>
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
I highly suggest you always add the string parameters as that sets the IndexedDb column name. By default it'll use the C# class property name. But You should always have some very unique and static set string for the attribute. That way if/when you change the c# property names, class names, or anything, the schema will not care because the code has smart logic to differentiate the C# class property names and the IndexedDb column names that was set in your attribute. This way you can freely change any C# class properties without ever caring about needing to create migration code. You can additionally add or remove columns freely without issue.

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


Here's a grid-style documentation for the methods:

| Method | Description |
| ------ | ----------- |
| `AddRange<T>(IEnumerable<T> records)` | Bulk add records to a table. |
| `ClearTableAsync(string storeName)` | Clear all records in a table. |
| `DeleteDbAsync(string dbName)` | Delete a database by name. |
| `Add<T>(T record, Action<BlazorDbEvent> action)` | Add a single record to a table. |
| `AddRange<T>(IEnumerable<T> records, Action<BlazorDbEvent> action)` | Bulk add records to a table with an action callback. |
| `ClearTable(string storeName, Action<BlazorDbEvent> action)` | Clear all records in a table with an action callback. |
| `ClearTable<T>(Action<BlazorDbEvent> action)` | Clear all records in a table of type `T` with an action callback. |
| `Delete<T>(T item, Action<BlazorDbEvent> action)` | Delete a record of type `T` with an action callback. |
| `DeleteDb(string dbName, Action<BlazorDbEvent> action)` | Delete a database by name with an action callback. |
| `OpenDb(Action<BlazorDbEvent> action)` | Open a database with an action callback. |
| `Update<T>(T item, Action<BlazorDbEvent> action)` | Update a record of type `T` with an action callback. |
| `UpdateRange<T>(IEnumerable<T> items, Action<BlazorDbEvent> action)` | Bulk update records of type `T` with an action callback. |
| `GetAll<T>()` | Get all records of type `T` from a table. |
| `DeleteRange<TResult>(IEnumerable<TResult> items)` | Bulk delete records of type `TResult`. |
| `Decrypt(string EncryptedValue)` | Decrypt an encrypted string. |
| `GetById<TResult>(object key)` | Get a record of type `TResult` by its primary key. |
| `Where<T>(Expression<Func<T, bool>> predicate)` | Query method to allow complex query capabilities for records of type `T`. |

# Where Query Capabilities Documentation

This documentation explains the various query capabilities available in the `Where` method. The `Where` method provides a way to filter collections or sequences based on specific conditions. It allows the use of standard operations, string comparison functions, and case insensitive string comparisons.

## Where Method MagicQuery syntax

| Method | Description |
| ------ | ----------- |
| `Take(int amount)` | Limits the number of records returned by the query to the specified amount. |
| `TakeLast(int amount)` | Returns the last specified number of records in the query. |
| `Skip(int amount)` | Skips the specified number of records in the query result. |
| `OrderBy(Expression<Func<T, object>> predicate)` | Orders the query result by the specified predicate in ascending order. |
| `OrderByDescending(Expression<Func<T, object>> predicate)` | Orders the query result by the specified predicate in descending order. |
| `Execute()` | Executes the MagicQuery and returns the results as an `IEnumerable<T>`. |

These MagicQuery methods allow you to build complex queries similar to standard LINQ in C#. Remember to call the `Execute` method at the end of your MagicQuery to execute the query and retrieve the results.

## Standard Operations

The `Where` method supports the following standard operations when defining a predicate:

| Operation | Description                                         |
|-----------|-----------------------------------------------------|
| ==        | Equal to                                            |
| !=        | Not equal to                                        |
| >         | Greater than                                        |
| >=        | Greater than or equal to                            |
| <         | Less than                                           |
| <=        | Less than or equal to                               |

Example usage:

```csharp
var evenNumbers = numbers.Where(n => n > 3);
```

## String Comparison Functions

The `Where` method also supports the following string comparison functions:

### Contains

Filters the sequence based on whether a specified substring is present in the element.

Example usage:

```csharp
var filteredStrings = strings.Where(s => s.Contains("example"));
```

### StartsWith

Filters the sequence based on whether the element starts with a specified substring.

Example usage:

```csharp
var filteredStrings = strings.Where(s => s.StartsWith("prefix"));
```

### Equals

Filters the sequence based on whether the element is equal to a specified string.

Example usage:

```csharp
var filteredStrings = strings.Where(s => s.Equals("exactString"));
```

## Case Insensitive String Comparison

To perform case insensitive string comparisons, use the `StringComparison.OrdinalIgnoreCase` option. This can be applied to the `Contains`, `StartsWith`, and `Equals` functions.

Example usage:

```csharp
var filteredStrings = strings.Where(s => s.StartsWith("prefix", StringComparison.OrdinalIgnoreCase));
```

## Examples

To start using MagicIndexedDb, you need to create a `DbManager` instance for your specific database.

```csharp
private List<Person> allPeople { get; set; } = new List<Person>();
private IEnumerable<Person> WhereExample { get; set; } = Enumerable.Empty<Person>();

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        try
        {
            var manager = await _MagicDb.GetDbManager(DbNames.Client);

            await manager.ClearTable<Person>();

                    var AllThePeeps = await manager.GetAll<Person>();
                    if (AllThePeeps.Count() < 1)
                    {
                        Person[] persons = new Person[] {
                    new Person { Name = "Zack", TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), Secret = "I buried treasure behind my house"},
                    new Person { Name = "Luna", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "Jerry is my husband and I had an affair with Bob."},
                    new Person { Name = "Jerry", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "My wife is amazing"},
                    new Person { Name = "Jon", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I black mail Luna for money because I know her secret"},
                    new Person { Name = "Jack", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I have a drug problem"},
                    new Person { Name = "Cathy", TestInt = 9, _Age = 22, GUIY = Guid.NewGuid(), Secret = "I got away with reading Bobs diary."},
                    new Person { Name = "Bob", TestInt = 3 , _Age = 69, GUIY = Guid.NewGuid(), Secret = "I caught Cathy reading my diary, but I'm too shy to confront her." },
                    new Person { Name = "Alex", TestInt = 3 , _Age = 80, GUIY = Guid.NewGuid(), Secret = "I'm naked! But nobody can know!" }
                    };

                        await manager.AddRange(persons);
                    }

                    var allPeopleDecrypted = await manager.GetAll<Person>();

                    foreach (Person person in allPeopleDecrypted)
                    {
                        person.SecretDecrypted = await manager.Decrypt(person.Secret);
                        allPeople.Add(person);
                    }

                    WhereExample = await manager.Where<Person>(x => x.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase)
                    || x.Name.StartsWith("l", StringComparison.OrdinalIgnoreCase)
                    || x.Name.StartsWith("j", StringComparison.OrdinalIgnoreCase) && x._Age > 35
                    ).OrderBy(x => x._Id).Skip(1).Execute();

                    StateHasChanged();
        }
        catch (Exception ex)
        {
            // Handle exception
        }
    }
}
```

## Adding Records

To add new records, use the `AddRange` method on the `DbManager` instance. This example adds new `Person` records to the database:

```csharp
if (AllThePeeps.Count() < 1)
{
    Person[] persons = new Person[] {
        // ... (list of Person objects)
    };

    await manager.AddRange(persons);
}
```

## Retrieving Records

To retrieve all records of a specific type, use the `GetAll` method on the `DbManager` instance. This example retrieves all `Person` records:

```csharp
var allPeople = await manager.GetAll<Person>();
```

## Decrypting Records

To decrypt a specific property of a record, use the `Decrypt` method on the `DbManager` instance. This example decrypts the `Secret` property of each `Person`:

```csharp
foreach (Person person in allPeople)
{
    person.SecretDecrypted = await manager.Decrypt(person.Secret);
}
```

## Querying Records

To query records based on specific conditions, use the `Where` method on the `DbManager` instance. You can chain additional LINQ methods, such as `OrderBy`, `Skip`, and `Execute`, to further refine your query. This example retrieves `Person` records that match certain criteria:

```csharp
var whereExample = await manager.Where<Person>(x => x.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase)
    || x.Name.StartsWith("l", StringComparison.OrdinalIgnoreCase)
    || x.Name.StartsWith("j", StringComparison.OrdinalIgnoreCase) && x._Age > 35
).OrderBy(x => x._Id).Skip(1).Execute();
```

In this example, the query returns `Person` records where the `Name` property starts with "c", "l", or "j" (case-insensitive), and the `_Age` property is greater than 35. The results are ordered by the `_Id` property and the first record is skipped.

These examples demonstrate the basics of using MagicIndexedDb in a Blazor WebAssembly application. You can also perform other operations, such as updating and deleting records, by using the appropriate methods provided by the `DbManager`.


## Acknowledgements

Thank you to nwestfall for his BlazorDB work, which was a fork of Blazor.IndexedDB.Framework by Reshiru. My branch was a direct fork off of the BlazorDB work as I liked the direction.
https://github.com/nwestfall/BlazorDB

Thank you to Reshiru for his Blazor.IndexedDb.Framework code as I took a great deal of inspiration from this as well.
https://github.com/Reshiru/Blazor.IndexedDB.Framework

Both projects accomplished or mostly accomplished what they were trying to do. My goal was to create a system I personally believed was easier and more fluid for developers. I'm a big fan of using Respositories with a ton of smart logic to make it so I have to write as little code as possible. In a way, Magic.IndexedDb is accomplishing what I wished I could do with IndexedDb. I also wanted a wrapper in which could easily be updated by updating the Dexie.Js file to the newer versions. This way a larger and more specialized community with IndexedDb can handle the heavy lifting. What I wanted was to use IndexedDb like I use my every day LINQ to SQL while not worrying about needing to update with browser compatabilities. I do believe I've mostly accomplished this. There's still more that I plan to add, but I do believe that the bulk of what I wanted from this project has been completed.
