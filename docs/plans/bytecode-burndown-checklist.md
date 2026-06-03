# Full-Bytecode Burn-Down Checklist

The finite, closed list of work to reach **full bytecode execution** — every
non-dynamic JS construct running on the production unified-bytecode VM, with the
two interpreter fallback tiers (`ExpressionProgram` tier-1, `ExecutionPlanRunner`
tier-2 IR) retired.

Derived from an exhaustive 6-surface census (206 raw leaf items deduped to 112)
plus an adversarial grammar-completeness audit. Authoritative gate:
`src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
(`Evaluate` / `EvaluateScript` / `EvaluateResumable`).

> **Completeness status (from the adversarial audit): this list is currently a
> LOWER BOUND, not yet a proven ceiling.** Because the eligibility gate runs
> *after* the compiler lowers the AST, constructs that lowering erases (`switch`,
> `do-while`, `debugger`, sequence expr, BigInt, `static {}`, super-in-field-init,
> ordinary `new.target`, labeled non-loop break) have no named line — they are
> folded into admitted primitives or the `UnsupportedPlanShape` umbrella. **Phase 0
> below closes that gap and makes the count final.** Until Phase 0 is done, treat
> the totals as a floor.

**Definition of done** — all must hold simultaneously and be machine-checkable:
1. No decline code fires for any non-dynamic shape (only the §Dynamic-Residue set).
2. Sync `Execute` and `ExecuteResumable` admit the same non-dynamic surface (parity).
3. Corpus route census ≈100% of non-residue functions/scripts on production bytecode.
4. Both fallback tiers deleted/quarantined; only a dynamic-scope interpreter remains.
5. A standing CI gate fails if any `isDynamicResidue=false` decline ever fires.
6. The surviving interpreter handles exactly: direct `eval`, live `with`/awaited-with, eval-injected bindings, `Function`-constructor bodies. Nothing else.

---

## Dynamic Residue — terminal, NOT work (the only permanent fallbacks)

`eval(arg)` direct / multi-arg / spread · eval-injected runtime bindings ·
`with(obj)` dynamic-scope core (resumable + awaited; **sync non-awaited `with` is
admitted, not residue**) · `Function(...)`-produced body.
**NOT residue** (real work, do not mis-park): indirect eval `(0,eval)(s)`, free
*global* identifier reads/calls (admitted via materialized activation), sync
non-awaited `with`, the `Function` call boundary itself.

---

## Phase 0 — Make the list provably finite (do first; closes the audit gaps)

- [x] **P0.1** Grammar-coverage appendix → `docs/plans/bytecode-grammar-coverage.md` ✅ (covers switch+`let`, do-while, sequence, BigInt literal/arithmetic/`typeof`, `static {}`, super-in-instance/static-fields, `new.target`, labeled-block break — each mapped to its lowering owner + test anchor). **Surfaced 1 real new leaf → A52 (`debugger;`).**
- [ ] **P0.2** Enumerate the `UnsupportedPlanShape` compiler umbrellas (A51 / B47 / E2): promote each of the ~12 hidden `UnifiedBytecodeCompiler.TryCompile` reason strings to a named decline + checklist leaf.
- [ ] **P0.3** Diff `UnifiedBytecodeOpCode` enum vs the sync admit-switch (E3) and the two resumable allowlists; name every enum-but-not-admitted opcode as its own leaf.
- [ ] **P0.4** Decompose the coarse leaves: split **B24** (class expression) into per-member shapes (constructor, instance fields, static fields, static blocks, private fields, private methods, accessors, computed members, super-in-members) and **A35** into its 4 object-literal-member opcodes.
- [x] **P0.5** Delete the dead `LabelControlFlow` enum member + stale contract-doc rows (zero emission sites; labeled loop break/continue already admitted on sync). *(= old E1.)*

---

## Phase A — Synchronous admission surface (51 items, by decline code)

Status: ☐ declined · ◐ partial · ☑ admitted (parity work remains on other engine)

- [ ] **A1** Captured/dynamic activation: closure-captured locals / with-in-chain needing heap env — *CapturedOrDynamicActivation* — sync ◐ / res ☐ — `:418`
- [ ] **A2** Unresolved non-with dynamic activation chain — *CapturedOrDynamicActivation* — ☐/☐ — `:418`
- [x] **A3** `arguments` whole-object dependency (escape/pass/return/mutate) — *ArgumentsObjectDependency* — ☑/☐ — `:427` ✅ (Codex)
- [x] **A4** `arguments` as call target — *ArgumentsObjectDependency* — ☑/☐ — `:1172` ✅ (Codex)
- [x] **A5** `arguments` store / assignment-reference — *ArgumentsObjectDependency* — ☑/☐ — `:1340` ✅ (Codex)
- [ ] **A6** Arrow needing lexical-this / new.target environment (non-simple body) — *ArrowLexicalThisDependency* — ☐/n/a — `:455`
- [ ] **A7** Class constructor activation outside admitted param shapes — *ClassConstructorActivation* — ◐/n/a — `:462`
- [ ] **A8** Tail call returned from inside `finally` — *CallDependency* — ☐/n/a — `:498`
- [ ] **A9** Identifier call-target outside first invocation boundary — *CallDependency* — ◐/◐ — `:1204`
- [ ] **A10** Free identifier call target (`helper(x)`) — sync only; resumable admits — *DynamicLookupDependency* — ◐/☑ — `:1195`
- [ ] **A11** Complex call arguments outside admitted spans (`fn(a+g(b))`) — *CallInvocationBoundary* — ☐/◐ — `:1245`
- [ ] **A12** Member/computed call-target outside first boundary (`a.b().c()`) — *CallInvocationBoundary/CallDependency* — ◐/◐ — `:1240`
- [ ] **A13** Free identifier READ (sync only; resumable admits) — *DynamicLookupDependency* — ◐/☑ — `:1328`
- [ ] **A14** Free dynamic identifier STORE `freeVar = x` (both routes) — *DynamicLookupDependency* — ☐/☐ — `:1359`
- [ ] **A15** `typeof freeName` dynamic lookup — *DynamicLookupDependency* — ☐/☐ — `:1417`
- [ ] **A16** Computed-property delete key `delete box[freeName]` — *DynamicLookupDependency* — ☐/◐ — `:1632`
- [ ] **A17** Named property read past first boundary (`box.a.b.c.d`) — *PropertyReadBoundaryOutOfScope* — ◐/◐ — `:1652`
- [ ] **A18** Computed property read past first boundary (`box[a][b][c]`) — *PropertyReadBoundaryOutOfScope* — ◐/◐ — `:1785`
- [ ] **A19** Named/computed write past admitted (`box.a.b.c=v`, `a=b=v`) — *PropertyWriteDependency* — ◐/◐ — `:1812`
- [ ] **A20** Ternary/branching computed-key write `box.child[c?a:b]=v` — *PropertyWriteDependency* — ☐/☐ — `:1777`
- [ ] **A21** Compound/logical computed write w/ ternary key `box[c?a:b]+=v` — *PropertyWriteDependency* — ☐/☐ — `:1644`
- [ ] **A22** Identifier update on free name `freeName++` — *PropertyUpdateDependency* — ☐/☐ — `:1829`
- [ ] **A23** Update w/ computed receiver prefix `box[k1].child[k2]++` — *PropertyUpdateDependency* — ◐/◐ — `:1850`
- [ ] **A24** `delete freeName` — *DeleteDependency* — ☐/☐ — `:1861`
- [ ] **A25** Named property delete past admitted (`delete box.a.b.c`) — *DeleteDependency* — ◐/◐ — `:1888`
- [ ] **A26** Computed property delete past admitted (`delete box[k1][k2]`) — *DeleteDependency* — ◐/◐ — `:1918`
- [ ] **A27** `super.m()` / `super[k]()` call-target prep outside first boundary — *SuperPropertyDependency* — ◐/☐ — `:1302` — _(in progress: workflow A1-super)_
- [ ] **A28** Optional-chain named read beyond single hop `a?.b?.c` — *OptionalChainDependency* — ◐/◐ — `:1464`
- [ ] **A29** Optional-chain computed read beyond admitted `a?.[k]?.[j]` — *OptionalChainDependency* — ◐/◐ — `:1689`
- [ ] **A30** Optional member/computed call beyond admitted `a?.b?.c()`, `o?.[k]()` — *OptionalChainDependency* — ◐/◐ — `:1232`
- [ ] **A31** Optional short-circuit guard outside admitted spans — *OptionalChainDependency* — ☐/☐ — `:1992`
- [ ] **A32** Optional-chain delete chained `delete a?.b?.c` — *OptionalChainDependency* — ☐/☐ — `:1619`
- [ ] **A33** Array spread non-simple source `[...f().items]`, `[...gen()]` — *ObjectLiteralOrSpreadDependency* — ◐/◐ — `:2014` — _(in progress: workflow A3-objlit)_
- [ ] **A34** Object spread non-simple source `{...f()}` — *ObjectLiteralOrSpreadDependency* — ◐/◐ — `:2036` — _(in progress: workflow A3-objlit)_
- [ ] **A35** Computed key / method / accessor object literal outside simple span *(decompose → P0.4)* — *ObjectLiteralOrSpreadDependency* — ☐/☐ — `:2019` — _(in progress: workflow A3-objlit)_
- [ ] **A36** Private-field define in object literal `{#x:v}` — *PrivateFieldDependency* — ☐/☐ — `:2042`
- [ ] **A37** Private-named mutation outside admitted direct shapes — *PrivateFieldDependency* — ◐/◐ — `:1093`
- [x] **A38** for-in unsupported driver source (awaited / non-lowered) — *ForInDriverStateDependency* — ☑/☑ — `:524` ✅ (Codex)
- [x] **A39** Array destructuring unsupported driver — *DestructuringDependency* — ☑/☐ — `:531` ✅ (Codex)
- [x] **A40** Object destructuring unsupported driver — *DestructuringDependency* — ☑/☐ — `:544` ✅ (Codex)
- [ ] **A41** Slot-resolved identifier via dynamic-name reference op — *UnsupportedPlanShape* — ☐/☐ — `:1346`
- [ ] **A42** `using` / `await using` declaration *(split → P0.4)* — *UnsupportedPlanShape* — ☐/☐ — `:571`
- [ ] **A43** Descriptor-backed block-scoped function declaration (Annex B) — *UnsupportedPlanShape* — ☐/☐ — `:581`
- [ ] **A44** PushEnvironment for iterating / non-flat-slot lexical block (per-iter `let`) — *UnsupportedPlanShape* — ◐/☐ — `:599`
- [ ] **A45** with-depth analysis failure (unbalanced Enter/Leave, irreducible flow) — *UnsupportedPlanShape* — ☐/☐ — `:482`
- [ ] **A46** Non-production binary operator (`**`, BigInt-mixed, …) *(decompose)* — *UnsupportedPlanShape* — ◐/◐ — `:2061`
- [x] **A47** for-of unsupported iterator-init source — *UnsupportedPlanShape* — ☑/☑ — `:517` ✅ (Codex)
- [ ] **A48** Sync iterator driver: async iterator kind — *UnsupportedPlanShape* — ☐/☐ — `:2332`
- [ ] **A49** Plan with no ActivationSlots metadata — *UnsupportedPlanShape* — ☐/☐ — `:204`
- [ ] **A50** Default prototype-only opcode guard (drift backstop) *(→ P0.3)* — *UnsupportedPlanShape* — ☐/n/a — `:8243`
- [ ] **A51** Compiler `TryCompile` failure umbrella *(→ P0.2)* — *UnsupportedPlanShape* — ☐/☐ — `:230`
- [ ] **A52** `debugger;` statement — no AST/lowering owner exists (surfaced by P0.1 grammar appendix); needs a parser/AST node + no-op-or-decline owner *(new leaf from P0.1)* — ☐/☐

## Phase B — Resumable-VM parity + suspension machinery (47 items)

Gated by `TryFindUnsupportedResumableOpcode@895` (opcode allowlist) and
`IsSupportedResumableInstruction@846` (instruction allowlist). Most are
sync-admitted ☑ but resumable-declined ☐ purely because they're absent from
these allowlists — mechanical extensions against existing sync VM handlers.

- [ ] **B1** async / async-arrow ordinary body — extend await-body admission — sync n/a / res ◐
- [ ] **B2** generator body — extend yield* / remaining yield shapes — n/a/◐
- [ ] **B3** **async generator `async function*` — no EvaluateResumable call at all; whole body on tier-2 (largest single gap)** — n/a/☐ — `AsyncGeneratorInvoker.cs:45`
- [x] **B4** Property write `o.x=v` in resumable — ☑/☑ ✅ #3114
- [x] **B5** Computed property write `o[k]=v` — ☑/☑ ✅ #3114
- [x] **B6** Property update `o.x++` / `o.x+=v` — ☑/☑ ✅ #3117
- [x] **B7** Computed property update `o[k]++` — ☑/☑ ✅ #3117
- [x] **B8** Slot update `x++` / `x+=v` (UpdateSlot) — ☑/◐ ✅ #3115 (var/param admitted; lexical `let`/`const` slot updates declined for const-safety — see B8a). Also fixed the latent const **plain-assignment** gap `const x=1; x=2` in #3116.
- [ ] **B8a** *(follow-up, option a)* Thread a static const-slot bitmap from scope analysis → `ExecutionPlan`/`ActivationSlotShape` → `UnifiedBytecodeResumeState`, so the resumable VM can raise `TypeError: Assignment to constant variable` itself and restore `let`-write/`let`-update fast-path (currently `let`/`const` slot updates + assignments decline to the interpreter).
- [x] **B9** Property delete `delete o.x` — ☑/☑ ✅ #3117
- [x] **B10** Computed property delete `delete o[k]` — ☑/☑ ✅ #3117
- [ ] **B11** `new C(args)` construct — ☑/☐
- [ ] **B12** super call/construct — ☑/☐ — _(in progress: workflow A1-super, resumable side)_
- [ ] **B13** super property read `super.x` — ☑/☐ — _(workflow A1-super)_
- [ ] **B14** super property write/update — ☑/☐ — _(workflow A1-super)_
- [ ] **B15** Optional member/computed call `o?.m()` / `o?.[k]()` — ☑/☐
- [ ] **B16** Object literal `{a,b:v,...spread}` — ☑/☐ — _(workflow A3-objlit)_
- [ ] **B17** Array literal `[a,,b,...spread]` — ☑/☐ — _(workflow A3-objlit)_
- [ ] **B18** `#field in obj` — ☑/☐
- [ ] **B19** `new.target` (LoadNewTarget) — ☑/☐
- [ ] **B20** `import.meta` — ☑/☐
- [ ] **B21** Tagged-template / template object — ☑/☐
- [ ] **B22** Regex literal — ☑/☐
- [ ] **B23** Nested function literal — ☑/☐
- [ ] **B24** Class expression *(decompose → P0.4: ~8 member shapes)* — ☑/☐
- [ ] **B25** `typeof unresolvedFreeVar` — ☑/☐
- [ ] **B26** Dynamic free write `freeVar=v` — ☑/☐
- [ ] **B27** Dynamic free update `freeVar++` — ☑/☐
- [ ] **B28** `delete freeVar` — ☑/☐
- [ ] **B29** Dynamic reference plumbing (compound free-var ops) — ☑/☐
- [x] **B30** `for-of` sync driver across suspension — ☑/☑ ✅ (Codex)
- [x] **B31** `for-in` driver across suspension — ☑/☑ ✅ (Codex)
- [ ] **B32** try/catch/finally across suspension — ☑/☐
- [ ] **B33** `break`/`continue` across suspension (driver cleanup) — ☑/☐
- [ ] **B34** Array destructuring across suspension — ☑/☐
- [ ] **B35** Object destructuring across suspension — ☑/☐
- [ ] **B36** Nested function/class declaration hoisting in resumable body — ☑/☐
- [ ] **B37** Scaffolding opcodes (Tdz/EnsureHasName/ToString/ThrowReferenceError) in resumable — ☑/☐
- [ ] **B38** `yield* freeIter` over free/dynamic iterable — n/a/☐
- [ ] **B39** async `yield* asyncIterable` — n/a/☐
- [ ] **B40** `with(obj){}` in generator/async body (routing gap, not residue) — n/a/☐
- [ ] **B41** `for await (x of asyncIter)` async-iterator driver (declines both routes) — ☐/☐
- [x] **B42** `for(k in await p)` awaited for-in source — n/a/☑ ✅ (Codex)
- [ ] **B43** Awaited with-object `with(await x){}` — ☐/☐
- [ ] **B44** Awaited binding/destructuring decl `let [a]=await x` — ☐/☐
- [ ] **B45** Resumable instruction-allowlist default (master plan-level gap) *(→ P0.3)* — n/a/☐
- [ ] **B46** Resumable opcode-allowlist default (master opcode-level gap) *(→ P0.3)* — n/a/☐
- [ ] **B47** Resumable compiler `TryCompile` wrap *(→ P0.2)* — n/a/☐

## Phase C — Top-level / script route (3 items)

- [x] **C1** Script `typeof <ident>` reading block-scoped lexical (stale flat-slot liveness): `for(let i){}; typeof i` — *UnsupportedPlanShape* — ☑/n/a ✅ (Codex)
- [x] **C2** Script with no `ScriptCompletionSlot` — ☑/n/a ✅ (Codex)
- [ ] **C3** Script inheriting any per-shape decline (union gate; closes via A/B) — *UnsupportedPlanShape* — ☐/n/a

## Phase D — Dynamic quarantine gates (5 items)

- [ ] **D1** Direct-eval quarantine gate (multi-arg/spread + Call-op IsDirectEval → interpreter)
- [ ] **D2** eval-injected runtime binding quarantine
- [ ] **D3** `with` quarantine for resumable bodies + awaited with-object
- [ ] **D4** `Function(...)` produced-body quarantine (body recurses into gate)
- [ ] **D5** Standing CI gate: assert no `isDynamicResidue=false` decline ever fires on the corpus

## Phase E — Retire the fallback tiers (6 items)

- [x] **E1** *(moved to P0.5 — dead `LabelControlFlow` deletion)*
- [ ] **E2** Promote each wrapped `TryCompile` reason to a named decline *(= P0.2)*
- [ ] **E3** Diff opcode enum vs admit-switch; name every gap *(= P0.3)*
- [ ] **E4** Remove `ExpressionProgram` (tier-1) from hot path (after A/C admit its coverage)
- [ ] **E5** Remove `ExecutionPlanRunner` (tier-2 IR) from hot path (after A/B/C parity)
- [ ] **E6** Delete `AsyncGeneratorInvoker` unconditional IR construction; route via EvaluateResumable (depends on B3)

---

## Counts

| Phase | Items | Notes |
|---|---:|---|
| 0 — Make list finite | 5 | grammar appendix + umbrella enumeration; converts floor → ceiling |
| A — Sync admission | 51 | by decline code |
| B — Resumable parity + suspension | 47 | bulk of the work; mostly allowlist extensions |
| C — Script route | 3 | closes mostly via A/B |
| D — Dynamic quarantine | 5 | build the residue boundary |
| E — Retire tiers | 6 | E2/E3 = P0.2/P0.3 |
| **Total** | **~117** | floor until Phase 0 done; will grow as B24/A35/umbrellas decompose |

**Status (96 concrete A+B+C shape items):** Sync `Execute` 28 admitted / 30 partial / 31 declined. Resumable `ExecuteResumable` 5 admitted / 16 partial / 67 declined. **Resumable is the bulk of the remaining work; B3 (async generators) is the single largest gap.**

## Known soft spots
1. **`UnsupportedPlanShape` compiler umbrellas** (A51/B47/E2) hide ~12 distinct `TryCompile` reasons — true leaf count not yet enumerated (Phase 0 closes this).
2. **Resumable suspension machinery** (B30–B33, B41, B3/B39) — inventory is complete, but per-item cost is unbounded (persisting driver/try/finally/iterator state across resume); these may subdivide during implementation. Treat the Phase B count of 47 as a lower bound for effort.

---

_Status: 16 / ~119 complete (P0.1, P0.5, A3, A4, A5, A39, A40, B4, B5, B6, B7, B8, B9, B10, C1, C2). Plus correctness fix #3116. New leaves: A52 (`debugger`), B8a (const-bitmap follow-up). Updated as each item merges._
