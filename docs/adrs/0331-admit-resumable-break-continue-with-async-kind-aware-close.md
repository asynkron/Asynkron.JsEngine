# ADR 0331: Admit resumable break/continue with async-kind-aware close

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-6e929f0772`
and PR #3225 closed B33 in the full unified-bytecode execution burndown. The
delivery admitted `BreakInstruction` and `ContinueInstruction` for resumable
generator and async bodies after earlier unified-bytecode work had already made
the compiler and VM own the hard parts:

- `UnifiedBytecodeCompiler` already lowered both IR records to VM-owned
  `Break` and `Continue` opcodes.
- `ExecuteResumable` already routed those opcodes through
  `TryCleanupDriverStatesForControlTargetResumable`.
- Existing driver descriptors already carried enough target topology to decide
  whether labeled control exited a driver or stayed inside the target loop.

The remaining behavior risk was iterator-close settlement mode. The resumable
close path always called `AwaitScheduler.TryResolvePromiseOrYield` with
`asyncStepMode: true`, which was correct for async functions and async
generators, but too broad for sync generators. A sync generator that breaks out
of a `for-of` after suspension must resolve an ordinary synchronous
`Iterator.return()` result synchronously, while async-like frames must keep
async-step scheduling.

## Decision

Admit resumable `break`/`continue` by reusing the VM-owned control-flow opcodes
and driver cleanup topology, while making iterator-close settlement depend on
the resumable frame kind.

- `UnifiedBytecodeProductionEligibility.EvaluateResumable` allowlists
  `BreakInstruction` and `ContinueInstruction` only because the compiler and
  resumable VM already have corresponding `Break`/`Continue` support.
- `UnifiedBytecodeResumeState.IsAsyncLike` records whether the resumable frame
  is an async function or async generator. Sync generators leave the flag false.
- Resumable iterator close passes `asyncStepMode: resumeState.IsAsyncLike` to
  the await scheduler, so sync-generator close consumes plain
  `Iterator.return()` results synchronously and async-like close still schedules
  promise settlement as an async step.
- Runtime proof stays route-aware: labeled generator `break` must close the
  iterator exactly once on the `unified-bytecode-resumable-generator-fast-path`;
  labeled generator `continue` must keep the target iterator open on the same
  route.

## Consequences

- Future resumable control-flow widening should not treat instruction
  allowlisting as enough. The proof has to show selector admission, compiled
  VM opcode shape, cleanup topology, iterator-close settlement mode, and public
  route hit together.
- Any new resumable frame kind that can close iterators must set the frame kind
  flags before execution reaches driver cleanup. Otherwise sync and async close
  semantics can silently collapse onto the wrong scheduler mode.
- ADR 0313 and ADR 0314 remain the topology records for nested and labeled
  driver cleanup. This ADR records the resumable suspension boundary and
  sync-vs-async close settlement detail.

## Evidence

- Delivery PR #3225 merged as commit
  `abd9f0424f3f15323bb99020c6146eac430cfc53`.
- Build-stage commit:
  - `48ad2da1c` ("Admit resumable break continue bytecode")
- Focused proof from the build stage:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.EvaluateResumable_GeneratorBreakAfterYield_AcceptsBreakInstruction|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.EvaluateResumable_GeneratorContinueAfterYield_AcceptsContinueInstruction|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.EvaluateResumable_AsyncBreakAndContinueAfterAwait_AcceptsControlInstructions|FullyQualifiedName~UnifiedBytecodeResumableForOfTests.GeneratorLabeledBreakAfterYield_ClosesIteratorOnce|FullyQualifiedName~UnifiedBytecodeResumableForOfTests.GeneratorLabeledContinueAfterYield_KeepsTargetIteratorOpen|FullyQualifiedName~ExpressionProgramCoverageMapTests"` passed 19 tests.
  - `rtk git diff --check` passed.
- The delivery updated:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncGeneratorInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableForOfTests.cs`
  - `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`

## Related

- ADR 0313:
  `docs/adrs/0313-admit-nested-driver-labeled-abrupt-cleanup-in-unified-bytecode.md`
- ADR 0314:
  `docs/adrs/0314-split-unified-bytecode-driver-break-and-continue-cleanup-targets.md`
- ADR 0330:
  `docs/adrs/0330-keep-iterator-init-async-kind-and-awaited-source-gates-separate.md`
- `docs/rules/ir-control-flow-cleanup.md`
- `docs/plans/bytecode-burndown-checklist.md`
- `docs/bytecode-progress.md`
