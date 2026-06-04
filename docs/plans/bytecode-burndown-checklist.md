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
> folded into admitted primitives or named compiler-decline leaves. **Phase 0
> below closes the remaining coarse gaps and makes the count final.** Until Phase
> 0 is done, treat the totals as a floor.

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
- [x] **P0.2** Enumerate the `UnsupportedPlanShape` compiler umbrellas (A51 / B47 / E2): promoted current `UnifiedBytecodeCompiler.TryCompile` reason templates into named owner leaves A51a-A51m plus B47a, with exact source-template drift coverage in `docs/unified-bytecode-expansion-contract.md`.
- [ ] **P0.3** Diff `UnifiedBytecodeOpCode` enum vs the sync admit-switch (E3) and the two resumable allowlists; name every enum-but-not-admitted opcode as its own leaf.
- [ ] **P0.4** Decompose the coarse leaves: split **B24** (class expression) into per-member shapes (constructor, instance fields, static fields, static blocks, private fields, private methods, accessors, computed members, super-in-members) and **A35** into its 4 object-literal-member opcodes.
- [x] **P0.5** Delete the dead `LabelControlFlow` enum member + stale contract-doc rows (zero emission sites; labeled loop break/continue already admitted on sync). *(= old E1.)*

---

## Phase A — Synchronous admission surface (64 items, by decline code / promoted compiler leaf)

Status: ☐ declined · ◐ partial · ☑ admitted (parity work remains on other engine)

