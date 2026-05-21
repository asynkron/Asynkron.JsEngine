#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
master_list="$repo_root/tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt"
pack_dir="$repo_root/tests/Asynkron.JsEngine.Tests.Test262/regression-packs"
pack_name="${1:-full}"
available_packs="full $(cd "$pack_dir" && printf '%s ' *.filter.txt | sed 's/\.filter\.txt//g')"

case "$pack_name" in
  --list|-l|list)
    echo "$available_packs"
    exit 0
    ;;
  full)
    list_file="$master_list"
    ;;
  */*|*.txt)
    list_file="$pack_name"
    ;;
  *)
    list_file="$pack_dir/$pack_name.filter.txt"
    ;;
esac

if [[ ! -f "$list_file" ]]; then
  echo "Missing regression list: $list_file" >&2
  echo "Available packs: $available_packs" >&2
  exit 1
fi

filters=()
while IFS= read -r filter; do
  filters+=("$filter")
done < <(grep -Ev '^[[:space:]]*(#|$)' "$list_file")

if [[ ${#filters[@]} -eq 0 ]]; then
  echo "Regression list is empty: $list_file" >&2
  exit 1
fi

filter_expr=""
for filter in "${filters[@]}"; do
  if [[ -z "$filter_expr" ]]; then
    filter_expr="$filter"
  else
    filter_expr+="|$filter"
  fi
done

echo "$filter_expr"
cd "$repo_root"
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "$filter_expr"
