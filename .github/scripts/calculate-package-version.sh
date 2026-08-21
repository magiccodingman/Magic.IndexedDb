#!/usr/bin/env bash

set -euo pipefail

current_version="${1:-}"
published_versions_file="${2:-}"

if [[ ! "${current_version}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Expected a stable major.minor.patch source version, found '${current_version}'." >&2
  exit 1
fi

if [[ ! -f "${published_versions_file}" ]]; then
  echo "Published-version index '${published_versions_file}' does not exist." >&2
  exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"
source_patch="$((10#${BASH_REMATCH[3]}))"

published_patch="$(
  jq --exit-status --raw-output \
    --arg major "${major}" \
    --arg minor "${minor}" \
    '
      if (.versions | type) != "array" then
        error("Expected a versions array in the NuGet version index.")
      else
        [
          .versions[]
          | select(type == "string")
          | select(test("^[0-9]+\\.[0-9]+\\.[0-9]+$"))
          | split(".")
          | select(.[0] == $major and .[1] == $minor)
          | .[2]
          | tonumber
        ]
        | max // -1
      end
    ' \
    "${published_versions_file}"
)"

base_patch="${source_patch}"
if (( published_patch > base_patch )); then
  base_patch="${published_patch}"
fi

printf '%s.%s.%s\n' "${major}" "${minor}" "$((base_patch + 1))"
