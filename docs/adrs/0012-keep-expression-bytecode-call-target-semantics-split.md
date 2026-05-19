# ADR 0012: Keep expression bytecode call-target semantics split

## Status

Accepted

## Context

Issue #775 fixed the Test262 `Expressions_call` failures for
`language/expressions/call/with-base-obj.js` and
`language/expressions/call/eval-realm-indirect.js`.

The expression bytecode call path had treated plain identifier calls as just a
callee value. That lost the ECMAScript reference base for identifiers resolved
through a `with` object, so `with (scope) { read() }` did not call `read` with
the `with` binding object as `this`. A first direct-eval repair then exposed a
related trap: `with ({ eval }) { eval(...) }` is still syntactically direct
eval, even though the runtime receiver is no longer `undefined`.

The slow AST call path already kept these semantics separate. The bytecode path
needed to do the same without falling back to AST evaluation.

## Decision

Keep call-target resolution, direct-eval classification, and eval-host realm
guards as separate expression bytecode concerns.

Identifier call lowering emits an identifier-call-target operation that pushes
both receiver and callee. If the identifier resolves through a `with` binding,
the receiver is the binding object and the callee is read through that captured
binding. Otherwise the receiver is `undefined` and the callee comes from normal
identifier resolution. The following `Call` operation treats that receiver as
explicit `this` because the stack now carries both values.

Direct eval remains a syntactic property of a non-optional unqualified
`eval(...)` identifier call. It must not be inferred from the receiver value.
At runtime, an `EvalHostFunction` is marked direct only when the call opcode is
syntactically direct eval and the eval host belongs to the current engine. This
keeps cross-realm eval indirect while preserving direct eval through `with`
bindings in the same engine.

## Consequences

- Future expression bytecode call work must inspect the target-lowering opcode,
  the `Call` stack contract, and the eval host directness guard together.
- Do not repair call receiver bugs by disabling expression bytecode lowering or
  by making direct eval depend on `this === undefined`.
- Focused regression coverage should include ordinary identifier calls,
  optional identifier calls, with-resolved identifier receivers, direct eval
  through `with`, and the owning Test262 `Expressions_call` group before
  widening proof runs.
