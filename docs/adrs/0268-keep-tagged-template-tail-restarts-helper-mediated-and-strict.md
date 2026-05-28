# ADR 0268: Keep tagged-template tail restarts helper-mediated and strict

## Status

Accepted

## Context

Issue #2566 / PR #2580 fixed a Test262 regression batch where strict
proper-tail-call rows crashed again across direct calls and tagged-template
call/member forms. The affected rows included
`language/expressions/call/tco-call-args.js`,
`language/expressions/tagged-template/tco-call.js`, and
`language/expressions/tagged-template/tco-member.js`.

The common owner was not template-object identity or a need to route the tagged
template back through AST evaluation. The failing shapes reached the sync IR
same-function restart path through call-target syntax that can hide the actual
self callee behind a helper call. For example:

```js
return getF()`${n - 1}`;
```

This first executes a helper returning the current function and then performs
the final tagged-template call. The existing
`SyncIrCallTrampoline` could model direct self calls and explicit receivers,
but it could not safely step through that helper-mediated target shape.

A broad repair would be unsafe: arbitrary call results are observable and can
change the callee, receiver, realm-sensitive errors, or activation capture
state. Treating any non-final helper call as a self callee would turn ordinary
call results into frame reuse decisions.

## Decision

Keep helper-mediated tagged-template tail restarts owned by
`SyncIrCallTrampoline`, with explicit proof that the helper only returns the
current function.

The trampoline may collapse a non-final helper call into a same-function callee
only when all of these guards hold:

1. the caller is a synchronous, non-generator function already eligible for the
   sync IR trampoline;
2. the helper is a zero-argument synchronous, non-generator function whose only
   statement returns the current function identifier;
3. the helper name is not shadowed by a parameter slot and is found as either a
   local function declaration or a closure binding whose current value is the
   proven helper;
4. the helper call has an explicit receiver shape, no spread, and no arguments,
   and it appears before the final tail call in a return-purpose expression
   program;
5. the final call still targets the same `SyncFunctionInvoker`; explicit
   receiver forms keep their receiver, while no-explicit-receiver self calls
   remain strict-only and restart with `undefined` as `this`.

Function declarations that define these helpers are part of the trampoline's
executable setup shape, so the compatibility scan and runner must handle them
instead of rejecting the entire plan. Tagged-template syntax is then treated as
another call-target shape at the runtime call boundary, not as a separate AST
fallback or template-cache special case.

If any guard fails, keep ordinary invocation. Future widening must first extend
the trampoline executor semantics and then relax eligibility to match the new
executable shape.

## Consequences

- Strict direct tagged-template self calls and helper-mediated tagged-template
  self calls can remain stack-stable through the sync IR restart path.
- The runtime does not infer tail-restart eligibility from arbitrary call
  results, preserving conservative proper-tail-call semantics.
- Tagged-template template-object cache identity remains owned by the template
  descriptor/cache boundary; this ADR only covers call-target restart
  eligibility.
- Future TCO residual work should classify helper-mediated call targets
  separately from direct self identifiers, member receivers, dictionary
  rebinding, proxy realm rows, and template-object cache identity.
- Focused proof for this class should include internal `TailCallTests` for
  direct, local-helper, and closure-helper tagged-template self calls plus the
  owning Release Test262 filter for the listed #2566 rows.

## Related

- `.claude/rules/proper-tail-calls.md`
- `.claude/rules/expression-bytecode-call-targets.md`
- `.claude/rules/ecmascript-template-object-cache.md`
- `docs/adrs/0126-keep-proper-tail-calls-runtime-context-owned.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
- `docs/adrs/0144-keep-dictionary-tail-restarts-strict-and-simple.md`
- `docs/adrs/0197-keep-tail-call-smoke-depths-guard-sized.md`
