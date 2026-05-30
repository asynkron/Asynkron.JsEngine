# ADR 0283: Admit labeled control flow in unified bytecode and decline driver-crossing abrupts

## Status

Accepted

## Context

Faktorial issue `gh2679` widened production unified bytecode to label-dependent
control flow — labeled statements, labeled loops, labeled block `break`, and
labeled `break`/`continue`. This is the contract's "Ranked Next Unsupported
Buckets" #5 and a direct continuation of the unlabeled loop-control lane
(PR #2489, ADR 0253), which established that loop-control targets are
*compiler-owned*, not source-syntax-owned.

Before this change two gates blanket-declined every labeled breakable region:

- Eligibility: `UnifiedBytecodeProductionEligibility.TryFindPlanDecline`
  declined any `BreakableEnterInstruction { Label: not null }` with
  `LabelControlFlow`.
- Compiler: `UnifiedBytecodeCompiler.IsSupportedBreakableEnter` rejected any
  breakable enter whose `Label` was non-null.

This was conservative but treated labels as a source-syntax permission, which
ADR 0253 explicitly warns against. The compiler already resolves `break` and
`continue` to numeric targets through one resolved-jump path
(`TryAppendResolvedJump`), and a labeled `break`/`continue` is just a jump to an
*outer* construct's target rather than the innermost one. Label resolution is
performed in the plan builder (`ControlFlowEmitter`), so the compiler receives a
fully resolved numeric target regardless of whether the source used a label.

The correctness-sensitive part is **driver cleanup**. When an abrupt exits an
iterator/for-in driver loop, the loop's iterator must be closed. The VM closes
drivers via `CleanupDriverStatesForBreakTarget`, which closes exactly the driver
whose descriptor `BreakTarget` equals the abrupt jump target (single-level
cleanup). For a labeled abrupt that exits *several* nested driver loops at once
(e.g. `break outer` from inside an inner `for-of`), only the target loop's
driver is closed; intervening inner iterators would be leaked. A program-counter
heuristic for multi-level cleanup was prototyped and rejected: the compiler's
lazy target compilation does **not** guarantee that an inner loop's exit PC
precedes its enclosing loop's exit PC, so a PC-ordering rule closed the wrong
set of drivers (observed: outer closed, inner leaked). The unlabeled lane never
hit this because unlabeled `break`/`continue` only ever exits one loop level.

## Decision

Admit labeled control flow on the production route, and keep the boundary
compiler-owned and target-explicit.

- Do not blanket-decline labeled `BreakableEnterInstruction`. A labeled
  breakable region routes whenever its unlabeled IR topology would route; the
  pre-existing canonical-loop topology checks (condition-first backedge,
  for-style update continue, do-while consequent, single-pass driver loops)
  still gate which *shapes* are eligible, labeled or not.
- Relax `IsSupportedBreakableEnter` to accept labeled breakable enters, and let
  `HasLoopContinueTarget` recognize labeled loop continue/break metadata so
  labeled loop backedges validate through the same owned path.
- Keep the VM fallback-free and single-level. The VM continues to close only the
  driver whose break target equals the abrupt jump target.
- Decline, before VM execution, the one labeled shape the single-level VM
  cleanup cannot serve safely: a labeled `break`/`continue` that transfers
  control out of an enclosing iterator/for-in driver loop it is not directly
  targeting (driver-crossing). This is detected in production eligibility by
  computing each driver loop's structured body region and checking whether the
  abrupt executes inside that loop while its resolved target leaves the loop
  without being the loop's own continue/break target. The decline keeps the
  `LabelControlFlow` code.
- Do **not** add a program-counter-based multi-driver cleanup. Multi-driver
  labeled cleanup is the next loop-control widening frontier and requires
  nesting metadata on driver descriptors, not a PC heuristic.
- Keep proof paired: production selector acceptance (labeled loop, labeled break
  out of a for-of, labeled single-loop continue), labeled block/loop invocation
  route-log proofs, driver-close-on-labeled-break proof, and negative
  driver-crossing decline proofs for both labeled `break` and labeled
  `continue`.

## Consequences

- "Ranked Next Unsupported Buckets" #5 is cleared except for the narrow
  driver-crossing residue; the contract's Production Loop-Control Boundary and
  the bucket entry are updated, and `docs/roadmap.md` records the widening.
- `LabelControlFlow` remains a live decline-taxonomy enum member, now describing
  only the driver-crossing residue rather than all labeled control flow.
- Prototype `TryCompile` decline expectations for labeled while loops and
  labeled non-loop statements moved with the compiler boundary and now assert
  successful compilation. The `UnsupportedControlFlowFunctions` labeled entry
  moved to `SupportedLoopControlFunctions`. Per ADR 0253, stale decline
  expectations must move in the same delivery so `make quality` stays
  meaningful.
- The no-mixed-execution invariant is preserved: a labeled plan either fully
  qualifies for the VM or declines as a whole before execution.

## Evidence

- Build-stage proof:
  `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`
  (137 passing) and
  `dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  (129 passing).
- Driver-close proof: labeled `break` out of a single-pass `for-of` closes the
  iterator (`return: function` side effect observed) before the function
  returns, on the production fast path.
- Negative proofs: labeled `break` and labeled `continue` that cross an inner
  `for-of` driver loop decline with `LabelControlFlow` before VM execution.

## Related

- ADR 0210: `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
