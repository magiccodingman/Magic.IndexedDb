# Magic.IndexedDb.Cli

`magicdb` is the separately packaged .NET 10 tool for Magic IndexedDB 3.0.
It exports models, scaffolds reviewed migration history, validates it, and publishes
static browser assets. It never connects to or rewrites customers' browser data.

```sh
dotnet new tool-manifest
dotnet tool install --local Magic.IndexedDb.Cli --version 3.0.0-preview.1
dotnet tool run magicdb -- --help
```

Commit the local tool manifest so development and CI use the same version.
Use `--schema model.json` for a language-neutral model, or `--project` for an explicit
runnable design-time schema exporter. A Blazor application's startup is not executed.

```sh
dotnet tool run magicdb -- migrations add Initial --schema model.json
dotnet tool run magicdb -- migrations validate --schema model.json
dotnet tool run magicdb -- migrations publish --schema model.json --output ./release-assets
```

Migration generation can produce unresolved `manual` operations. That creates a
reviewable draft and exits 3. Validation and publishing reject it until the author
supplies the transformation. Destructive operations require `allowDataLoss:true`.

Protection changes scaffold `reprotect` operations for authenticated, resumable
rewrites. Both adding and removing encryption are supported authoring paths;
exposing previously protected values requires an explicit `allowProtectionLoss:true`
decision in the migration. The runtime receives authorization from the consuming
application. Keys are never CLI inputs or static migration assets. Protected
histories retain their authentication capability even if their latest model has
only public fields.

Published JSON and modules are sealed by `published.lock.json`; commit it with the
history. A released history cannot be silently edited, truncated, or overwritten.
Use immutable deployment directories and pin the manifest to the running app release.

See the [CLI guide](https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/guides/migration-cli.md)
for exporter setup, commands, adoption, automated tests, and diagnostics. This preview
is intended for validation before production use; the release acceptance checklist
and supported operations are documented in the repository.
