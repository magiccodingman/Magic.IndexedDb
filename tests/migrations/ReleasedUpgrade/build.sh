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

export NUGET_PACKAGES="$out/nuget"
dotnet pack "$root/Magic.IndexedDb/Magic.IndexedDb.csproj" -c Release -o "$out/feed" -p:GeneratePackageOnBuild=false
dotnet pack "$root/Magic.IndexedDb.Cli/Magic.IndexedDb.Cli.csproj" -c Release -o "$out/feed" -p:GeneratePackageOnBuild=false
dotnet tool install Magic.IndexedDb.Cli --version 3.0.0-preview.1 --tool-path "$out/tools" \
    --source "$out/feed"
cli="$out/tools/magicdb"

make_fixture() {
    mkdir -p "$1"
    cp -R "$fixture/App" "$fixture/Exporter" "$fixture/Model.cs" "$1/"
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
    cat > "$path/Directory.Build.props" <<PROPS
<Project><PropertyGroup><DefineConstants>$definitions</DefineConstants></PropertyGroup></Project>
PROPS
    (
        cd "$path"
        "$cli" schema export --project Exporter/Exporter.csproj --output legacy.json
        for schema in west east; do
            "$cli" migrations add AdoptReleased --database "$schema" --project Exporter/Exporter.csproj
            "$cli" migrations baseline --database "$schema" --legacy-schema legacy.json --revision 1 \
                --id released-v2 --acknowledge-data-contract
            "$cli" migrations validate --database "$schema" --project Exporter/Exporter.csproj
            "$cli" migrations publish --database "$schema" --project Exporter/Exporter.csproj --output App/wwwroot/db/use
        done
    )
    dotnet publish "$path/App/App.csproj" -c Release -o "$out/published/$contract" \
        -p:MagicPackage=3.0.0-preview.1 -p:RunAOTCompilation="${MAGIC_UPGRADE_AOT:-false}"
done

printf '%s\n' "${versions[@]}" > "$out/historical-versions.txt"
