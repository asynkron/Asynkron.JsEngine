# ADR 0168: Keep ExecuteProgram JsValue-native

## Status

Accepted

## Context

Issue `autrun-disjor8mq4lk-f2e8b924c2` / PR #2087 continued the Unboxer
cleanup of private `object?` carriers in the core runtime.

Before the delivery, `JsEngine.ExecuteProgram` returned `object?` by calling
`ProgramNode.EvaluateProgram(...)`. Several private callsites immediately
converted that result back into `JsValue`:

- direct eval in `EvalHostFunction`;
- ShadowRealm evaluate;
- the `Function` constructor dynamic parse/execute path; and
- dynamic generator-function constructors.

That carrier was not a public facade, host-interop, debugger, or diagnostic
boundary. It sat inside script/eval execution plumbing where callers already
needed JavaScript values. The only intentional `object?` surfaces in the slice
were public `Evaluate*` facade returns and the broader module-body result
surface, which still stores module `LastValue` through an `object?` path.

The accepted delivery changed `JsEngine.ExecuteProgram` to return `JsValue` via
`EvaluateProgramJsValue(...)`, moved direct eval and ShadowRealm evaluate onto
the typed evaluator entrypoint, and deleted caller-side
`JsValue.FromObjectUnsafe(...)` rewraps in the immediate constructor/eval
callers. Public facade methods unwrap at the API boundary with `ToObject()` or
`UnwrapResult(...)`.

Focused proof included:

```bash
rtk rg -n "\.EvaluateProgram\(" src/Asynkron.JsEngine --glob '*.cs'
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ShadowRealm|FullyQualifiedName~FunctionConstructor|FullyQualifiedName~Eval"
rtk git diff --check
```

The final `EvaluateProgram` scan left only the module-body callsite in
`JsEngine.ExecuteModuleBody`, and the focused internal test pack passed 408
tests.

Issue #2263 / PR #2264 exposed a follow-up gap outside `src/`: the
BenchmarkDotNet `EvaluationOverheadBenchmarks` direct-AST evaluation methods
used reflection to fetch `GlobalEnvironment` and `RealmState`, then called the
obsolete `ProgramNode.EvaluateProgram(...)` wrapper. Because the benchmark
project is part of the default solution build, marking that wrapper
`[Obsolete(..., true)]` made `rtk dotnet build Asynkron.JsEngine.sln` red with
two CS0619 errors. The repair made the benchmark methods return `JsValue` and
call `EvaluateProgramJsValue(...)`; the post-fix scan left `EvaluateProgram(`
only at the obsolete wrapper definition.

## Decision

Keep private program/script/eval execution plumbing `JsValue`-native once the
selected callsites all consume JavaScript values.

For future execution-wrapper migrations:

1. prefer `EvaluateProgramJsValue(...)` or a `JsValue`-returning private
   wrapper for direct eval, ShadowRealm evaluate, and dynamic function
   construction paths;
2. do not route private script/eval results through `object?` and immediately
   rewrap with `JsValue.FromObjectUnsafe(...)`;
3. keep public `Evaluate*` methods as explicit object-facade boundaries by
   unwrapping at the final API edge;
4. treat async module result storage and `ExecuteModuleBody` as a separate
   migration surface with its own focused proof; and
5. treat benchmark, test, profiling, and diagnostic harnesses that bypass the
   public facade and call internal program execution entrypoints as internal
   core callers; keep those callsites on `JsValue` and unwrap only at an
   explicit reporting edge; and
6. prove the slice with a before/after `EvaluateProgram`/`ExecuteProgram`
   callsite scan plus focused eval, Function-constructor, and ShadowRealm
   coverage. When an obsolete wrapper is error-level, include repo-internal
   harnesses such as `benchmarks`, `tests`, and `tools` in that scan, not only
   `src/Asynkron.JsEngine`.

## Consequences

- The core runtime has fewer accidental object-carrier seams between parsing,
  eval, and dynamic function construction.
- Public API behavior stays stable because unwrapping remains explicit at the
  facade edge.
- Future Unboxer slices should not widen an `ExecuteProgram` cleanup into
  module `LastValue` storage unless the module async/sync boundary is the
  selected owner surface.
- Default solution builds compile benchmark harnesses, so internal migration
  proof must include any benchmark direct-evaluation callsites that reach the
  evaluator by reflection.
- ShadowRealm wrapping remains cross-realm sensitive: keep primitive/callable
  wrapping semantics intact while removing only the carrier conversion.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0111-keep-array-static-sync-result-helpers-jsvalue-native.md`
- `docs/adrs/0143-keep-generator-pending-completion-payloads-jsvalue-native.md`
- `docs/adrs/0164-keep-super-constructor-resolver-jsvalue-native.md`
