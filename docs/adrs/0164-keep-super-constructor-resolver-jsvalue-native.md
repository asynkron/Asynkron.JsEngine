# ADR 0164: Keep super-constructor resolver JsValue-native

## Status

Accepted

## Context

Issue `autrun-disgkh6pbz1k-90662a8047` / PR #2068 continued the bounded
object-carrier cleanup for evaluator helper flows.

Before the delivery, `JsEnvironmentExtensions.ResolveSuperConstructorForCall`
returned `object?` even though every successful branch represented a JavaScript
constructor value or runtime callable/accessor. Both evaluator callsites,
`Ast/Legacy/ExpressionNodeExtensions.cs` and
`Ast/TypedAstEvaluator.ExecutionPlanRunner.Helpers.cs`, immediately wrapped
the result back into `JsValue` with
`JsValue.FromObjectUnsafe(dynamicSuperConstructor)`.

That object carrier was not a public facade, host-interop, debugger, or
diagnostic boundary. It was a private resolver feeding evaluator paths that
already needed `JsValue`. The only extra state the `object?` return expressed
was absence of a super constructor.

The accepted delivery replaced the nullable object return with
`TryResolveSuperConstructorForCall(..., out JsValue constructorValue)`. The
helper now converts known runtime callables/accessors to `JsValue` at the
resolver boundary and uses the boolean result to preserve the missing-super
error path. Both legacy AST and expression-bytecode callsites now consume the
typed value directly.

## Decision

Keep private super-constructor resolution `JsValue`-native.

For optional resolver-style helper migrations:

1. return the resolved JavaScript value as `JsValue` when the caller already
   needs a JavaScript value;
2. represent missing resolution separately, for example with a `bool`
   `Try...(..., out JsValue value)` contract or an equivalent typed result;
3. do not use nullable `object?` as a private resolver payload when the only
   non-value state is absence;
4. do not use `JsValue.Undefined` as the sole absence signal when callers must
   preserve an existing missing-value error branch; and
5. keep any unavoidable `JsValue.FromObjectUnsafe(...)` wrapping inside the
   resolver boundary for known runtime objects instead of at every evaluator
   callsite.

## Consequences

- Future evaluator resolver migrations should remove caller-side
  `JsValue.FromObjectUnsafe(...)` rewraps once the selected resolver can own the
  typed conversion.
- Optional resolver APIs need an explicit presence channel. A typed payload plus
  a boolean/result shape is safer than conflating "not found" with a JavaScript
  value.
- Legacy AST and expression-bytecode paths must stay aligned when they share a
  resolver, and the proof should cover both callsites.
- Focused proof for this area should pair a targeted legacy-pattern search with
  class-super semantic tests, including the AST-free derived-constructor
  `super(...)` regression.

## Related

- `.claude/rules/jsvalue-core-values.md`
