# Full-Bytecode Burn-Down Checklist

The finite, closed list of work to reach **full bytecode execution** — every
non-dynamic JS construct running on the production unified-bytecode VM, with the
fallback tiers (`ExpressionProgram` VM, `ExecutionPlanRunner` IR) retired.

**Why this list is closed:** the work is bounded by three finite things — the
21-value `UnifiedBytecodeProductionDeclineCode` enum, the JS grammar (which does
not grow), and exactly two fallback tiers + two VM engines (`Execute`,
`ExecuteResumable`).

**Definition of done:** every decline code is either never emitted for
non-dynamic code or emitted only for the dynamic residue; `Execute` and
`ExecuteResumable` cover the same non-dynamic surface; both fallback tiers are
deleted/quarantined; and a standing gate proves no non-dynamic shape reaches a
fallback.

**Terminal — NOT work (the dynamic residue, always keeps a fallback):**
`eval(...)`, `with (obj) {}`, the `Function(...)` constructor, and runtime
dynamic-scope resolution against those.

> Foundations already merged this session (resumable VM build-out): #3107
> value-tier reads, #3108 sync call dispatch, #3109 optional chains/calls +
> flag persistence, #3110 permute-flag hardening, #3111 free/dynamic identifier
> reads & calls in resumable bodies.

---

## Phase A — Synchronous VM: close the decline codes for non-dynamic JS

- [ ] **A1** Super property access — `super.x`, `super.x = v`, `super.x++`, `delete super.x`, `super[k]` *(SuperPropertyDependency)*
- [ ] **A2** Private members — `#field`, `#method()`, `obj.#x`, `#x in obj`, private accessors, static private *(PrivateFieldDependency)*
- [ ] **A3** Object literals & spread — `{a, ...b}`, computed keys, literal methods/getters/setters, array spread `[...a]` *(ObjectLiteralOrSpreadDependency)*
- [ ] **A4** Ternary/branching computed keys on writes/updates — `box.a[c ? x : y] = v` *(PropertyWriteDependency / PropertyUpdateDependency)*
- [ ] **A5** Remaining optional chains — `o?.[k]()`, `o?.a?.b?.()`, deep mixed forms *(OptionalChainDependency)*
- [ ] **A6** Free dynamic writes/updates — `globalX = v`, `globalX++`, compound/logical assign, reference forms *(DynamicLookupDependency)*
- [ ] **A7** `typeof`/`delete` of dynamic identifiers — `typeof freeVar`, `delete freeVar` *(DynamicLookupDependency)*
- [ ] **A8** `for-in` statement driver — `for (k in obj)` *(ForInDriverStateDependency)*
- [ ] **A9** Labeled break/continue across drivers — `outer: for(...) { continue outer }` *(LabelControlFlow)*
- [ ] **A10** Destructuring (full) — nested patterns, defaults, holes, rest, `let`/`const` script-scope, parameter destructuring *(DestructuringDependency)*
- [ ] **A11** Full `arguments` object — aliasing/mutation beyond bounded spans *(ArgumentsObjectDependency)*
- [ ] **A12** Lexical-`this` arrows outside the admitted route *(ArrowLexicalThisDependency)*
- [ ] **A13** Class constructor residue — runtime-dependent defaults, destructured constructor params *(ClassConstructorActivation)*
- [ ] **A14** Wider calls — complex receivers, complex computed-key callees, receiver-binding-sensitive families *(CallDependency / CallInvocationBoundary)*
- [ ] **A15** Captured/enclosing-scope activations — closures capturing an outer function's slots *(CapturedOrDynamicActivation)*
- [ ] **A16** `UnsupportedPlanShape` residue — catch-all; exact contents pending census

## Phase B — Resumable VM (generators/async): parity + suspension machinery

- [ ] **B1** Property writes/updates/deletes inside a resumable body
- [ ] **B2** Construct & super inside a resumable body — `new X()`, `super()`, `super.m()`
- [ ] **B3** Free dynamic writes in resumable — `n++` on a free binding
- [ ] **B4** Enclosing-function-scope closure captures in resumable (`ScopeId>=0` slot-resolved)
- [ ] **B5** `yield*` over a free/dynamic iterable — `yield* makeIterator()`
- [ ] **B6** `for-of` / `for-in` across a `yield`/`await`
- [ ] **B7** `try`/`catch`/`finally` across suspension
- [ ] **B8** Async iterators / `for await...of` / async-generator driver states
- [ ] **B9** Destructuring inside a resumable body

## Phase C — Top-level / script route (R7)

- [ ] **C1** `let`/`const` inside a `for`-loop block at script scope
- [ ] **C2** Broad script completion + abrupt completion for non-admitted shapes
- [ ] **C3** Module top-level execution

## Phase D — Dynamic quarantine

- [ ] **D1** Hard gates so only `eval`/`with`/`Function` can reach a fallback

## Phase E — Retire the fallback tiers (= done)

- [ ] **E1** Delete/quarantine the `ExpressionProgram` tier-1 VM
- [ ] **E2** Delete/quarantine the `ExecutionPlanRunner` tier-2 IR interpreter
- [ ] **E3** Shrink the legacy AST evaluator to a dynamic-residue-only interpreter
- [ ] **E4** Standing completeness gate — a test that fails if any non-dynamic shape reaches a fallback

---

_Status: 0 / 33 complete. Updated as each item merges._
