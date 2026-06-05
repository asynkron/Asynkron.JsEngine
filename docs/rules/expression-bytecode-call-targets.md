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
4a. If expression-program execution adds a direct-eval fast path, pass
    invocation-local eval state explicitly into `EvalHostFunction`: current
    `EvaluationContext`, current `JsEnvironment`, directness, and
    class-field-initializer state. Do not let a fast path's shared eval core
    read `CallingContext`, `CallingJsEnvironment`, `IsDirectCall`, or
    `InClassFieldInitializer` as hidden inputs on the engine-global eval host.
    Keep such shortcuts same-engine guarded and fall back for spread,
    multi-argument, cross-engine, or indirect shapes.
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
7. When expanding direct member-call bytecode support for static dot-access
   targets, normalize the member name at compile time and keep receiver binding
   proof separate from property-name proof. A call target such as `obj.method()`
   must preserve JavaScript `this` binding after the static-name shape is
   accepted; do not satisfy `UnsupportedDirectMemberCallPropertyName` by falling
   back to AST evaluation or by only proving non-call member access. Include
   focused lowering coverage and runtime receiver/`this` regression coverage for
   the accepted non-computed member shape.
8. When admitting optional calls (`?.`) into a bytecode call-target preparation
   dispatcher, encode the nullish short-circuit jump target directly in the
   opcode operand rather than through a separate jump instruction. The receiver-
   optional (`box?.read()`) and callee-optional (`box.read?.()`, `box[key]?.()`
   shapes have distinct expression-program trailing structures and must be detected
   by separate sub-predicates before the non-optional path runs. Keep the nullish
   check before argument evaluation (arguments are only evaluated when the call
   proceeds). Do not share the optional preparation opcode with non-optional
   member calls; the nullish-check and jump-target operand encoding must be
   self-contained and visible at the opcode level.
8a. For optional-start computed plain calls such as `a?.b[k](...)`, keep the
    first optional hop's nullish check before the computed key load, then use
    the ordinary computed call-target preparation only after the receiver object
    has been loaded. Treat spread arguments as a separate capability boundary:
    unless the slice explicitly proves spread-mask handling for this exact
    optional-start shape, decline `a?.b[k](...args)` instead of letting generic
    simple-argument checks admit it accidentally.
9. When adding a new `if`/`else-if` branch to a pattern-dispatch chain where a
   non-match means "not my responsibility," always close every new arm with
   `else { return false; }` (or equivalent silent-decline) before any
   catch-all error-reporting block. Leaving a catch-all in scope for all
   non-matching trailing shapes causes the dispatcher to report a hard failure
   for programs that belong to entirely different fast-path handlers, blocking
   all subsequent patterns and producing widespread false-positive failures
   (196 test failures in gh2689).
10. When widening the executable `CallInvocationBoundary` to a new ordinary-call
   family, remove descriptor-level `HasCallDependency` or script `FunctionCode`
   seam blockers only for shapes the boundary can already prove end-to-end.
   For direct eval specifically, admit only the syntactic `eval(<literal>)`
   identifier shape with exactly one non-spread, declaration-free literal
   argument. Identifier-loaded eval source is runtime text whose declaration set
   is not proven before VM execution, so it must stay on IR. Literal eval source
   containing top-level `var`, `let`, `const`, `function`, or `class`
   declarations can inject runtime bindings and must also stay on IR. Carry
   directness in boundary-local metadata instead of inferring it from
   receiver/dynamic lookup state, and keep the fast path same-engine guarded.
10a. If a direct-eval boundary fast path bypasses the generic host-call setup,
    resynchronize the unified-bytecode frame's slot array from the active slot
    environments before returning to the VM. Sloppy direct eval can mutate
    caller-visible bindings through the environment chain, so a fast path that
    skips the ordinary invocation handoff must still make those writes visible
    to later bytecode ops.
10b. When a call-boundary operand gains new packed bits, audit every compiler
    and VM encode/decode site that reuses the surface, including construct
    boundaries that share the helper. Non-call shapes must pass the new flag
    explicitly as off, and existing construct proofs should be rerun after the
    packing change.
11. When admitting `super.m()` / `super[k]()` into resumable unified bytecode,
    prove that the resumable activation owns the method environment used for
    super lookup. The VM must resolve the callee through
    `UnifiedBytecodeResumeState.CallingEnvironment`, preserve the derived
    receiver as `this`, and keep `SuperConstructInvocationBoundary` declined
    unless constructor-state ownership is separately proven for a legal
    resumable source shape.

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

Issue #2149 / PR #2156 optimized the same-engine, one-argument direct-eval path
for `activation-evalscope-lite` by adding an explicit
`EvalHostFunction.InvokeDirectSingleArgumentFast` entrypoint. Review found that
the shared eval core initially read class-field-initializer state from the
mutable host field instead of the explicit invocation parameter. The durable
lesson is that direct-eval call-path shortcuts may remove generic host-call
handoff, but they must keep caller context, caller environment, and class-field
state as explicit per-invocation inputs.

The plan-child issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-b08421d0b0`
and PR #1552 expanded direct member-call bytecode support for
`UnsupportedDirectMemberCallPropertyName`. The property-name gap was the same
literal-vs-identifier static-name normalization trap as ordinary member access,
but direct calls add a receiver hazard: accepting `obj.method()` must still
preserve `obj` as `this`. Future direct-call slices need both lowering proof and
runtime receiver proof, not a runtime AST fallback or only ordinary member-access
coverage.

Issue #2689 admitted optional calls (`box?.read()`, `box.read?.()`,
`box[key]?.()`) into unified bytecode production routing (ADR 0289). The
callee-optional pattern is detected by the trailing `Call, Jump, SwapTopTwo, Pop`
structure; the compiler skips those trailing ops by setting `callIndex =
OperationCount - 4` and emits `PrepareNamedOptionalCallTarget` /
`PrepareComputedOptionalCallTarget` with the jump target packed into the operand.
During delivery, adding the `lastOp.Kind == ExpressionOpKind.Pop` branch to
`TryAppendFirstBoundaryCallTargetPreparation` without an `else { return false; }`
guard left the catch-all error block in scope for every expression whose last op
was neither `Call` nor `Pop` (e.g. `Binary`, `Return`, `GetNamedProperty`). This
caused 196 test failures because `TryAppendExpressionProgramOps` received a
non-empty failure reason and could not fall through to the per-op dispatch loop
for those programs. The fix — `else { return false; }` for the unrecognized
trailing-op case — is now rule 9 above.

Issue #2828 / PR #2832 widened ADR 0301's optional call-chain route from named
optional-start forms to the computed plain-call form `a?.b[k](...)`. The
important distinction is evaluation order: the computed key must not execute
when `a` is nullish, while the eventual `PrepareComputedCallTarget` still has to
preserve `a.b` as the receiver. Review also caught that the new recognizer could
have admitted `a?.b[k](...args)` through the generic argument path; that spread
variant was outside the slice and now declines explicitly. Future optional-start
computed-call slices need to prove key skipping, receiver binding, and
shape-specific spread handling separately.

Issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-1707adc0f0`
/ PR #2863 retired the descriptor-level ordinary-call blocker for the proven
direct-eval invocation-boundary slice. The accepted shape was deliberately
narrow: syntactic one-argument non-spread direct eval only, compiled through
the existing dynamic identifier call-target preparation, tagged by a packed
direct-eval bit on `CallInvocationBoundary`, and executed through the same-engine
`EvalHostFunction.InvokeDirectSingleArgumentFast` path with caller slot
resynchronization afterward. Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-4e57b65e02`
/ PR #3230 narrowed this further to declaration-free literal eval source;
identifier-loaded runtime text can inject bindings whose names are unknown
before VM execution, so it must stay on IR. The delivery also hit
construct-boundary rebase
fallout because construct sites reuse the same operand-packing helper. The
durable lesson is that widening executable call families must move three
surfaces together — activation gating, boundary-local directness metadata, and
VM state write-back — while auditing any shared operand-packing helpers that
construct paths depend on.

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-b4a52d0bfe`
/ PR #3261 closed the D2 eval-injected runtime binding quarantine as a ratchet
slice. The existing eligibility code already declined declaration-bearing eval
literals, but the proof only pinned `var`. The learn-stage lesson is to keep
the direct-eval production boundary declaration-family complete: `var`, `let`,
`const`, function declarations, and class declarations are all runtime-binding
injection hazards for a VM frame compiled before eval declaration
instantiation. Future direct-eval production work should preserve that whole
negative set while keeping declaration-free literal eval route proof green.

Issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-43182f6ef4`
/ PR #3189 admitted resumable named and computed super member calls after
`yield` / `await`. The delivery had to create a resumable invocation environment
for method bodies, thread it through `UnifiedBytecodeResumeState.CallingEnvironment`,
and add matching `PrepareNamedSuperCallTarget` /
`PrepareComputedSuperCallTarget` handlers before widening eligibility. The
durable lesson is that super member calls are not just another call-target
opcode: they depend on both super lookup and receiver preservation across
suspension. Super construct stays out of this route because constructor-state
ownership was not proven by the legal generator/async method shapes in B12.

Related ADRs:

- `docs/adrs/0326-admit-resumable-super-member-calls-through-captured-method-environment.md`
- `docs/adrs/0338-keep-direct-eval-production-bytecode-literal-and-declaration-free.md`
