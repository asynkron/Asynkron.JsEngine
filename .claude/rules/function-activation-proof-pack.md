# Function Activation Proof Pack

Before changing function-call activation setup, run the named internal proof pack
for activation semantics:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests"
```

## Rules

1. Treat activation changes as semantically high-risk even when the goal is only
   overhead reduction. The proof pack must stay green before claiming the change
   preserves JavaScript behavior.
2. Keep the pack broad across activation families: sloppy and strict
   `arguments`, parameter aliasing, default/rest/destructured parameters, nested
   closures, direct eval, `with` / dynamic scope, strict vs sloppy `this`,
   generators, async functions, and async generators.
3. If activation work changes one of those semantics, update
   `ActivationSemanticsProofPackTests` in the same delivery so the named filter
   remains the focused confidence gate for future agents.
4. Do not replace this narrow internal proof with Test262-only evidence. Test262
   can widen confidence after the focused pack passes, but the named pack is the
   fast regression gate for this subsystem.
5. When optimizing lazy `arguments` creation, prove the observable-binding split
   explicitly: ordinary body `arguments`, parameter-default `arguments`, direct
   eval in the body, direct eval in parameter defaults, nested-arrow
   `arguments`, and nested-arrow direct eval. Arrow functions inherit the
   enclosing activation's `arguments`; nested non-arrow functions are the
   boundary.
6. When optimizing arity-specific sync calls, keep struct argument carriers on
   concrete generic paths until parameter binding consumes them. Do not pass
   `TwoValueArgs` or similar readonly struct lists through `IReadOnlyList`-typed
   hot helper parameters or locals, because that boxes the struct and reintroduces
   the allocation the optimization is trying to remove.

## Why

Issue `planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-8b2aee3a48`
and PR #1636 added `ActivationSemanticsProofPackTests` after the
function-call activation overhead plan identified many easy-to-break edge cases.
Ordinary functions and generator/async-generator activation paths are separate,
and mapped `arguments`, non-simple parameters, direct eval, `with`, mode
differences, and resumable functions can regress independently. Future
activation-overhead work needs one explicit, cheap proof gate before broader
quality or Test262 runs.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-p-3c8725f1f9`
and PR #1637 showed the lazy-arguments trap directly: an optimization that only
looks for syntactic `arguments` in the immediate body misses direct eval and
nested arrows, both of which can observe the enclosing function's binding. The
durable rule is to prove the observable-binding decision, not just allocation
avoidance.

Related ADR: `docs/adrs/0100-keep-observable-arguments-binding-eval-aware-through-arrows.md`.

Issue
`planitem-planmanual1779530433702731000-reduce-function-call-activation-overhead-s-f3dc144c31`
and PR #1657 showed the arity-carrier trap directly: after the simple sync
activation fast path landed, `functioncalls-lite --memory` still reported
`TwoValueArgs` / `EmptyValueArgs` helper allocations until the typed call path
preserved generic struct carriers through `SyncFunctionInvoker` and used
`Array.Empty<JsValue>()` for the runner placeholder. Future activation-overhead
work should prove both the activation proof pack and the allocation table for
helper carriers.

Related ADR:
`docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`.
