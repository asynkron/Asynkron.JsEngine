# ADR 0162: Keep tail restarts activation-capture safe after arguments

## Status

Accepted

## Context

Issue #2003 / PR #2022 fixed the latest Test262 proper-tail-call batch:

- `language/statements/for/tco-const-body.js`;
- `language/statements/for/tco-let-body.js`;
- `language/statements/for/tco-lhs-body.js`;
- `language/statements/for/tco-var-body.js`; and
- `built-ins/Proxy/revocable/tco-fn-realm.js`.

The for-body rows were strict same-function tail calls whose return statements
still reached the quarantined legacy statement evaluator from loop-body shapes.
The ordinary recursive path could not survive the Test262-scale iteration
count, but a broad rewrite into AST fallback or loop-specific recursion logic
would have weakened the runtime-owned proper-tail-call boundary from ADR 0126.

The first safe slice added a legacy same-function restart bridge for strict,
simple, same-callable identifier calls from return position. Review then found
the harder edge case: a tail-call argument can evaluate code that captures the
current activation before the restart request is applied, for example by
creating an escaping closure over the current frame. Reusing that frame after
argument materialization would mutate the captured binding and make the saved
closure observe the restarted invocation instead of the original one.

The same delivery also had to make legacy restarts behave like fresh call entry
where the existing frame is intentionally reused: refresh the observable
`arguments` object, clear function-scoped `var` state, reset top-level lexical
TDZ bindings, clear or rebind `new.target`, preserve strict `this` rebinding,
and keep `finally` completion replacement ahead of restart application.

## Decision

Same-function tail restarts may reuse an activation only after the restart
executor has proven that the activation was not captured before the request and
was not captured while materializing the call arguments.

For the legacy return bridge:

1. identify only strict same-function identifier calls with simple unique
   parameters and no spread or optional call shape;
2. evaluate and store every argument value before scheduling a restart;
3. reject restart reuse if an argument expression may capture the activation,
   such as an inner function expression or direct eval;
4. re-check the active environment chain after argument materialization and
   reject reuse if any environment between the call site and the function
   closure is captured;
5. fall back to ordinary invocation for those unsafe shapes instead of
   mutating the current activation; and
6. when restart reuse is accepted, rerun the call-entry observable state
   refresh before executing the body again.

The IR runner follows the same ownership principle: it must reject a
same-function tail restart when the current activation chain has already been
captured before the request is recorded.

Do not treat stack reuse as a semantic guarantee. Tail-call optimization is an
implementation permission that must yield to closure capture, direct eval,
completion replacement, receiver/new-target rebinding, and arguments-object
observability.

## Consequences

- Strict for-body Test262 TCO rows can complete at the required iteration count
  without growing the host call stack.
- Argument expressions that can expose the current activation keep ordinary
  call semantics even if that means no tail restart for that shape.
- Future TCO work must prove unsafe activation-capture shapes, not only the
  positive stack-depth case.
- The focused proof pack for this class should include `TailCallTests`, the
  `Statements_for` Test262 `tco-*` filter, the proxy realm TCO filter when it
  is part of the reported batch, and the AST-seam scan for
  `EvaluateExpression(` / `ProfileEvaluateExpression(` in
  `TypedAstEvaluator.ExecutionPlanRunner*`.
- This ADR narrows the legacy bridge and IR restart guard. It does not make the
  legacy evaluator the owner of general TCO behavior, and it does not merge
  proxy realm ownership into tail-restart eligibility.

## Related

- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/function-activation-proof-pack.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
- `docs/adrs/0144-keep-dictionary-tail-restarts-strict-and-simple.md`
- `docs/adrs/0146-keep-functioncode-activation-isolation-ahead-of-ir-fast-paths.md`
