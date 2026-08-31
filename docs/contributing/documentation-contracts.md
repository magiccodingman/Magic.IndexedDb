# Maintaining the documentation contract

Magic IndexedDB documentation is both the ordinary human learning path and the canonical written description of the library. Do not maintain a second AI-specific guide. Improve the same pages developers read so examples, terminology, and behavioral contracts have one source of truth.

## Progressive disclosure

Preserve these layers:

1. `getting-started` provides the shortest successful path.
2. `guides` explains practical choices and common workflows.
3. `reference` defines exact API, data, error, and edge-case contracts.
4. `architecture` explains translation and execution internals.
5. `contributing` explains validation and maintenance.

Add a short link from an introductory page instead of inserting advanced failure or implementation detail into the first workflow. Put each precise contract on one canonical page and link to it rather than maintaining several slightly different explanations.

## Supported-contract inventory

Documentation changes must account for these consumer-facing areas:

- `AddMagicBlazorDB` registration and interop sizing
- `IMagicIndexedDb`, `IMagicQuery<T>`, `IMagicExecute<T>`, and every staged query/cursor interface
- Table CRUD, return values, keys, bulk behavior, and cancellation
- `IMagicDatabaseScoped` lifecycle and `QuotaUsage`
- `IMagicRepository`, `IMagicTableBase`, `IMagicTable<TDbSets>`, `IndexedDbSet`, and `MagicTableTool<T>`
- Schema attributes and constructor selection
- Every supported expression and query addition
- Serialization/materialization behavior
- Browser, origin, quota, multi-tab, and migration limitations

The assembly also exports helper and interop types for compatibility. Classify an exported type before documenting it as recommended application API. A public symbol is not automatically a supported high-level workflow.

## Required method documentation

For each supported operation, document whichever of these apply:

- Preconditions and valid fluent stage
- Deferred versus immediate work
- Indexed, cursor, in-memory, or lifecycle execution
- Return-value meaning
- Side effects and destructive scope
- Ordering and de-duplication behavior
- Cancellation coverage
- Transaction or atomicity guarantee
- Expected error categories
- Unsupported and path-dependent edge cases
- A compiling example

Do not infer a guarantee from a method name. For example, verify whether an update is update-only or upsert, whether a count reports affected rows or requested rows, and whether cancellation can roll back dispatched work.

## Evidence hierarchy

Use evidence in this order:

1. Public interface and explicit compatibility contract
2. Browser end-to-end regression test
3. Deterministic .NET contract test
4. C# and JavaScript implementation read together
5. Clearly labeled current limitation when no stronger contract exists

If source paths disagree, do not choose the most convenient behavior for the prose. Add a regression test or document the absence of a guarantee.

## Examples

Examples should:

- Use current namespaces and target-framework syntax
- Include enough schema context to identify indexed and key properties
- Avoid methods not available on the current staged return type
- Use deterministic values unless generation is the subject
- State whether a predicate executes in IndexedDB, a cursor, or .NET when that distinction matters
- Avoid suggesting that a successful helper-only JSON round trip proves browser persistence behavior

Compile-check new or materially changed C# examples. Run browser tests for claims involving IndexedDB, JavaScript numbers, storage, lifecycle, streaming, or query-planner behavior.

## Pull-request checklist

- Update the canonical page for every changed contract.
- Keep the getting-started path concise.
- Add new pages to `docs/README.md`.
- Check every relative Markdown link.
- Compile-check C# snippets that express a public workflow.
- Add or update a regression test for new guarantees.
- Review `PublicApiBaseline.txt` changes intentionally.
- Run `dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release`.
- Run the relevant browser suite for browser-dependent claims.
- Avoid versionless words such as “always” when the behavior is only a current implementation detail.

## Release accountability

The documentation index states the target framework and release line. Update it with a release that changes those facts. Upgrade notes should describe transition-specific behavior; current reference pages should describe only the resulting supported contract.

When a known limitation is fixed, remove obsolete warnings from every linking page and replace them with the new tested contract in the same change.
