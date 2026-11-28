#!/usr/bin/env bash
set -euo pipefail

app="$1"
output="$2"
cli="$3"
shift 3
fixture_source=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
(
    cd "$app"
    "$cli" schema export --project Exporter/Exporter.csproj --output target.json
    node "$fixture_source/author.mjs" "$cli" target.json
)
for mode in "$@"; do
    aot=false
    if [[ "$mode" == aot ]]; then aot=true; fi
    dotnet publish "$app/App/App.csproj" -c Release -o "$output/published/$mode/protected" \
        --artifacts-path "$output/app-build/$mode/protected" \
        -p:RunAOTCompilation="$aot" -p:PublishTrimmed=true -p:TrimMode=full
done
