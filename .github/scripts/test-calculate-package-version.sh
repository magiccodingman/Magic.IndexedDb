#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test_directory="$(mktemp -d)"
trap 'rm -rf "${test_directory}"' EXIT
test_number=0

assert_version() {
  local current_version="${1}"
  local published_versions="${2}"
  local expected_version="${3}"
  local versions_file="${test_directory}/versions-$((++test_number)).json"
  local actual_version

  printf '%s\n' "${published_versions}" > "${versions_file}"
  actual_version="$(
    bash "${script_directory}/calculate-package-version.sh" \
      "${current_version}" \
      "${versions_file}"
  )"

  if [[ "${actual_version}" != "${expected_version}" ]]; then
    echo "Expected ${expected_version}, got ${actual_version}." >&2
    exit 1
  fi
}

assert_failure() {
  local current_version="${1}"
  local published_versions="${2}"
  local versions_file="${test_directory}/versions-$((++test_number)).json"

  printf '%s\n' "${published_versions}" > "${versions_file}"
  if bash "${script_directory}/calculate-package-version.sh" \
    "${current_version}" \
    "${versions_file}" > /dev/null 2>&1; then
    echo "Expected version calculation to fail for '${current_version}'." >&2
    exit 1
  fi
}

assert_version "2.0.2" '{"versions":["1.0.12","2.0.1","2.0.2"]}' "2.0.3"
assert_version "2.0.2" '{"versions":["2.0.2","2.0.3"]}' "2.0.4"
assert_version "2.0.7" '{"versions":["2.0.3"]}' "2.0.8"
assert_version "2.1.0" '{"versions":["2.0.99","2.1.0-alpha1"]}' "2.1.1"
assert_version "2.0.2" '{"versions":["2.0.999-alpha1","3.0.0"]}' "2.0.3"
assert_failure "2.0" '{"versions":["2.0.2"]}'
assert_failure "02.0.2" '{"versions":["2.0.2"]}'
assert_failure "2.0.2" '{"unexpected":[]}'

echo "Package-version tests passed."
