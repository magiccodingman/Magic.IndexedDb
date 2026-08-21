# Universal predicate language

Magic IndexedDB separates language-specific expressions from its browser query planner with a universal predicate tree. The Blazor wrapper currently builds this tree from C# expression trees; the architecture is intended to allow future wrappers to produce the same intent from other languages.

## Stability notice

The supported public integration surface in version 3 is the C# `IMagicIndexedDb` API. The JavaScript modules, transport envelope, and direct query functions are package implementation details and may evolve. Treat this page as contributor architecture documentation, not as a promise that external JavaScript can call an unversioned public SDK.

## Node model

Conceptually, a predicate is either a logical node or a condition node:

```text
PredicateNode = LogicalNode | ConditionNode

LogicalNode:
  nodeType: Logical
  operator: And | Or
  children: PredicateNode[]

ConditionNode:
  nodeType: Condition
  condition:
    property: string
    operation: string
    value: any
    isString: boolean
    caseSensitive: boolean
```

The C# enum values serialized by the current wrapper are:

| Field | Value | Meaning |
|---|---:|---|
| `nodeType` | `0` | Logical |
| `nodeType` | `1` | Condition |
| `operator` | `0` | And |
| `operator` | `1` | Or |

The JavaScript normalizer also understands the PascalCase strings `Logical`, `Condition`, `And`, and `Or` internally.

## Example

This C# predicate:

```csharp
person => person.Age > 30 &&
          (person.City == "New York" ||
           person.City == "San Francisco")
```

is represented by the current C# transport in this shape:

```json
{
  "nodeType": 0,
  "operator": 0,
  "children": [
    {
      "nodeType": 1,
      "operator": 0,
      "children": null,
      "condition": {
        "property": "age",
        "operation": "GreaterThan",
        "value": 30,
        "isString": false,
        "caseSensitive": false
      }
    },
    {
      "nodeType": 0,
      "operator": 1,
      "children": [
        {
          "nodeType": 1,
          "operator": 0,
          "children": null,
          "condition": {
            "property": "city",
            "operation": "Equal",
            "value": "New York",
            "isString": true,
            "caseSensitive": false
          }
        },
        {
          "nodeType": 1,
          "operator": 0,
          "children": null,
          "condition": {
            "property": "city",
            "operation": "Equal",
            "value": "San Francisco",
            "isString": true,
            "caseSensitive": false
          }
        }
      ],
      "condition": null
    }
  ],
  "condition": null
}
```

The stored property names shown above assume camel-case transport and no overriding `[MagicName]`. A wrapper must use the actual persisted schema names, not merely its language's source-member names.

## Operations

The operation vocabulary includes:

- Comparisons: `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`
- Membership and text: `In`, `StartsWith`, `NotStartsWith`, `Contains`, `NotContains`, `EndsWith`, `NotEndsWith`
- Nulls: `IsNull`, `IsNotNull`
- Length comparisons: `LengthEqual`, `NotLengthEqual`, and the range variants
- Date components: the `Year*`, `Month*`, `Day*`, `DayOfWeek*`, and `DayOfYear*` families
- Type operations used by the cursor evaluator

The source of truth is [`queryConstants.js`](../../Magic.IndexedDb/wwwroot/utilities/queryConstants.js), together with the C# translator and cursor evaluator. An operation existing in the vocabulary does not mean it can use an IndexedDB index.

## Query additions

Additions are separate from the predicate tree:

```json
[
  { "additionFunction": "orderBy", "intValue": 0, "property": "age" },
  { "additionFunction": "take", "intValue": 20, "property": null },
  { "additionFunction": "skip", "intValue": 40, "property": null }
]
```

Supported addition keys are `orderBy`, `orderByDescending`, `first`, `last`, `take`, `skip`, `takeLast`, and `stableOrdering`.

## Store schema

The browser planner also needs a store definition containing the database name/version and each table's primary key, ordinary indexes, unique indexes, and compound indexes. The C# wrapper derives this from `IMagicRepository`, `IMagicTable<TDbSets>`, table helper methods, and schema attributes.

The current camel-case shape is:

```json
{
  "name": "Client",
  "version": 1,
  "storeSchemas": [
    {
      "tableName": "people",
      "version": 0,
      "primaryKeyAuto": true,
      "uniqueIndexes": ["externalId"],
      "indexes": ["firstName", "lastName", "age"],
      "columnNamesInCompoundIndex": [
        ["lastName", "firstName"]
      ],
      "columnNamesInCompoundKey": ["id"]
    }
  ],
  "dbMigrations": []
}
```

| Store field | Purpose |
|---|---|
| `name` | Browser database name |
| `version` | Database version passed to the store definition |
| `storeSchemas` | Object-store definitions |
| `tableName` | IndexedDB object-store name |
| table `version` | Reserved table-schema version field; generated version 3 schemas currently leave it at `0` |
| `primaryKeyAuto` | Whether the single primary key auto-increments |
| `uniqueIndexes` | Unique index property names |
| `indexes` | Ordinary index property names |
| `columnNamesInCompoundIndex` | Ordered property lists for compound indexes |
| `columnNamesInCompoundKey` | Ordered primary-key property list |

Database migration fields currently present in the source are not a supported automatic migration protocol.

## Internal browser execution

The package currently has materialized and generator-based query paths corresponding to `magicQueryAsync` and `magicQueryYield`. Both accept the table, universal predicate, query additions, and a forced-cursor flag. The Blazor package calls them through its versioned streaming envelope; they are not exported as a separately supported JavaScript SDK.

## Building another wrapper

A future wrapper needs more than a JSON serializer. It must:

1. Translate its language's expressions without changing logical meaning.
2. Apply persisted-name mappings consistently to schemas, predicates, and returned objects.
3. Validate supported operations and normalize values such as dates and enums.
4. Preserve AND/OR nesting and constant true/false predicates.
5. Follow the query-addition ordering contract.
6. Implement the versioned interop and streaming lifecycle, including cancellation and errors.
7. Track changes to the internal JavaScript contract until a standalone public wrapper API is formally versioned.

Contributors should propose the public boundary and compatibility tests along with any new language wrapper rather than coupling directly to one internal module function.
