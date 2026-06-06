# ADR 0351: Keep retained with closure environments dynamic residue

## Status

Accepted

## Context

Faktorial issue
`planitem-planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndo-8acd1c43ab`
and delivery PR #3314 refined the A1/A2 unified-bytecode burndown boundary for
closures whose enclosing environment chain retains a `with` object.

The repo already admits non-awaited `with` bodies through production unified
bytecode when the VM owns the active current environment. ADR 0269 keeps that
route activation-hoist and receiver-owned. ADR 0344 keeps resumable `with`
outside production bytecode until dynamic-scope suspension state is VM-owned.
This delivery handled the remaining sync closure question: a function created
inside `with (o) { ... }` can outlive the active `with` statement and retain the
with-object environment in its closure chain.

That retained environment is not the same ownership model as an active
current-environment `with` body. If a closure calls a name supplied by the
retained with object, the call target must preserve that with object as
`this`. Routing the closure through the production VM without explicit
closure-retained with-environment ownership would risk either reading through
the wrong environment or losing the receiver-sensitive call semantics.

## Decision

Keep closure-retained live `with` environments out of production unified
bytecode until the VM explicitly owns retained with-object environment state and
receiver-sensitive lookup for that state.

- Sync non-awaited `with` bodies may still route through the VM-owned
  current-environment lane described by ADR 0269.
- A closure whose enclosing chain retains a live `with` object must decline the
  production route and run through the existing dynamic environment path.
- Receiver-sensitive retained-with calls are part of the boundary. A closure
  such as `function g(){ return finish(); }`, where `finish` is supplied by the
  retained with object, must preserve that object as `this`.
- Do not classify retained live-`with` closures as ordinary captured-closure
  support, and do not "fix" the no-route signal by adding AST fallback,
  expression-program callback, or generic dynamic-name widening inside the VM.
- Future admission work must model the retained with object in the closure
  environment, resolve dynamic reads and call targets through that retained
  environment, and prove receiver behavior before routing.

## Consequences

- A1 captured-closure support remains admitted for flat, nested, and colliding
  lexical captured locals, but retained live-`with` closure environments stay
  outside that admitted set.
- A2's dynamic activation boundary now names retained live-`with` closure
  chains as precise dynamic residue instead of leaving them as an ambiguous
  captured-activation gap.
- Future agents should pair retained-with no-route checks with correctness
  assertions, including a receiver-sensitive call case, so a broad admission
  attempt cannot silently miscompile `this`.

## Evidence

- Delivery PR #3314 merged as commit `53b0657ee`.
- The carried delivery commit was `84de7aebb`.
- The delivery changed:
  - `docs/plans/bytecode-burndown-checklist.md`
  - `tests/Asynkron.JsEngine.Tests/ClosureCapturedActivationTests.cs`
- Focused tests added:
  - `ClosureWithRetainedWithObjectRead_DeclinesProductionRoute_ButRunsCorrectly`
  - `ClosureWithRetainedWithObjectReceiverCall_DeclinesProductionRoute_ButRunsCorrectly`
- Focused verification recorded by the build stage:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ClosureCapturedActivationTests"`
  - `rtk git diff --check`
- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so the learn pass
  used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":351}`.

## Related

- ADR 0269: `docs/adrs/0269-keep-with-backed-unified-bytecode-dynamic-names-activation-hoist-and-receiver-owned.md`
- ADR 0344: `docs/adrs/0344-keep-resumable-with-terminal-dynamic-residue.md`
- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
