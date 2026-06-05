# ADR 0330: Keep iterator init async-kind and awaited-source concepts separate

- Status: Accepted
- Date: 2026-06-05
- Tags: execution, unified-bytecode, iterators, async

## Context

ADR 0288 admitted TDZ head environments for synchronous iterator and for-in
drivers, while leaving two later slices out of production routing: awaited
sources and async iterator driver state. Later resumable unified bytecode work
made one of those boundaries narrower than the ADR 0288 table: a synchronous
iterator driver may now carry an `AwaitedProgram` source payload because the
resumable route compiles the source expression, awaits it, and then initializes
the same synchronous iterator driver state.

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7e33a72606`
and PR #3217 hardened that gate after the distinction became easy to blur. The
production eligibility helper had to keep three cases separate:

- a synchronous driver with exactly one `IterableProgram` payload;
- a synchronous driver with exactly one `AwaitedProgram` payload;
- an async iterator driver kind (`IteratorDriverKind.Await`), regardless of
  source-payload shape.

## Decision

`IteratorInitInstruction` production eligibility treats source payload shape and
iterator driver kind as separate concepts.

- The instruction must carry exactly one source payload: either `IterableProgram`
  or `AwaitedProgram`, never neither and never both.
- `IteratorDriverKind.Sync` may pass that payload gate. An `AwaitedProgram` on a
  sync driver is not an async iterator driver; it represents an awaited source
  followed by synchronous iterator initialization in the owned resumable path.
- `IteratorDriverKind.Await` remains the async iterator protocol boundary. B41
  admits the simple resumable VM subset once protocol state, next-result/value
  settlement, and close behavior are VM-owned by unified bytecode.

The helper tests assert the boundary points directly: sync iterable source
accepted, sync awaited source accepted, async-kind sources accepted for the B41
subset, and missing/dual source payloads declined before compilation.

## Consequences

- Future widening must not use `AwaitedProgram` as shorthand for async iterator
  driver state. The driver kind is the protocol-state boundary, while
  `AwaitedProgram` only describes source-expression evaluation.
- Async-kind tests should exercise `IsSupportedIteratorInit` directly where
  async-like activation pre-gates would otherwise hide the iterator-kind
  decision.
- Async-kind admission must prove the full settlement surface, not just
  `IteratorInit`: async iterator `next` results, yielded values, `return()`
  close results, and control-flow cleanup jumps must all be owned by
  `ExecuteResumable` and reflected in the resumable opcode allowlist.
- ADR 0288 remains the record for admitting TDZ head environments, but its
  awaited-source row is historical. The current iterator-init boundary is the
  one above.

## Evidence

- Issue:
  `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-7e33a72606`
- PR: #3217
- Delivery commit: `e9ab87b89 Harden iterator init async-kind eligibility gate`
- Original focused tests:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.IsSupportedIteratorInit"`
  passed 6 tests.
- Focused route-adjacent test:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_ForOfPlan_AcceptsIteratorDriverOpcodes"`
  passed 1 test.
- Diff hygiene: `rtk git diff --check` passed.
- B41 admission issue:
  `planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-b462ae896c`
- PR: #3220
- Delivery commit:
  `4ad2cf19d Agent: task planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-b462ae896c`
- B41 focused tests:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ExpressionProgramCoverageMapTests|FullyQualifiedName~UnifiedBytecodeResumableForOfTests"`
  passed 19 tests across the coverage-map and resumable for-of proof packs.
