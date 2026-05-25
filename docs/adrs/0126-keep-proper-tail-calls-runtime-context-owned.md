# ADR 0126: Keep proper tail calls runtime-context owned

## Status

Accepted

## Context

Issue #1748 / PR #1796 enabled the Test262 proper-tail-call coverage that had
previously failed or crashed after the compliance-gap tests were turned on. The
initial failure set mixed direct strict tail recursion, member calls, tagged
templates, expression wrappers, `try` / `finally`, and revoked proxy calls that
had to preserve another realm's `TypeError`.

The accepted delivery did not add an AST fallback for these cases. It kept the
IR and expression-bytecode path as the owner by adding a synchronous IR
trampoline for eligible functions and a narrower same-function tail-restart path
inside `ExecutionPlanRunner`. The review-back repair then fixed a latent strict
receiver bug: restarting the same function with new parameter slots was not
enough when the tail call was a member call such as `second.f(...)`; the strict
callee's existing `this` binding also had to be rebound before jumping back to
the plan entry point.

The issue also exposed nearby context hazards:

- expression-stack side state, including optional-chain short-circuit flags,
  must be reset when a trampoline frame is reused;
- tail evaluation must not bypass active scheduled `finally` cleanup;
- revoked proxies need the proxy's realm for the thrown TypeError, not whatever
  caller context happens to be current;
- tail-call proof has to include call-depth behavior and semantic receiver /
  realm / cleanup checks, not only a passing broad Test262 lane.

## Decision

Keep proper tail-call support owned by the runtime call boundary, not by a
legacy AST evaluation fallback or a broad source rewrite.

For synchronous IR functions, an eligible trampoline may reuse runtime frames
only when it can preserve the function activation shape, plan entry point,
arguments, `this`, expression-stack side state, and observable JavaScript
context. Unsupported or unsafe shapes should fall back to the existing ordinary
call path, not to a partial trampoline.

For same-function tail restarts, preserve the existing function environment but
treat each restart as a new call for the observable values that can change:

1. capture evaluated arguments before requesting the restart;
2. capture the explicit receiver for strict member calls;
3. run any scheduled cleanup before the restart is applied;
4. rebind strict `this` in both the function environment field and slot storage;
5. reset parameter slots and jump back to the execution-plan entry point.

Do not mark a return expression as tail-position while an active `finally`
frame still has to run. Tail-call optimization must preserve completion and
cleanup ordering first; stack reuse is only valid after the cleanup boundary is
respected.

Realm-sensitive throw paths remain owned by the object or callable that raises
the error. A revoked proxy call in tail position must still throw from the
proxy's realm, so tail-call routing must not replace that error with a caller
realm error.

## Consequences

- Proper-tail-call work can reduce recursive call-depth growth without moving
  function execution back to AST evaluation.
- Tail restarts become a semantic runtime feature, so future changes must prove
  receiver rebinding, realm identity, `finally` ordering, and expression-stack
  state in addition to call-depth stability.
- Eligibility checks should stay conservative. If a function has activation,
  statement, or expression shapes the trampoline cannot model, ordinary
  invocation is the safe fallback.
- Focused internal tests such as `TailCallTests` are the durable fast proof
  surface before widening to the owning Test262 filters.
- This ADR complements existing activation, call-target, and IR cleanup
  boundaries instead of replacing them: activation metadata still owns slot
  shape, expression bytecode still owns receiver/callee stack contracts, and IR
  cleanup still owns `try` / `finally` completion order.

## Related

- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/expression-bytecode-call-targets.md`
- `.claude/rules/ir-control-flow-cleanup.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
