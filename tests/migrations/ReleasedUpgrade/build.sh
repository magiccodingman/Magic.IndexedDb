#!/usr/bin/env bash
set -euo pipefail

root=$(git rev-parse --show-toplevel)
fixture="$root/tests/migrations/ReleasedUpgrade"
out="${1:-$root/artifacts/released-upgrade}"
if (( $# > 0 )); then shift; fi
mkdir -p "$out"
out=$(cd "$out" && pwd)
if [[ -n "$(ls -A "$out")" ]]; then
    echo "Choose an empty output directory: $out" >&2
    exit 1
fi
mkdir -p "$out/feed"
versions=("$@")
if (( ${#versions[@]} == 0 )); then
    versions=(2.0.1 2.0.2 2.0.3 2.0.4 2.0.5 2.0.6 2.0.7)
fi

runtime_version=$(dotnet msbuild "$root/Magic.IndexedDb/Magic.IndexedDb.csproj" -nologo -getProperty:Version)
cli_version=$(dotnet msbuild "$root/Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj" -nologo -getProperty:Version)
modes=()
case "${MAGIC_UPGRADE_AOT:-false}" in
    false) modes=(trimmed) ;;
    true) modes=(aot) ;;
    both) modes=(trimmed aot) ;;
    *) echo 'MAGIC_UPGRADE_AOT must be false, true or both.' >&2; exit 1 ;;
esac

export NUGET_PACKAGES="$out/nuget"
dotnet pack "$root/Magic.IndexedDb/Magic.IndexedDb.csproj" -c Release -o "$out/feed" -p:GeneratePackageOnBuild=false
dotnet pack "$root/Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj" -c Release -o "$out/feed" -p:GeneratePackageOnBuild=false
dotnet tool install Magic.IndexedDb.Cli --version "$cli_version" --tool-path "$out/tools" \
    --source "$out/feed"
cli="$out/tools/magicdb"

make_fixture() {
    mkdir -p "$1"
    local source="${3:-$fixture}"
    cp -R "$source/App" "$source/Exporter" "$source/Model.cs" "$1/"
    if [[ "$2" == historical ]]; then
        cat > "$1/NuGet.Config" <<'CONFIG'
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
CONFIG
        return
    fi
    cat > "$1/NuGet.Config" <<CONFIG
<configuration>
  <packageSources>
    <clear />
    <add key="fixture" value="$out/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="fixture">
      <package pattern="Magic.IndexedDb" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
CONFIG
}

for version in "${versions[@]}"; do
    path="$out/legacy-$version"
    make_fixture "$path" historical
    definitions=
    if [[ "$version" == 2.0.7 ]]; then definitions=CODECS; fi
    dotnet publish "$path/App/App.csproj" -c Release -o "$out/published/$version" \
        -p:MagicPackage="$version" -p:DefineConstants="$definitions" -p:PublishTrimmed=false
done

for contract in legacy codecs; do
    path="$out/managed-$contract"
    make_fixture "$path" preview
    definitions=MANAGED
    if [[ "$contract" == codecs ]]; then definitions='MANAGED;CODECS'; fi
    write_properties() {
        cat > "$path/Directory.Build.props" <<PROPS
<Project><PropertyGroup><DefineConstants>$definitions$1</DefineConstants><MagicPackage>$runtime_version</MagicPackage></PropertyGroup></Project>
PROPS
    }

    publish_app() {
        for mode in "${modes[@]}"; do
            local aot=false
            if [[ "$mode" == aot ]]; then aot=true; fi
            # Wasm SDK intermediates include native/AOT runtime artifacts. Never
            # let an interpreted publish reuse a previous AOT configuration.
            dotnet publish "$path/App/App.csproj" -c Release -o "$out/published/$mode/$contract$1" \
                --artifacts-path "$out/app-build/$mode/$contract$1" \
                -p:RunAOTCompilation="$aot" -p:PublishTrimmed=true
        done
    }

    write_properties ''
    (
        cd "$path"
        "$cli" schema export --project Exporter/Exporter.csproj --output legacy.json
        for schema in west east; do
            "$cli" migrations add AdoptReleased --database "$schema" --project Exporter/Exporter.csproj
            "$cli" migrations baseline --database "$schema" --legacy-schema legacy.json --revision 1 \
                --id released-v2 --acknowledge-data-contract
            "$cli" migrations publish --database "$schema" --project Exporter/Exporter.csproj --output App/wwwroot/db/use
        done
    )
    publish_app ''

    write_properties ';FORWARD'
    (
        cd "$path"
        for schema in west east; do
            status=0
            "$cli" migrations add IndexMigratedInvoices --database "$schema" --project Exporter/Exporter.csproj || status=$?
            if [[ "$status" != 3 ]]; then
                echo "Expected revision 2 to require an explicit backfill decision for $schema." >&2
                exit 1
            fi
            node "$fixture/fill-forward.mjs" "$schema"
            "$cli" migrations validate --database "$schema" --project Exporter/Exporter.csproj
            "$cli" migrations publish --database "$schema" --project Exporter/Exporter.csproj --output App/wwwroot/db/forward
        done
    )
    publish_app '-forward'
done

recovery_source="$root/tests/migrations/RecoveryWasm"
recovery_app="$out/recovery-wasm"
make_fixture "$recovery_app" preview "$recovery_source"
cat > "$recovery_app/Directory.Build.props" <<PROPS
<Project><PropertyGroup><MagicPackage>$runtime_version</MagicPackage></PropertyGroup></Project>
PROPS
bash "$recovery_source/build.sh" "$recovery_app" "$out" "$cli" "${modes[@]}"

protected_source="$root/tests/migrations/ProtectedWasm"
protected_app="$out/protected-wasm"
make_fixture "$protected_app" preview "$protected_source"
cat > "$protected_app/Directory.Build.props" <<PROPS
<Project><PropertyGroup><MagicPackage>$runtime_version</MagicPackage></PropertyGroup></Project>
PROPS
bash "$protected_source/build.sh" "$protected_app" "$out" "$cli" "${modes[@]}"

printf '%s\n' "${versions[@]}" > "$out/historical-versions.txt"
node - "$out" "$(git rev-parse HEAD)" "$runtime_version" "$cli_version" "$(dotnet --version)" "${modes[@]}" <<'JS'
const [out, sourceSha, runtimeVersion, cliVersion, sdkVersion, ...modes] = process.argv.slice(2);
require('node:fs').writeFileSync(out + '/build.json', JSON.stringify({ sourceSha, runtimeVersion, cliVersion, sdkVersion, modes }, null, 2) + '\n');
JS
