#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
compare_script="$repo_root/tools/compare-jint-profiles"

if [[ ! -x "$compare_script" ]]; then
  echo "Missing executable: $compare_script" >&2
  echo "Expected the ProfileRunner/Jint comparison script at tools/compare-jint-profiles." >&2
  exit 1
fi

exec "$compare_script" "$@"
