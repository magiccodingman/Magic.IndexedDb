#!/usr/bin/env bash
set -euo pipefail

app="$1"
output="$2"
cli="$3"
shift 3
source=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
(
    cd "$app"
    project=Exporter/Exporter.csproj
    "$cli" migrations add Initial --project "$project"
    draft() {
        local status=0
        "$cli" migrations add "$1" --project "$project" --empty "${@:2}" || status=$?
        if [[ "$status" != 3 ]]; then
            echo "Expected a review-required migration draft." >&2
            exit 1
        fi
    }
    draft BrokenTax --mode resumable
    node "$source/author.mjs" 2
    "$cli" migrations publish --project "$project" --output App/wwwroot/releases/original
    "$cli" migrations recovery-add --database recovery-wasm --revision 1 --id fixture-repair --module App/wwwroot/repair.mjs
    draft Normalize
    node "$source/author.mjs" 3
    "$cli" migrations validate --project "$project"
    "$cli" migrations publish --project "$project" --output App/wwwroot/releases/final
)
for mode in "$@"; do
    aot=false
    if [[ "$mode" == aot ]]; then aot=true; fi
    dotnet publish "$app/App/App.csproj" -c Release -o "$output/published/$mode/recovery" \
        --artifacts-path "$output/app-build/$mode/recovery" \
        -p:RunAOTCompilation="$aot" -p:PublishTrimmed=true
done
