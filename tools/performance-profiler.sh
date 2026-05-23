#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/.." && pwd)
output_root="$script_dir/profile-output"
timestamp=$(date +%Y%m%d_%H%M%S)
output_dir="$output_root/performance_$timestamp"
summary_path="$output_dir/summary.txt"

filter="${PERF_FILTER:-Asynkron.JsEngine}"
depth="${PERF_CALLTREE_DEPTH:-14}"
width="${PERF_CALLTREE_WIDTH:-8}"
sibling_cutoff="${PERF_CALLTREE_SIBLING_CUTOFF:-1}"
profiles="${PERF_PROFILES:-bytecode:EvaluateExpressionProgram forloop:ExecuteInstructionLoop functioncalls-lite:ExecuteInstructionLoop objectcreation:ExecuteInstructionLoop arrayops:ExecuteInstructionLoop}"

mkdir -p "$output_dir"

log() {
  printf '%s\n' "$*"
}

run_profile() {
  local profile=$1
  local root=$2
  local mode=$3
  local report_path="$output_dir/${profile}-${mode}.txt"

  {
    printf '=== %s %s profile ===\n' "$profile" "$mode"
    printf 'Root: %s\n' "$root"
    printf 'Filter: %s\n' "$filter"
    printf 'Report: %s\n\n' "$report_path"
  } | tee "$report_path"

  if [[ "$mode" == "cpu" ]]; then
    "$script_dir/profile" "$profile" \
      --cpu \
      --root "$root" \
      --filter "$filter" \
      --calltree-depth "$depth" \
      --calltree-width "$width" \
      --calltree-sibling-cutoff "$sibling_cutoff" | tee -a "$report_path"
  else
    "$script_dir/profile" "$profile" \
      --memory \
      --root "$root" \
      --calltree-depth "$depth" \
      --calltree-width "$width" \
      --calltree-sibling-cutoff "$sibling_cutoff" | tee -a "$report_path"
  fi
}

append_cpu_summary() {
  local profile=$1
  local report_path="$output_dir/${profile}-cpu.txt"

  {
    printf '\n## %s CPU\n' "$profile"
    awk '
      /Top Functions/ { in_table=1; next }
      in_table && /^Time \(ms\)/ { next }
      in_table && /^Filtered out/ { in_table=0 }
      in_table && NF > 0 {
        print
        count++
        if (count == 8) {
          in_table=0
        }
      }
    ' "$report_path"
  } >> "$summary_path"
}

append_memory_summary() {
  local profile=$1
  local report_path="$output_dir/${profile}-memory.txt"

  {
    printf '\n## %s Memory\n' "$profile"
    awk '
      /^Metric/ || /^Total allocated/ { print }
      /^Type[[:space:]]+Count[[:space:]]+Total/ { in_table=1; print; next }
      in_table && /^TOTAL/ { print; in_table=0 }
      in_table && NF > 0 {
        print
        count++
        if (count == 10) {
          in_table=0
        }
      }
    ' "$report_path"
  } >> "$summary_path"
}

write_static_checks() {
  local check_path="$output_dir/static-checks.txt"
  {
    printf '=== AST expression eval seams in IR runner ===\n'
    rg 'EvaluateExpression\(|ProfileEvaluateExpression\(' \
      "$repo_root/src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner"* || true
    printf '\n=== Statement runner anchors ===\n'
    rg 'ExecutePlan\(|ExecuteInstructionLoop|EvaluateExpressionProgram' \
      "$repo_root/src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner"* || true
  } > "$check_path"

  {
    printf '\n## Static Checks\n'
    printf 'Wrote %s\n' "$check_path"
  } >> "$summary_path"
}

write_header() {
  {
    printf 'Asynkron.JsEngine performance profile report\n'
    printf 'Generated: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'Output: %s\n' "$output_dir"
    printf 'Profiles: %s\n' "$profiles"
    printf 'CPU filter: %s\n' "$filter"
    printf 'Call tree: depth=%s width=%s sibling_cutoff=%s\n' "$depth" "$width" "$sibling_cutoff"
    printf '\nRead this first:\n'
    printf '%s\n' '- bytecode uses hand-built ExpressionProgram payloads and profiles the expression VM loop directly.'
    printf '%s\n' '- IR profiles use normal script profiles and root the CPU call tree at ExecuteInstructionLoop.'
    printf '%s\n' '- Memory reports are sampled allocation-by-type tables; current profiler output does not include allocation call stacks.'
    printf '%s\n' '- Full CPU call trees and complete allocation tables are in the per-profile report files next to this summary.'
  } > "$summary_path"
}

write_header
log "Performance profiler output directory: $output_dir"

for entry in $profiles; do
  profile=${entry%%:*}
  root=${entry#*:}
  if [[ -z "$profile" || -z "$root" || "$profile" == "$root" ]]; then
    printf 'Invalid PERF_PROFILES entry: %s\n' "$entry" >&2
    exit 1
  fi

  log ""
  log "Profiling $profile with root $root"
  run_profile "$profile" "$root" cpu
  run_profile "$profile" "$root" memory
  append_cpu_summary "$profile"
  append_memory_summary "$profile"
done

write_static_checks

log ""
log "Human-readable summary:"
cat "$summary_path"
log ""
log "Full reports written under: $output_dir"
