# Universal predicate language

Magic IndexedDB converts C# expressions into a small predicate tree before sending them to the browser. This keeps C# expression parsing separate from IndexedDB query planning and gives other language wrappers a format they could target later.

## Who this page is for

This page is for contributors working on expression translation, query planning, or another language wrapper. Blazor applications should use `IMagicIndexedDb`; the JavaScript modules and transport format are not a public JavaScript SDK.

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
            "caseSensitive": true
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
            "caseSensitive": true
          }
        }
      ],
      "condition": null
    }
  ],
  "condition": null
}
```

The property names above assume camel-case storage and no `[MagicName]` override. The tree must always use the names stored in IndexedDB, which may differ from the C# member names.

Ordinary C# string equality is case-sensitive, so the string equality conditions above carry `caseSensitive: true`. Case-insensitive supported method overloads carry `false` and normally require cursor evaluation.

Constant predicates use a condition whose property is `__constant` and whose `Equal` value is `true` or `false`. The planner treats this as boolean truth, not as a real stored property. Empty captured membership and empty `Any`/`All` inputs must retain their language semantics rather than producing malformed empty logical groups.

## Operations

The operation vocabulary includes:

- Comparisons: `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`
- Membership and text: `In`, `StartsWith`, `NotStartsWith`, `Contains`, `NotContains`, `EndsWith`, `NotEndsWith`
- Nulls: `IsNull`, `IsNotNull`
- Length comparisons: `LengthEqual`, `NotLengthEqual`, and the range variants
- Date components: the `Year*`, `Month*`, `Day*`, `DayOfWeek*`, and `DayOfYear*` families
- Type operations used by the cursor evaluator

The source of truth is [`queryConstants.js`](../../Magic.IndexedDb/wwwroot/utilities/queryConstants.js), together with the C# translator and cursor evaluator. An operation existing in the vocabulary does not mean it can use an IndexedDB index.

Operation names must match exactly. The names are `Equal`, `NotEqual`, and `Contains`, not variants such as `StringEquals`, `NotEquals`, or `ArrayContains`. A captured membership expression may begin as several `Equal` alternatives and later be compressed to `In`. The supported `record.Values.Contains(3)` form uses `Contains` and runs through the cursor evaluator.

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
| table `version` | Reserved table-schema version field; generated schemas currently leave it at `0` |
| `primaryKeyAuto` | Whether the single primary key auto-increments |
| `uniqueIndexes` | Unique index property names |
| `indexes` | Ordinary index property names |
| `columnNamesInCompoundIndex` | Ordered property lists for compound indexes |
| `columnNamesInCompoundKey` | Ordered primary-key property list |

The migration fields shown in the source are not a usable automatic migration feature yet.

## Internal browser execution

The package has a materialized path (`magicQueryAsync`) and a generator path (`magicQueryYield`). Both receive the table, predicate tree, query additions, and a forced-cursor flag. Blazor calls them through its streaming transport; applications do not call these functions directly.

## What another language wrapper would need

Producing the JSON shape is only one part of a wrapper. A wrapper would also need to:

1. Translate its language's expressions without changing logical meaning.
2. Apply persisted-name mappings consistently to schemas, predicates, and returned objects.
3. Validate supported operations and normalize values such as dates and enums.
4. Preserve AND/OR nesting and constant true/false predicates.
5. Enforce the same valid order for query additions.
6. Implement the versioned interop and streaming lifecycle, including cancellation and errors.
7. Be updated when the internal JavaScript transport changes, until a public wrapper API exists.

Because the JavaScript entry points are still internal, a new wrapper should define and test a public boundary instead of depending directly on one module function.
