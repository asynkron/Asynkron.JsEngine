#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/.." && pwd)
output_root="$script_dir/profile-output"
timestamp=$(date +%Y%m%d_%H%M%S)
output_dir="$output_root/performance_$timestamp"
summary_path="$output_dir/summary.txt"
insights_path="$output_dir/insights.txt"

filter="${PERF_FILTER:-Asynkron.JsEngine}"
depth="${PERF_CALLTREE_DEPTH:-14}"
width="${PERF_CALLTREE_WIDTH:-8}"
sibling_cutoff="${PERF_CALLTREE_SIBLING_CUTOFF:-1}"
profiles="${PERF_PROFILES:-bytecode:EvaluateExpressionProgram ir-arithmetic:ExecuteInstructionLoop forloop:ExecuteInstructionLoop activation-noargs:InvokeWithContextSlow activation-params:InvokeWithContextSlow activation-arguments:InvokeWithContextSlow activation-closures:InvokeWithContextSlow activation-evalscope:InvokeWithContextSlow functioncalls-lite:ExecuteInstructionLoop objectcreation:ExecuteInstructionLoop arrayops:ExecuteInstructionLoop}"

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

write_insights() {
  python3 - <<'PY' "$output_dir" "$insights_path" "$profiles"
from pathlib import Path
import re
import sys

output_dir = Path(sys.argv[1])
insights_path = Path(sys.argv[2])
profile_entries = sys.argv[3].split()

noise = (
    "Program.Main",
    "StateMachine.Evaluate",
    "RunScript",
    "RunScriptInternal",
)

def profile_label(profile: str) -> str:
    if profile == "bytecode":
        return "Bytecode expression VM"
    if profile == "simplearithmetic":
        return "IR expression dispatch"
    if profile == "ir-arithmetic":
        return "IR arithmetic loop"
    if profile == "forloop":
        return "IR loop execution"
    if profile == "activation-noargs":
        return "Activation no-arg calls"
    if profile == "activation-params":
        return "Activation parameter binding"
    if profile == "activation-arguments":
        return "Activation arguments object"
    if profile == "activation-closures":
        return "Activation nested closures"
    if profile == "activation-evalscope":
        return "Activation eval-sensitive scope"
    if profile == "functioncalls-lite":
        return "IR function calls"
    if profile == "objectcreation":
        return "IR object creation"
    if profile == "arrayops":
        return "IR array callbacks"
    return profile

def category(profile: str) -> str:
    if profile.startswith("activation-"):
        return "ACTIVATION"
    return "BYTECODE" if profile == "bytecode" else "IR"

def parse_cpu(profile: str):
    path = output_dir / f"{profile}-cpu.txt"
    rows = []
    in_table = False
    if not path.exists():
        return rows
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if "Top Functions" in line:
            in_table = True
            continue
        if in_table and line.startswith("Time (ms)"):
            continue
        if in_table and line.startswith("Filtered out"):
            break
        if in_table and line.strip():
            match = re.match(r"\s*([0-9.]+)\s+([0-9,]+)\s+(.+?)\s*$", line)
            if match:
                rows.append((float(match.group(1)), match.group(2), match.group(3)))
    return rows

def parse_memory(profile: str):
    path = output_dir / f"{profile}-memory.txt"
    total = "unknown"
    rows = []
    in_table = False
    if not path.exists():
        return total, rows
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("Total allocated"):
            total = line.split("Total allocated", 1)[1].strip()
            continue
        if line.startswith("Type") and "Count" in line and "Total" in line:
            in_table = True
            continue
        if in_table and line.startswith("TOTAL"):
            break
        if in_table and line.strip():
            match = re.match(r"(.+?)\s+([0-9,]+)\s+([0-9.]+\s+[KMG]B)\s*$", line)
            if match:
                rows.append((match.group(1).strip(), match.group(2), match.group(3)))
    return total, rows

def interesting_cpu(rows):
    interesting = []
    for time_ms, calls, name in rows:
        if any(skip in name for skip in noise):
            continue
        interesting.append((time_ms, calls, name))
        if len(interesting) == 5:
            break
    return interesting

def hints(profile: str, cpu_rows, mem_rows):
    names = " ".join(name for _, _, name in cpu_rows[:12])
    types = " ".join(type_name for type_name, _, _ in mem_rows[:12])
    top_types = " ".join(type_name for type_name, _, _ in mem_rows[:5])
    result = []
    if profile == "bytecode":
        result.append("This is the direct expression VM loop; parser/lowering/statement dispatch are intentionally outside the hot loop.")
    if profile == "ir-arithmetic":
        result.append("This is a valid repeated IR arithmetic loop; it uses var bindings so repeated iterations do not trip redeclaration errors.")
    if "ProfileApplyBinaryOperator" in names or "ApplyBinaryOperator" in names:
        result.append("Binary operator dispatch is visible; specialize numeric arithmetic before chasing smaller decode costs.")
    if "HandleAssignmentSlot" in names:
        result.append("Assignment-slot execution dominates this profile; inspect assignment references, global/declarative binding writes, and value boxing.")
    if "TryGetIdentifierJsValue" in names or "TryReadIdentifierWithSlot" in names:
        result.append("Identifier reads remain relevant inside IR expression execution.")
    if "SetPropertyInternal" in names or "SetPropertyJsValue" in names:
        result.append("Global/object-backed writes are visible; this profile is paying object property write machinery for hot assignments.")
    if "HandleIncrementSlotSlow" in names:
        result.append("Increment handling is on the slow path; a direct numeric slot increment path would be easy to measure here.")
    if "JsArray.Push" in names or "List<JsValue>.AddWithResize" in names:
        result.append("Array construction pays for grow/copy; pre-sizing or denser array backing would likely show up immediately.")
    if "DefineObjectLiteralProperty" in names or "JsObject.DefineProperty" in names:
        result.append("Object/property creation is descriptor and dictionary heavy.")
    if "HasWithObjectInChain" in names:
        result.append("Dynamic-scope checks are still visible on non-dynamic loop execution; consider caching/proving the no-with path.")
    if "CreateExecutionEnvironment" in names or "CreateArgumentsObject" in names:
        result.append("Function calls are dominated by environment creation, arguments object creation, slot growth, and parameter binding.")
    if "BindFunctionParameters" in names:
        result.append("Parameter binding is hot; argument-to-slot write costs are a first-order activation target.")
    if "CreateExecutionContext" in names:
        result.append("Execution context creation is visible; context allocation and initialization overhead are significant here.")
    if "IncreaseSlotArraySize" in names or "GrowSlotArray" in names or "JsSlot[]" in types:
        result.append("Slot array growth appears in activation-heavy paths; reducing slot churn can cut both CPU and memory cost.")
    if "SyncFunctionInvoker" in names or "InvokeWithContextSlow" in names:
        result.append("Call-entry overhead is measurable in the invoker path; this profile is suitable for activation regression tracking.")
    if "ArrayPrototype.Map" in names or "ArrayPrototype.Filter" in names or "ArrayPrototype.Reduce" in names:
        result.append("Array higher-order methods are callback/invocation heavy, not just bytecode interpretation.")
    if "ExecutionPlanRunner" in types or "EvaluationContext" in types or "JsEnvironment" in types:
        result.append("Allocations include runner/context/environment objects; pooling or reuse is probably higher leverage than bytecode encoding here.")
    if "JsValue" in top_types or "Double" in top_types:
        result.append("Primitive arithmetic is still allocating/boxing enough to dominate sampled allocations.")
    if "PropertyDescriptor" in top_types or "Entry<String,PropertyDescriptor>[]" in top_types:
        result.append("Property descriptor storage dominates allocations in object-heavy paths.")
    if not result:
        result.append("No obvious named hotspot heuristic matched; inspect the full call tree file for this profile.")
    return result

sections = []
overview = []
for entry in profile_entries:
    profile = entry.split(":", 1)[0]
    cpu_rows = parse_cpu(profile)
    mem_total, mem_rows = parse_memory(profile)
    hot = interesting_cpu(cpu_rows)
    alloc = mem_rows[:5]
    overview.append((profile, mem_total, hot[:3], alloc[:3]))
    sections.append((profile, mem_total, hot, alloc, hints(profile, cpu_rows, mem_rows)))

lines = []
lines.append("Asynkron.JsEngine profiling insights")
lines.append(f"Output: {output_dir}")
lines.append("")
lines.append("How to read this:")
lines.append("- BYTECODE profiles isolate hand-built ExpressionProgram execution.")
lines.append("- ACTIVATION profiles isolate function-call setup costs (environment/context/arguments/parameter binding).")
lines.append("- IR profiles execute real script profiles and root CPU call trees at the statement runner or expression VM.")
lines.append("- Memory numbers are sampled allocation totals by type; use them for direction, not byte-perfect accounting.")
lines.append("")
lines.append("Executive summary:")
for profile, mem_total, hot, alloc in overview:
    hot_text = "; ".join(f"{name} ({time_ms:.0f}ms)" for time_ms, _, name in hot) or "no CPU rows"
    alloc_text = "; ".join(f"{type_name} {size}" for type_name, _, size in alloc) or "no allocation rows"
    lines.append(f"- [{category(profile)}] {profile_label(profile)}: allocated {mem_total}; CPU: {hot_text}; allocs: {alloc_text}.")

lines.append("")
lines.append("Detailed interpretation:")
for profile, mem_total, hot, alloc, profile_hints in sections:
    lines.append("")
    lines.append(f"## [{category(profile)}] {profile_label(profile)}")
    lines.append(f"Profile key: {profile}")
    lines.append(f"Sampled allocations: {mem_total}")
    lines.append("Top useful CPU frames:")
    if hot:
        for time_ms, calls, name in hot:
            lines.append(f"- {name}: {time_ms:.2f}ms across {calls} samples/calls")
    else:
        lines.append("- none parsed")
    lines.append("Top allocation types:")
    if alloc:
        for type_name, count, size in alloc:
            lines.append(f"- {type_name}: {size} sampled in {count} samples")
    else:
        lines.append("- none parsed")
    lines.append("What this suggests:")
    for hint in profile_hints:
        lines.append(f"- {hint}")

lines.append("")
lines.append("Suggested next optimization order:")
lines.append("1. Function-call environment setup: CreateExecutionEnvironment, CreateArgumentsObject, JsSlot[]/EvaluationContext/JsEnvironment allocation.")
lines.append("2. Bytecode/object paths: descriptor/dictionary churn in object literals and property writes.")
lines.append("3. Array construction/callback paths: List<JsValue> growth, JsArray.Push, ArrayPrototype map/filter/reduce callback invocation.")
lines.append("4. Tight-loop expression execution: slot read validation, HasWithObjectInChain, branch compare, buffer clear overhead.")
lines.append("")
lines.append("Artifacts:")
for entry in profile_entries:
    profile = entry.split(":", 1)[0]
    lines.append(f"- {profile}: {output_dir / (profile + '-cpu.txt')} and {output_dir / (profile + '-memory.txt')}")
lines.append(f"- Summary: {output_dir / 'summary.txt'}")
lines.append(f"- Static checks: {output_dir / 'static-checks.txt'}")

insights_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
PY
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
write_insights

log ""
log "Profiling insights:"
cat "$insights_path"
log ""
log "Human-readable summary:"
cat "$summary_path"
log ""
log "Full reports written under: $output_dir"