- [ ] **A1** Captured/dynamic activation: closure-captured locals / with-in-chain needing heap env — *CapturedOrDynamicActivation* — sync ◐ / res ☐ — `:418` — **Stage 0 done** (commit 1c0b30675): FLAT multi-statement captured closures (counters/accumulators/captured reads+writes, e.g. `function inc(){ n++; return n; }`) now route through the production VM — captured locals lower to dynamic-identifier ops over the threaded closure env. Bounded by `HasOnlyRootFlatSlotMappings`: NESTED-lexical-scope closures still decline (a nested block's `let`/`const` can shadow a captured name → flat-slot miscompile; `NestedFunctionScopeRegressionTests`). Foundation prerequisite landed first: sync-VM `const` reassignment enforcement (commit d556f5d08) — function/block `const` slots were never marked const, so own- and captured-`const` writes silently succeeded; now they throw TypeError. Remaining A1 slices: `with`-in-chain residue (A2). **Resumable parity (Stage 5) LANDED** (commit 71be17015): generators/async capturing an enclosing local route through `ExecuteResumable` — captured READ + UPDATE (`n++`) alias the same enclosing heap slot across suspension (`UpdateDynamicIdentifier` on the resumable allowlist, 1:1 with the switch); captured-`const` write → TypeError; captured plain/compound STORE still declines (ResolveDynamicIdentifierReference not threaded through resume state). **Nested-scope unlock landed** (commit 76ebd019d): the `HasOnlyRootFlatSlotMappings` guard was replaced by `HasNoCapturedNameShadowedByNestedScope` — NON-colliding nested-scope closures (loops, catch, nested const/TDZ, multi-level) route. **Option B durable fix landed** (commit cebb626c6): `SlotAssignmentRewriter.TryResolve` no longer mis-stamps a captured name to a shadowing nested-block flat slot, so COLLIDING nested-scope closures now route AND compute correctly too. Sync captured closures fully admitted (flat + nested + colliding).
- [ ] **A2** Unresolved non-with dynamic activation chain — *CapturedOrDynamicActivation* — ☐/☐ — `:418`
- [x] **A3** `arguments` whole-object dependency (escape/pass/return/mutate) — *ArgumentsObjectDependency* — ☑/☐ — `:427` ✅ (Codex)
- [x] **A4** `arguments` as call target — *ArgumentsObjectDependency* — ☑/☐ — `:1172` ✅ (Codex)
- [x] **A5** `arguments` store / assignment-reference — *ArgumentsObjectDependency* — ☑/☐ — `:1340` ✅ (Codex)
- [x] **A6** Arrow needing lexical-this / new.target environment (non-simple body) — *ArrowLexicalThisDependency* — ◐/n/a — `:455` ✅ (commit 3591e8507): FLAT multi-statement arrow bodies now route through the production VM (lexical `this`/`new.target` threaded before VM entry, body-shape-agnostic). Mirrors closure Stage 0: lifted the arrow path off `SimpleReturnProgram`-only. **Nested-scope unlock landed** (commit 76ebd019d): now bounded by `HasNoCapturedNameShadowedByNestedScope` — NON-colliding nested-scope arrows route. **Option B durable fix landed** (commit cebb626c6): `SlotAssignmentRewriter.TryResolve` no longer mis-stamps a captured name to a shadowing nested-block flat slot, so COLLIDING nested-scope arrows now route AND compute correctly too. Fully admitted for flat + nested + colliding shapes.
- [ ] **A7** Class constructor activation outside admitted param shapes — *ClassConstructorActivation* — ◐/n/a — `:462` — ◐ (commit 57369e286): BASE-class ctor with a general multi-statement body (incl. nested-lexical-scope — `this`-stores resolve through the receiver, no captured-slot hazard) confirmed already admitted (gates on `CanUseSimpleIrActivationPlanShape`, never `SimpleReturnProgram`) and PINNED with 11 tests + 4 ratchet rows. **Major advance** (commit 830236be0 + test update): the widened complex property-write RHS removed the body-shape blocker, so **derived ctors with `super(...)` AND instance-field-initializer ctors now route + compute correctly** (verified: super w/ complex args, multi/call-init/field-refs-field fields, field init order, `this`-before-`super` TDZ→ReferenceError; `SuperFieldConstructorAdmissionTests`). **Remaining (declined):** super-PROPERTY access `super.m()` (A27), private-name ctors (PrivateFieldDependency).
- [ ] **A8** Tail call returned from inside `finally` — *CallDependency* — ☐/n/a — `:498`
- [x] **A9** Identifier call-target outside first invocation boundary — *CallDependency* — ☑/◐ — `:1204` ✅ (commit 26bc64504): sequential identifier calls (`a(); return b();`) route once the dynamic call-target path is enabled; eval order preserved.
- [x] **A10** Free identifier call target (`helper(x)`) — sync only; resumable admits — *DynamicLookupDependency* — ☑/☑ — `:1195` ✅ (commit 26bc64504): free/global call targets route via `HasOrdinaryDynamicCallTargetDependency` (dynamic-name path, mirroring resumable). `this` = undefined receiver, unbound → ReferenceError. **TCO boundary:** STRICT tail-position `return <identifier-call>` is DECLINED (kept on the TCO-capable IR runner) since the production VM has no tail-call optimization — prevents StackOverflow on strict self-recursion (`ContainsTailPositionIdentifierCallReturn`). Non-strict tail calls + statement/argument calls stay admitted. (The per-iteration-const-continue decline guard that this originally added was REMOVED in commit ddad73e6a — the underlying VM TDZ bug was fixed via `MirrorDynamicLexicalToFlatSlot`, so the shape now routes through production correctly instead of declining.)
- [x] **A11** Complex call arguments outside admitted spans (`fn(a+g(b))`) — *CallInvocationBoundary* — ☑/◐ — `:1245` ✅ (commit ee4506026): general operand-stack region walker (eligibility `TryValidateAdmittedComplexCallArgumentRegion` + compiler `TryAppendAdmittedComplexCallArgumentRegion`) admits args built from any already-admitted value op — nested calls `g(h(x))`, call-in-binary `g(a+h(b))`, member/computed-member call args. Left-to-right eval order proven. Assignment args, spread, out-of-vocabulary ops correctly still decline (region rolls back).
- [ ] **A12** Member/computed call-target outside first boundary (`a.b().c()`) — *CallInvocationBoundary/CallDependency* — ◐/◐ — `:1240`
- [x] **A13** Free identifier READ (sync only; resumable admits) — *DynamicLookupDependency* — ☑/☑ — `:1328` ✅ (commit af1be8ecc): already admitted via `allowsOrdinaryDynamicIdentifiers` → `LoadDynamicIdentifier`; locked with adversarial tests (global value; undeclared → ReferenceError).
- [x] **A14** Free dynamic identifier STORE `freeVar = x` (both routes) — *DynamicLookupDependency* — ☑/☑ — `:1359` ✅ (commit af1be8ecc): admitted; `StoreDynamicIdentifier` creates a configurable sloppy global, strict unresolvable → ReferenceError. Both verified.
- [x] **A15** `typeof freeName` dynamic lookup — *DynamicLookupDependency* — ☑/☑ — `:1417` ✅ (commit af1be8ecc): `TypeOfDynamicIdentifier` → "undefined" for unbound (never throws), correct type for bound.
- [x] **A16** Computed-property delete key `delete box[freeName]` — *DynamicLookupDependency* — ☑/◐ — `:1632` ✅ (commit af1be8ecc): free key lowers via `LoadDynamicIdentifier`; first-boundary computed-delete candidate accepts.
- [x] **A17** Named property read past first boundary (`box.a.b.c.d`) — *PropertyReadBoundaryOutOfScope* — ◐/◐ — `:1652` ✅ #3154
- [x] **A18** Computed property read past first boundary (`box[a][b][c]`) — *PropertyReadBoundaryOutOfScope* — ◐/◐ — `:1785` ✅ #3154
- [x] **A19** Named/computed write past admitted (`box.a.b.c=v`, `a=b=v`) — *PropertyWriteDependency* — ◐/◐ — `:1812` ✅ #3160
- [x] **A20** Ternary/branching computed-key write `box.child[c?a:b]=v` (+ update/delete) — *PropertyWriteDependency* — ☑/◐ ✅ #3121
- [x] **A21** Compound/logical computed write w/ ternary key `box[c?a:b]+=v`, `a&&b`, `a??b` — *PropertyWriteDependency* — ☑/◐ ✅ #3121
- [x] **A22** Identifier update on free name `freeName++` — *PropertyUpdateDependency* — ☑/☑ — `:1829` ✅ (commit af1be8ecc): `UpdateDynamicIdentifier` returns old/new and mutates the global; verified.
- [x] **A23** Update w/ computed receiver prefix `box[k1].child[k2]++` — *PropertyUpdateDependency* — ◐/◐ — `:1850` ✅ #3168
- [x] **A24** `delete freeName` — *DeleteDependency* — ☑/☑ — `:1861` ✅ (commit af1be8ecc): `DeleteDynamicIdentifier` — correct false/true per configurability, unresolvable-ref → true.
- [x] **A25** Named property delete past admitted (`delete box.a.b.c`) — *DeleteDependency* — ◐/◐ — `:1888` ✅ #3155
- [x] **A26** Computed property delete past admitted (`delete box[k1][k2]`) — *DeleteDependency* — ◐/◐ — `:1918` ✅ #3155
- [ ] **A27** `super.m()` / `super[k]()` call-target prep outside first boundary — *SuperPropertyDependency* — ◐/☐ — `:1302` — _(in progress: workflow A1-super)_
- [x] **A28** Optional-chain named read beyond single hop `a?.b?.c` — *OptionalChainDependency* — ◐/◐ — `:1464` ✅ #3161
- [x] **A29** Optional-chain computed read beyond admitted `a?.[k]?.[j]` — *OptionalChainDependency* — ◐/◐ — `:1689` ✅ #3162
- [x] **A30** Optional member/computed call beyond admitted `a?.b?.c()`, `o?.[k]()` — *OptionalChainDependency* — ◐/◐ — `:1232` ✅ #3165
- [ ] **A31** Optional short-circuit guard outside admitted spans — *OptionalChainDependency* — ☐/☐ — `:1992` — note (audit a0a7078): multi-hop `o?.a?.b` read + short-circuit already routes + correct, PINNED (`AlreadyRoutingShapePinTests`). The exact "outside admitted spans" decline target still needs an exact repro before check-off.
- [ ] **A32** Optional-chain delete chained `delete a?.b?.c` — *OptionalChainDependency* — ☐/☐ — `:1619` — ⚠️ FOUNDATION: ExpressionProgram IR is lossy for optional-chain deletes (terminal-hop optionality not encoded; `delete a?.b?.c` == `delete a?.b.c` in bytecode). Needs an IR-lowering change preserving terminal-hop optionality (broad blast radius, both interpreter + VM). Not a sync-route slice.
- [x] **A33** Array spread non-simple source `[...f().items]`, `[...gen()]` — *ObjectLiteralOrSpreadDependency* — ◐/◐ — `:2014` ✅ #3166
- [x] **A34** Object spread non-simple source `{...f()}` — *ObjectLiteralOrSpreadDependency* — ◐/◐ — `:2036` ✅ #3167
- [ ] **A35** Computed key / method / accessor object literal outside simple span *(decompose → P0.4)* — *ObjectLiteralOrSpreadDependency* — ☐/☐ — `:2019` — note (audit a0a7078): the BASIC computed-key (`{[k]:1}`), shorthand method (`{m(){}}`), and getter (`{get a(){}}`) object literals already route + correct, PINNED (`AlreadyRoutingShapePinTests`). The "outside simple span" complex cases (decompose → P0.4) still need work before check-off.
- [x] **A36** Private-field define in object literal `{#x:v}` — *PrivateFieldDependency* — n/a — `:2042` ✅ (commit 45aea8402): RESOLVED — `{#x:v}` is not a valid shape; the parser previously (wrongly) accepted it as a property named `"#x"`. Now `ParseObjectPropertyKey` throws a SyntaxError (private names are class-body-only), matching spec. Nothing to admit; the decline path is unreachable for valid input.
- [ ] **A37** Private-named mutation outside admitted direct shapes — *PrivateFieldDependency* — ◐/◐ — `:1093`
- [x] **A38** for-in unsupported driver source (awaited / non-lowered) — *ForInDriverStateDependency* — ☑/☑ — `:524` ✅ (Codex)
- [x] **A39** Array destructuring unsupported driver — *DestructuringDependency* — ☑/☐ — `:531` ✅ (Codex)
- [x] **A40** Object destructuring unsupported driver — *DestructuringDependency* — ☑/☐ — `:544` ✅ (Codex)
- [ ] **A41** Slot-resolved identifier via dynamic-name reference op — *UnsupportedPlanShape* — ☐/☐ — `:1346`
- [ ] **A42** `using` / `await using` declaration *(split → P0.4)* — *UnsupportedPlanShape* — ☐/☐ — `:571` — note: bytecode ADMISSION still pending (declines to interpreter/IR). Separately, a real disposal correctness bug was fixed (commit 5e14e701): function-body-scope `using` never fired `[Symbol.dispose]` because the function environment is never popped via `PopEnvironment`; `RunSync`/`ExecuteAsyncStep` now dispose on completion+throw (LIFO, SuppressedError preserved). Known residual: `await using` async-dispose promise isn't awaited before resolution (pre-existing engine-wide limitation).
- [ ] **A43** Descriptor-backed block-scoped function declaration (Annex B) — *UnsupportedPlanShape* — ☐/☐ — `:581` — ⚠️ INVESTIGATED-DECLINE (commit 798c946c3, comment-only doc at the gate): the `DeclareFunction` opcode already ports the sloppy B.3.3 dual-hoist, but the block's lexical function-name slot has NO flat-slot mapping (bound by Symbol, read via the var/dynamic path), so `IsSupportedPushEnvironment` (`:2980`) declines. Empirically relaxing it admits sloppy cases BUT breaks strict-mode block scoping (`StrictModeBlockFunctionScopingTests` — strict block fn leaks to function scope). Clean admission needs a block-environment flat-slot change affecting ALL lexical block bindings (let/const too) — a foundation change, NOT an Annex-B slice. Correct via IR runner today.
- [ ] **A44** PushEnvironment for iterating / non-flat-slot lexical block (per-iter `let`) — *UnsupportedPlanShape* — ◐/☐ — `:599`
- [ ] **A45** with-depth analysis failure (unbalanced Enter/Leave, irreducible flow) — *UnsupportedPlanShape* — ☐/☐ — `:482`
- [ ] **A46** Non-production binary operator (`**`, BigInt-mixed, …) *(decompose)* — *UnsupportedPlanShape* — ◐/◐ — `:2061` — ◐ (commit 9058879): `**` on Numbers confirmed admitted (`IsProductionBinaryOperator` + `JsOps.Exp`) and PINNED with 12 routing tests + 4 ratchet rows; right-assoc/`**=`/precedence/NaN edges verified. Also fixed a pre-existing parser bug: unparenthesized prefix-unary base before `**` (e.g. `-2**2`) now correctly throws SyntaxError. **Remaining:** BigInt operator production-routing (`2n**10n` computes correctly but routing tier not pinned; BigINt-mixed correctly throws TypeError).
- [x] **A47** for-of unsupported iterator-init source — *UnsupportedPlanShape* — ☑/☑ — `:517` ✅ (Codex)
- [ ] **A48** Sync iterator driver: async iterator kind — *UnsupportedPlanShape* — ☐/☐ — `:2332`
- [ ] **A49** Plan with no ActivationSlots metadata — *UnsupportedPlanShape* — ☐/☐ — `:204` — note (audit a0a7078): trivial no-ActivationSlots plans (top-level `1+1;` script route, empty `function f(){}`) already route + correct, PINNED (`AlreadyRoutingShapePinTests`). Confirm the `:204` decline arm is unreachable for valid shapes before check-off.
- [ ] **A50** Default prototype-only opcode guard (drift backstop) *(→ P0.3)* — *UnsupportedPlanShape* — ☐/n/a — `:8243`
- [x] **A51** Compiler `TryCompile` failure umbrella *(decomposed by P0.2)* — *UnsupportedPlanShape* — see A51a-A51m and B47a below.
- [ ] **A51a** Compiler entrypoint, invalid target, loop-shaped topology, and unsupported breakable/loop control — owner: statement/control-flow lowering; fallback route: existing execution-plan runner; sync/resumable: both.
- [ ] **A51b** Activation-slot metadata, slot-layout, and unsupported declaration / assignment / update / storage targets — owner: slot-layout and flat-storage lowering; fallback route: existing execution-plan runner; sync/resumable: both.
- [ ] **A51c** Catch binding, lexical dynamic declaration, active-with dynamic-name, and TDZ-head binding storage gaps — owner: scope/environment lowering; fallback route: existing environment-aware execution-plan runner; sync/resumable: both.
- [ ] **A51d** Iterator, for-in, `yield*`, resume-target, and driver state-slot gaps — owner: iterator/resume-state lowering; fallback route: existing iterator/generator/async IR drivers; sync/resumable: both, with `yield*` and resume-target failures resumable-owned.
- [ ] **A51e** Array/object destructuring state-slot and target gaps — owner: destructuring driver lowering; fallback route: existing destructuring IR helpers; sync/resumable: both.
- [ ] **A51f** General expression-loop unsupported op, binding-target expression, dynamic identifier, `arguments`, and private-neighbor gaps — owner: expression-program lowering; fallback route: expression-program / execution-plan runner; sync/resumable: both.
- [ ] **A51g** Call-target preparation, direct-eval boundary, member/super/private call-target, and invocation-boundary shape gaps — owner: call-boundary lowering; fallback route: existing call/eval/super IR paths; sync/resumable: both, excluding dynamic residue rows D1/D4.
- [ ] **A51h** Array/object/template literal, spread source, computed object key, and simple literal-span shape gaps — owner: literal/span lowering; fallback route: existing expression-program literal/spread evaluation; sync/resumable: both.
- [ ] **A51i** Computed/optional/private property-read and receiver-boundary shape gaps — owner: property-read lowering; fallback route: existing expression-program property evaluation; sync/resumable: both.
- [ ] **A51j** Property write, compound/logical write, update, delete, name-inference, key-span, and RHS-span gaps — owner: property mutation lowering; fallback route: existing expression-program mutation evaluation; sync/resumable: both.
- [ ] **A51k** Simple binary/unary/control/conditional operand span gaps — owner: expression-span lowering; fallback route: existing expression-program evaluation; sync/resumable: both.
- [ ] **A51l** Catch/try/driver cleanup topology diagnostics not otherwise captured by concrete driver rows — owner: statement diagnostics and control-flow reconstruction; fallback route: existing execution-plan runner; sync/resumable: both.
- [ ] **A51m** Measured property-read span rollback diagnostics — owner: measured span helpers; fallback route: existing property-read expression evaluation; sync/resumable: both.
- [x] **A52** `debugger;` statement — *(new leaf from P0.1)* — ☑/☑ — ✅ (commit d01faf31f): was not a keyword — lexed as Identifier and threw `ReferenceError` at runtime. Now a reserved word (`TokenType.Debugger`) lowered to the already-owned `EmptyStatement` no-op (admitted on every path; no new opcode/eligibility change). Routes through both sync-function and script fast paths. Reserved-word rejection (`var debugger`) + property-name usage (`o.debugger`) preserved.

## Phase B — Resumable-VM parity + suspension machinery (48 items)

Gated by `TryFindUnsupportedResumableOpcode@895` (opcode allowlist) and
`IsSupportedResumableInstruction@846` (instruction allowlist). Most are
sync-admitted ☑ but resumable-declined ☐ purely because they're absent from
these allowlists — mechanical extensions against existing sync VM handlers.

- [ ] **B1** async / async-arrow ordinary body — extend await-body admission — sync n/a / res ◐
- [ ] **B2** generator body — extend yield* / remaining yield shapes — n/a/◐ — note (audit a5b0c09): `yield*` over a LOCAL/param iterable already routes + correct, PINNED (`ResumableAlreadyRoutingPinTests`); `YieldStar`/`Iterator*` opcodes already allowlisted. **Remaining = free/dynamic iterable == B38.**
- [x] **B3** **async generator `async function*` — first EvaluateResumable route modeled** — n/a/◐ ✅ #3135 (simple-parameter direct-yield bodies can route through `UnifiedBytecodeVirtualMachine.ExecuteResumable`; async-generator `yield*` / `yield* await ...` delegation remains an explicit pre-VM decline on the IR runner).
- [x] **B4** Property write `o.x=v` in resumable — ☑/☑ ✅ #3114
- [x] **B5** Computed property write `o[k]=v` — ☑/☑ ✅ #3114
- [x] **B6** Property update `o.x++` / `o.x+=v` — ☑/☑ ✅ #3117
- [x] **B7** Computed property update `o[k]++` — ☑/☑ ✅ #3117
- [x] **B8** Slot update `x++` / `x+=v` (UpdateSlot) — ☑/◐ ✅ #3115 (var/param admitted; lexical `let`/`const` slot updates declined for const-safety — see B8a). Also fixed the latent const **plain-assignment** gap `const x=1; x=2` in #3116.
- [ ] **B8a** *(follow-up, option a)* Thread a static const-slot bitmap from scope analysis → `ExecutionPlan`/`ActivationSlotShape` → `UnifiedBytecodeResumeState`, so the resumable VM can raise `TypeError: Assignment to constant variable` itself and restore `let`-write/`let`-update fast-path (currently `let`/`const` slot updates + assignments decline to the interpreter).
- [x] **B9** Property delete `delete o.x` — ☑/☑ ✅ #3117
- [x] **B10** Computed property delete `delete o[k]` — ☑/☑ ✅ #3117
- [x] **B11** `new C(args)` construct — ☑/☐ ✅ #3152
- [ ] **B12** super call/construct — ☑/☐ — _(in progress: workflow A1-super, resumable side)_
- [ ] **B13** super property read `super.x` — ☑/☐ — _(workflow A1-super)_
- [ ] **B14** super property write/update — ☑/☐ — _(workflow A1-super)_
- [x] **B15** Optional member/computed call `o?.m()` / `o?.[k]()` — ☑/☑ ✅ #3159
- [x] **B16** Object literal `{a,b:v,...spread}` — ☑/☑ ✅ #3151
- [x] **B17** Array literal `[a,,b,...spread]` — ☑/☑ ✅ #3151
- [x] **B18** `#field in obj` — ☑/☑ ✅ #3153
- [x] **B19** `new.target` (LoadNewTarget) — ☑/☑ ✅ (Codex; leak fixed #3150 — per-activation new.target threaded onto the resume state: undefined for ordinary generators/async, inherited for async arrows)
- [ ] **B20** `import.meta` — ☑/☐
- [ ] **B21** Tagged-template / template object — ☑/☐
- [x] **B22** Regex literal — ☑/☑ ✅ #3118
- [ ] **B23** Nested function literal — ☑/☐
- [ ] **B24** Class expression *(decompose → P0.4: ~8 member shapes)* — ☑/☐
- [x] **B25** `typeof unresolvedFreeVar` — ☑/☑ ✅ #3163
- [ ] **B26** Dynamic free write `freeVar=v` — ☑/☐
- [ ] **B27** Dynamic free update `freeVar++` — ☑/☐
- [ ] **B28** `delete freeVar` — ☑/☐
- [ ] **B29** Dynamic reference plumbing (compound free-var ops) — ☑/☐
- [x] **B30** `for-of` sync driver across suspension — ☑/☑ ✅ (Codex; #3123 hardened the guard: suspending/nested try-finally correctly declines, restoring 13 generator try/finally tests)
- [x] **B31** `for-in` driver across suspension — ☑/☑ ✅ (Codex)
- [ ] **B32** try/catch/finally across suspension — ☑/◐ — note (audit a5b0c09): try/**finally** with yield-in-try already routes + correct (empty + non-empty finally runs), PINNED (`ResumableAlreadyRoutingPinTests`). **Remaining = try/CATCH across suspension only.**
- [ ] **B33** `break`/`continue` across suspension (driver cleanup) — ☑/☐
- [x] **B34** Array destructuring across suspension — ☑/☑ ✅ (Codex)
- [x] **B35** Object destructuring across suspension — ☑/☑ ✅ (Codex)
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
- [x] **B47** Resumable compiler `TryCompile` wrap *(decomposed by P0.2)* — n/a/see A51a-A51m plus B47a.
- [ ] **B47a** Resumable-only compiler declines for `yield*` state slots and synthetic resume targets — owner: resumable resume-state layout; fallback route: existing generator/async execution-plan route; sync/resumable: n/a/☐.

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
- [x] **E2** Promote each wrapped `TryCompile` reason to a named decline *(= P0.2; A51/B47 decomposed and drift-guarded)*
- [ ] **E3** Diff opcode enum vs admit-switch; name every gap *(= P0.3)*
- [ ] **E4** Remove `ExpressionProgram` (tier-1) from hot path (after A/C admit its coverage)
- [ ] **E5** Remove `ExecutionPlanRunner` (tier-2 IR) from hot path (after A/B/C parity)
- [ ] **E6** Delete remaining `AsyncGeneratorInvoker` IR fallback after the async-generator route widens past the current #3135 direct-yield boundary.

---

## Counts

| Phase | Items | Notes |
|---|---:|---|
| 0 — Make list finite | 5 | grammar appendix + umbrella enumeration; converts floor → ceiling |
| A — Sync admission | 64 | by decline code / promoted compiler leaf |
| B — Resumable parity + suspension | 48 | bulk of the work; mostly allowlist extensions |
| C — Script route | 3 | closes mostly via A/B |
| D — Dynamic quarantine | 5 | build the residue boundary |
| E — Retire tiers | 6 | E2/E3 = P0.2/P0.3 |
| **Total** | **~131** | floor until remaining Phase 0 decomposition is done; P0.2 promoted A51/B47 compiler leaves |

**Status (110 concrete A+B+C shape items):** Sync `Execute` 29 admitted / 30 partial / 43 declined. Resumable `ExecuteResumable` 6 admitted / 16 partial / 68 declined. **Resumable is the bulk of the remaining work; async-generator delegation, driver state, and fallback-tier retirement remain significant gaps.**

## Known soft spots
1. **Named compiler-decline leaves** (A51a-A51m/B47a) are now source-inventoried in the expansion contract; future `TryCompile` reason drift must update that contract and the focused source gate.
2. **Resumable suspension machinery** (B30–B33, B41, B3/B39) — inventory is complete, but per-item cost is unbounded (persisting driver/try/finally/iterator state across resume); these may subdivide during implementation. Treat the Phase B count of 48 as a lower bound for effort.

---

_Status: 50 / ~131 complete (Stage 1 batch 2: A30,A33,A34,A23 via #3165-#3168; A32 blocked on IR optional-delete lowering). Plus correctness fix #3116. New leaves: A51a-A51m/B47a compiler declines, A52 (`debugger`), B8a (const-bitmap follow-up). Updated as each item merges._
