# ADR 0234: Keep unified bytecode property writes strict and directive owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e`
and PR #2396 hardened the production unified-bytecode property-write boundary
after PR #2379 had admitted the first ordinary write/update opcodes.

The carried baseline showed the focused property-write proof pack at 4 passed
out of 5, with one failed proof. The first build-stage pass added semantic
tests for setter/proxy receiver identity, computed key ordering, and
strict/sloppy failed writes, but review found two proof gaps:

- the computed-write proof shape did not exercise the admitted unified
  production path; and
- the strict-mode failed-write arm did not prove that the strict function
  itself routed through `unified-bytecode-production-fast-path`.

The build-back fix kept the production surface narrow and added only the
runtime support needed to prove the intended boundary:

- strict directive prologue functions can compile because the unified compiler
  skips only string-literal `EvaluateAndDiscard` directive instructions; and
- the sync invocation bridge passes lexical strictness into the unified VM so
  property set/update handle resolution preserves strict failed-write
  semantics without creating a function environment or falling back to
  `ExecutionPlanRunner`.

## Decision

Keep production unified-bytecode property writes and updates strictness-owned by
the sync invocation bridge and directive-prologue-owned by the unified
compiler.

1. Pass the active function lexical strictness into
   `UnifiedBytecodeVirtualMachine.Execute` for production sync invocation.
2. Resolve property set/update handles with `context.CurrentScope.IsStrict ||
   isStrict`, not with `context.CurrentScope.IsStrict` alone.
3. Allow strict directive prologues by treating only string-literal
   `EvaluateAndDiscard` instructions as no-op directive discards in the
   unified compiler.
4. Keep every non-directive discarded expression declined before VM execution.
5. Prove strict/sloppy failed writes with public invocation logs for both arms,
   so the strict function's TypeError behavior is known to come from the owned
   unified path.
6. Keep computed-write proof bodies route-eligible. If a proof needs unrelated
   RHS side effects, put them at the call site and pass the resulting value into
   the admitted `write(box, key, value)` body.

## Consequences

- Strict failed writes now throw through owned unified property set/update
  semantics instead of accidentally inheriting sloppy behavior from the current
  outer scope.
- Directive prologues do not authorize generic expression-discard support.
  Supporting arbitrary discarded expressions still needs its own selector,
  compiler, VM, and route-proof slice.
- Property-write proof tests must prove both semantics and route selection.
  A passing behavior test is not enough when the accepted function body could
  have declined and executed through an older path.
- Future property write/update widening must keep selector acceptance,
  compiler directive handling, VM strictness, and public route logs aligned in
  the same slice.

## Evidence

- Delivery PR #2396 merged as commit
  `29e627eb Agent: task planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e (#2396)`.
- Build-back commit
  `00024ada037a3988452dc8a56606d87ccab358bd Fix strict/computed property-write unified proof coverage`
  added the narrow directive discard support and VM strictness threading.
- The focused property-write proof pack moved from 4 passed / 5 total to
  5 passed / 5 total.
- Focused verification also kept the property-write eligibility pack green at
  2 passed / 2 total, the AST-eval seam scan empty, and `rtk git diff --check`
  clean.

## Related

- `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0231-keep-unified-bytecode-property-write-private-names-guarded.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `.claude/rules/expression-bytecode-assignment.md`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
