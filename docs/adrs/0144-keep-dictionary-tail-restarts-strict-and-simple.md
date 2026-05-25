# ADR 0144: Keep dictionary tail restarts strict and simple

## Status

Accepted

## Context

Issue #1917 / PR #1932 revisited the proper-tail-call residual set after the
earlier TCO fixes. The exact non-eval Test262 rows
`language/expressions/call/tco-non-eval-function-dynamic.js`,
`language/expressions/call/tco-non-eval-function.js`,
`language/expressions/call/tco-non-eval-global.js`, and
`language/expressions/call/tco-non-eval-with.js` still overflowed the host
stack through `ExecuteProgramCall` and `InvokeWithContextSlow`.

The existing same-function tail restart path assumed the lowered plan had
activation-slot parameter indices. These non-eval rows used the older
dictionary-backed parameter environment shape instead, so the runner fell back
to ordinary recursive calls even though the callee was the same function and
the call was restart-eligible.

The tempting broad fix was to treat missing activation slots as equivalent to
activation slots and restart through dictionary bindings for every function
shape. That is unsafe. Sloppy functions, duplicate parameters, defaults, rest
parameters, destructuring, and bodies that need an extra hoisted environment
have observable binding behavior that is not modeled by simply redefining
parameter names in the current function environment.

The same focused issue also included
`built-ins/Proxy/revocable/tco-fn-realm.js`. PR #1932 did not mark that row
fixed; it remained a wrong-realm TypeError rooted in proxy realm propagation
around `JsProxy` / `Proxy.revocable` cross-realm invocation. That residual
should stay owned by proxy realm work, not by widening tail restart eligibility.

## Decision

Allow same-function tail restarts without activation-slot parameter indices
only for the narrow dictionary-backed shape that can be rebound exactly:

1. the function is strict;
2. every parameter is a simple unique identifier;
3. there are no rest, default, or destructuring parameters;
4. the function body does not need an extra hoisted environment;
5. the restart still targets the same callable and preserves the existing
   receiver rebinding and cleanup ordering rules from ADR 0126 and ADR 0139.

When those guards hold, the runner may locate the current parameter/function
environment, redefine each parameter binding from the already evaluated
argument list, reset the program counter to the plan entry point, and reuse the
existing runner frame.

If any guard fails, keep the ordinary invocation path. Do not synthesize a
partial activation-slot model at restart time, and do not widen dictionary
rebinding to dynamic binding shapes without focused semantic proof.

Keep proxy realm failures split from this decision. Tail-call routing may expose
a proxy realm bug, but dictionary restart eligibility must not choose or repair
proxy error realms.

## Consequences

- The exact four non-eval Test262 rows can complete without host stack growth
  while preserving same-function restart semantics.
- Dictionary-backed functions remain eligible only when rebinding parameter
  names is semantically equivalent to a new strict call.
- Complex parameter and hoisted-environment shapes stay conservative until a
  future change proves their binding behavior separately.
- Future TCO residual work should classify failures by owner before broadening
  tail-call routing: dictionary restart eligibility, sync IR trampoline shape,
  direct eval/global/with binding, cleanup completion, and proxy realm identity
  are separate boundaries.
- Focused proof for this class should include the `Name~tco-non-eval` Test262
  filter, `TailCallTests`, an AST-eval seam scan, and the allocation stability
  profile used for this issue.

## Related

- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/ecmascript-proxy-realm-errors.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
