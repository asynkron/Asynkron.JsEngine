# ADR 0227: Keep Math host-function fast dispatch marked and arity-specific

## Status

Accepted

## Context

Issue `autrun-diteq2bzwzxc-ba3c5ff36a` / PR #2355 continued the
`simplearithmetic` optimizer line after ADR 0216 fixed profiler scope parity
for top-level lexical declarations. The selected benchmark still showed a
meaningful loss:

```text
simplearithmetic  asynkron_ms=640  jint_ms=165  Jint 3.88x faster
```

After the wrapper fix, three CPU profiles of:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

showed the intended expression-program workload. The remaining engine-owned
hotspot was not another public evaluation API boundary. It was repeated calls
from expression bytecode into generated `Math.sqrt` and `Math.pow` host
functions through the generic `IReadOnlyList<JsValue>` path, with
`CastHelpers.Box` under `InvokeCallableSingleArg` and `InvokeCallableTwoArgs`.

The accepted delivery added optional direct one-argument and two-argument
handlers to `HostFunction`, marked only the generated `Math.sqrt` and
`Math.pow` instances during `MathPrototype.ConfigurePrototype`, and taught
callable dispatch to try those handlers before allocating or boxing the generic
argument carrier.

Repeated focused final rows were noisy but clearly below the retained baseline:

```text
simplearithmetic  asynkron_ms=415
simplearithmetic  asynkron_ms=752
simplearithmetic  asynkron_ms=429
simplearithmetic  asynkron_ms=391
simplearithmetic  asynkron_ms=403
simplearithmetic  asynkron_ms=389
```

Focused Math/compliance tests passed, and the canonical `make quality` gate
passed in the orchestrator review flow before PR #2355 merged.

## Decision

Keep built-in Math host-function fast dispatch opt-in, marked, and
arity-specific.

The durable boundary is:

1. `HostFunction` may carry internal direct handlers for hot fixed-arity
   built-ins, but the default host-call contract remains the generic
   `IReadOnlyList<JsValue>` invocation path.
2. A fast handler must be stamped on the engine-owned generated host-function
   instance during prototype/global construction. Do not infer fast-dispatch
   eligibility from JavaScript-visible `name`, property path, or a callable
   found by ordinary lookup.
3. The handler signature must match the proven arity. `Math.sqrt` is a
   one-argument handler; `Math.pow` is a two-argument handler. Other arities,
   spread calls, unmarked host functions, wrappers, and user replacement
   functions stay on the generic path.
4. The direct handler must reuse the same semantic helper as the ordinary
   built-in body. For this slice, `Math.sqrt` still runs `JsOps.ToNumber` before
   `Math.Sqrt`, and `Math.pow` still runs `JsOps.MathPow` over
   `JsOps.ToNumber` operands.
5. Do not move this into a broad expression-bytecode special case or a generic
   host-call bypass. The profile identified generated Math host dispatch, not
   all host functions and not every expression-program call.

## Consequences

- Repeated expression calls to generated `Math.sqrt(value)` and
  `Math.pow(base, exponent)` avoid `SingleValueArgs` / `TwoValueArgs` boxing
  and the generic `IReadOnlyList<JsValue>` host boundary.
- User code that replaces `Math.sqrt` or `Math.pow` receives an ordinary
  callable with no fast marker, so observable property replacement remains on
  the existing fallback path.
- Future built-in host-call fast paths should use explicit engine-owned
  metadata or direct instance markers. They should not use function names,
  property paths, or benchmark identities as semantic proof.
- If a candidate built-in depends on `this`, `new.target`, argument count,
  active evaluation context, side-effect ordering beyond ordinary argument
  coercion, or replacement/wrapper observability, the simple fixed-arity
  handler shape is insufficient unless that state is carried explicitly and
  proven with focused tests.
- Performance claims for host-call dispatch still need current CPU ownership,
  repeated selected-profile rows, and focused semantic coverage. A cleaner call
  tree alone is not enough.

## Related

- `docs/performance/simplearithmetic-fast-math-host-dispatch.md`
- `docs/adrs/0216-keep-simplearithmetic-profiler-scope-comparable-before-runtime-retries.md`
- `docs/adrs/0116-keep-array-push-single-arg-host-call-shortcut-runtime-guarded.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `.claude/rules/host-function-observable-shape.md`
- `.claude/rules/performance-profiling-guardrails.md`
