#!/usr/bin/env bash

set -euo pipefail

output_directory="${1:-}"
package_version="${2:-}"
project="Magic.IndexedDb/Magic.IndexedDb.csproj"

if [[ -z "${output_directory}" ]]; then
  echo "Usage: pack-nuget.sh OUTPUT_DIRECTORY [PACKAGE_VERSION]" >&2
  exit 1
fi

if [[ -z "${package_version}" ]]; then
  package_version="$(sed -nE 's:.*<Version>([^<]+)</Version>.*:\1:p' "${project}")"
fi

if [[ ! "${package_version}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  echo "Expected a major.minor.patch package version with an optional prerelease suffix, found '${package_version}'." >&2
  exit 1
fi

dotnet pack "${project}" \
  --configuration Release \
  --no-restore \
  --output "${output_directory}" \
  -p:Version="${package_version}" \
  -p:PackageVersion="${package_version}" \
  -p:ContinuousIntegrationBuild=true \
  -p:GeneratePackageOnBuild=false
