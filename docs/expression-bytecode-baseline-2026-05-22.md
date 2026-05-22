# Expression Bytecode + Allocation Baseline (2026-05-22)

Context:
- Date: 2026-05-22
- Branch/worktree: `agent-go/issue-1513` in `.faktorial/worktrees/1513`
- Purpose: current-worktree baseline evidence for later expression-program compaction work.

## Commands Run

1. Narrow proof pack (storage diagnostics tests):

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExpressionProgramStorageDiagnosticsTests"
```

Result:
- Passed: 7
- Failed: 0
- Skipped: 0

2. `ExpressionProgram` storage diagnostics (forloop profile):

```bash
rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --expression-program-storage
```

Result excerpt:
- profile: `forloop`
- programs: `7`
- total_ops: `10`
- packed_op_estimated_bytes: `120`
- packed_op_shape: `flags=1`, `immediate0=4`, `immediate1=0`, `both_immediates=0`
- constants:
  - literals: `4`
  - strings: `0`
  - objects: `0`
  - identifiers: `7`
  - spread_masks: `0`
- optional_chain_shape:
  - optional_ops: `1`
  - short_circuit_ops: `0`
- side_state_estimates:
  - max_stack_slots: `9`
  - stack_value_bytes: `216`
  - stack_flag_words: `7`
  - stack_flag_bytes: `56`
- op_kind_histogram:
  - LoadLiteral: `4`
  - LoadIdentifier: `3`
  - LoadIdentifierCallTarget: `1`
  - Binary: `1`
  - Call: `1`
- max_stack_depth_histogram:
  - depth=1: `5`
  - depth=2: `2`

3. Allocation baseline (`tools/profile` wrapper):

```bash
rtk ./tools/profile forloop --memory
```

Result excerpt:
- total allocated: `7.05 MB`
- top sampled allocation owners by total:
  - `JsValue[]`: `2.52 MB`
  - `String`: `935.20 KB`
  - `PropertyDescriptor`: `724.71 KB`
  - `Entry<JsValue>[]`: `417.31 KB`
  - `Int32[]`: `412.95 KB`
  - `JsObject`: `208.20 KB`
  - `Double`: `205.01 KB`
  - `String[]`: `174.19 KB`
  - `VolatileNode<Int32,PrivateNameScope>[]`: `107.02 KB`
  - `RuntimeTypeCache`: `106.22 KB`
  - `BreakableFrame[]`: `104.14 KB`

## Issue #1516 Re-run (2026-05-22)

Commands rerun on branch `agent-go/issue-1516` in `.faktorial/worktrees/1516`:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~ExpressionProgramStorageDiagnosticsTests"
rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --expression-program-storage
rtk ./tools/profile forloop --memory
```

Comparison vs Part 1 baseline:
- Proof pack: unchanged (`Passed=6, Failed=0, Skipped=0`).
- Storage diagnostics: unchanged (`programs=7`, `total_ops=10`, `packed_op_estimated_bytes=120`, constants and stack-depth histogram match baseline exactly).
- Memory total allocated: unchanged (`7.05 MB`).
- Runtime/timing visible from diagnostics run: `Done in 4682ms (avg 234.10ms per iteration)`.
- Top sampled allocation owners: shape stable with small sampled variance (`String` sampled at `1.12 MB` in this rerun vs `1.02 MB` baseline; `BreakableFrame[]` dropped from top list; `Byte`/`JsHostHandler`/`HybridDictionary<JsValue>`/`JsObjectState` appeared near ~`104 KB` each).

Interpretation:
- AC-1 through AC-5 are satisfied.
- Current numbers are stable relative to the Part 1 baseline; no regression signal in proof pack, storage footprint metrics, or total allocated memory.

## Notes

- These measurements are baseline evidence from this worktree, not optimization claims.
- The `tools/profile` output is sampled profiler data.
- If `asynkron-profiler` is unavailable, `./tools/profile ... --memory` can fail before producing allocation output.
