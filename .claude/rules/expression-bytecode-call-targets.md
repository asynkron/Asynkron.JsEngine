# Expression Bytecode Call Targets

When changing expression bytecode call lowering or runtime invocation, keep
callee lookup, receiver binding, direct-eval classification, and eval-host realm
checks separate.

## Rules

1. Plain identifier calls must lower through an identifier-call-target operation
   when the runtime may need a reference base. The operation must push both the
   receiver and callee so the later `Call` opcode has one stable stack contract.
2. If an identifier call resolves through a `with` binding, use the binding
   object as `this` and read the callee through that captured binding. Do not
   collapse it into ordinary identifier lookup that loses the receiver.
3. Classify direct eval from syntax: a non-optional unqualified `eval(...)`
   identifier call is the direct-eval candidate. Do not use
   `this === undefined` or other receiver state as the directness signal.
4. At runtime, mark `EvalHostFunction.IsDirectCall` only when the call opcode is
   syntactically direct eval and the eval host belongs to the current engine.
   Cross-realm eval values must remain indirect.
5. When direct eval executes, validate `super` and `new.target` from the
   caller's lexical execution context, not from only the immediate function kind.
   Arrows can inherit valid method, derived-constructor, or class-field
   initializer context; ordinary no-home-object callers must still reject
   `super`.
6. Prove both structure and behavior before widening Test262: lowering should
   show `LoadIdentifierCallTarget` plus an explicit-this `Call`, runtime tests
   should cover with-resolved receivers, direct eval through `with`, method-arrow
   `super`, derived-constructor-arrow `super()` / `new.target`, and class-field
   initializer direct eval. The owning Test262 method group should pass.

## Why

Issue #775 / PR #955 fixed the `Expressions_call` Test262 failures for
`with-base-obj.js` and `eval-realm-indirect.js`. The expression bytecode path
lost the `with` object receiver for identifier calls and initially risked
coupling direct eval to receiver shape. The durable lesson is that ECMAScript
call references carry both a callee and a base value, while direct eval is a
syntactic distinction with an additional same-engine runtime guard.

Issue #773 / PR #951 then fixed the adjacent direct-eval caller-context trap.
Direct eval inside arrows and class-field initializer contexts still needs the
caller lexical `super` / `new.target` state; categorical arrow or no-method-frame
rejections reject valid ECMAScript contexts and miss the real home-object /
derived-constructor eligibility check.
