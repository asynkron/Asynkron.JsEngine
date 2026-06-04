# Design: Nested-Scope Capture Resolution — Unblock A1 / A6 / A44 nested-scope admissions

Read-only design investigation for the `plan.HasOnlyRootFlatSlotMappings` guard that
caps three production-VM admission slices (A1 captured closures, A6 arrows, and — in a
parallel form — A44 PushEnvironment). **Headline finding: the miscompile hazard is
*name-collision-specific*, not "any nested scope". A narrow collision-only guard is sound
and is the cheapest unlock; it admits the large majority of nested-scope closures
immediately.** Companion to [`stage2-closure-activation-design.md`](stage2-closure-activation-design.md)
(precedent format) and [`bytecode-completion-plan.md`](bytecode-completion-plan.md).

Status legend: **[PROVEN]** = verified by reading the resolution code / running the
baseline suite; **[HYP]** = hypothesis, labelled, with the confirming probe a future
implementer must run.

---

## 1. Root cause — where a captured name gets a flat inner slot

### 1.1 The three guard sites (all the same predicate)
- **A1 closures** — `CanUseProductionUnifiedBytecodeCapturedClosureActivation`,
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs:3957`
  (final conjunct `plan.HasOnlyRootFlatSlotMappings`; commit `1c0b30675`).
- **A6 arrows** — `CanUseProductionUnifiedBytecodeArrowFunctionActivation`,
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs:3911`
  (commit `3591e8507`).
- **A44 PushEnvironment** — *not* the same predicate. A44 already admits non-root scopes
  when every per-iteration / block lexical slot resolves to a flat activation slot:
  `IsSupportedPushEnvironment`, `UnifiedBytecodeProductionEligibility.cs:2835-2868`
  (called from `:533`). A44 is therefore *ahead* of A1/A6 — it does not use
  `HasOnlyRootFlatSlotMappings` at all. See §4.3 for why A44's existing test is the wrong
  shield against the collision hazard.

### 1.2 What `HasOnlyRootFlatSlotMappings` means **[PROVEN]**
`ExecutionPlan.cs:60-61` → `ComputeHasOnlyRootFlatSlotMappings` (`:108-126`): true iff
`FlatSlotMappings` has entries **only for `RootScopeId`**. Any non-root scope key (i.e. any
nested `if`/loop/catch block that owns flat-slot bindings) flips it to `false`. It is a
blunt structural proxy: "this function body has no nested lexical scope with its own
slots." It declines `function inc(){ if(c){ let t=1; return t; } return 0; }` even though
`t` collides with nothing captured.

### 1.3 The actual conflation site **[PROVEN]**
The inner function's body is compiled by its **own** CFG-aware
`ExecutionPlanBuilder`/`SlotAssignmentRewriter` pass; the outer rewriter only *re-stamps*
already-resolved nested identifiers (guarded preserve at
`SlotAssignmentRewriter.cs:744` and `:805`:
`if (_isRestampingNestedFunction && identifier.ScopeId >= 0 && identifier.SlotIndex >= 0) return operation;`).
So the bug is in the **inner's own name resolution**, namely `TryResolve`:

```
src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs:1164
private bool TryResolve(Symbol symbol, out (int scopeId, int slotIndex) resolution)
{
    foreach (var scopeId in _scopeStack)                 // (a) lexically-correct walk
    {
        var candidate = ResolveInScope(symbol, scopeId);
        if (candidate.scopeId >= 0 && candidate.slotIndex >= 0) { resolution = candidate; return true; }
    }

    foreach (var scope in _scopes)                       // (b) UNSCOPED fallback — the bug
    {
        if (scope.Value.Slots.TryGetValue(symbol, out var slotIndex))
        {
            var mappedScope = RemapScopeId(scope.Key);
            resolution = (mappedScope, slotIndex);        // returns ANY scope's slot
            return true;
        }
    }
    resolution = default; return false;                  // (c) → stays a dynamic identifier
}
```

The lowering caller (`RewriteExpressionOp`, `:749-759`) then stamps the identifier with
`(ScopeId, SlotIndex, FlatSlotId = GetOrCreateFlatSlotId(...))` — i.e. a **flat slot read**
— whenever `TryResolve` returns true; otherwise the op keeps its unresolved form and the
compiler lowers it to a **dynamic-identifier op** (`LoadDynamicIdentifier` &c.,
`TypedAstEvaluator.SyncFunctionInvoker.cs:3308-3318`) that walks the live `JsEnvironment`
chain into the captured activation. That dynamic path is exactly what the IR runner uses,
and it is correct.

**The conflation:** for a captured enclosing name (e.g. `outer` / `baseValue`) read in the
inner's *outer* (function-root) scope:
- branch **(a)** fails — the name is not declared in any currently-active scope on the
  stack (the inner's root has no slot for the captured name);
- branch **(c)** is the *correct* outcome (no slot → dynamic op → walks to the enclosing
  binding);
- **but** if the inner ALSO has a nested block declaring `let outer` (a SHADOW), that
  block-local `outer` lives in `_scopes` (the inner's full scope set,
  `SlotAssignmentRewriter.cs:21`,`70`), so branch **(b)** finds it and returns the
  block-local's flat slot. The captured read is now wrongly stamped to read the
  (uninitialised / out-of-block) local slot → `undefined`/`NaN`. **This is the
  miscompile.**

The IR runner does not hit this because at *runtime* it resolves the same identifier
through the environment chain with proper block-scope entry/exit, so the outer-scope read
of `outer` finds the function-scope/captured binding and the inner-block `let outer` only
exists while its block environment is live. The production-VM flat-slot stamping has no
such temporal scoping — the slot id is baked at compile time.

### 1.4 Why the regression tests need this **[PROVEN]**
`tests/Asynkron.JsEngine.Tests/NestedFunctionScopeRegressionTests.cs`:
- `NestedFunctionCapturesOuterLetAfterReturn` (`:16`): inner reads captured `baseValue`;
  inner `if`-block declares `let baseValue = seed + 100` — **name collision**. Expect
  `10,110,10`.
- `ShadowingInsideInnerDoesNotCorruptOuterClosure` (`:40`): closure reads captured `outer`
  + `captured`; inner `if`-block declares `let outer = 99` — **name collision on `outer`,
  no collision on `captured`**. Expect `7,101,7`.

Both shapes are *exactly* the collision case. There is **no** regression test exhibiting a
non-colliding nested scope miscompiling — because there is no such miscompile (§2).
Baseline suite for both regression + `MultiStatementArrowAdmissionTests` is **green**
(9/9, verified).

---

## 2. Which nested-scope shapes are safe vs unsafe

| Shape | Captured read resolves to | Safe under flat-slot VM? |
|---|---|---|
| Nested block whose lexical name **does not** equal any captured name | dynamic op (branch (c)) | **SAFE [PROVEN by code]** |
| Nested block declaring `let/const N` where `N` **is** a captured enclosing name read outside that block | flat slot of the block-local (branch (b)) | **UNSAFE [PROVEN]** — the regression-test hazard |
| Nested block local `N` that is **never** also a captured name (pure inner local) | flat slot, correctly (it *is* a local) | **SAFE [PROVEN]** |
| Per-iteration `let` loop binding (A44) with no captured-name collision | flat slot via `IsSupportedPushEnvironment` | **SAFE [PROVEN]** — already admitted |
| Per-iteration `let` loop binding that **collides** with a captured name read in the loop's enclosing scope | branch (b) fallback can mis-stamp | **UNSAFE [HYP — see §5 probe]** |

**Conclusion [PROVEN for sync block case]:** the hazard is the *name collision between a
nested-scope lexical binding and a captured enclosing name that is also read in an outer
scope of the same inner function*. Any nested scope whose binding names are disjoint from
the captured-name set is safe. The `HasOnlyRootFlatSlotMappings` guard is therefore far
broader than the real hazard.

The captured-name set is recoverable from the compiled plan: it is exactly the set of names
carried by dynamic-identifier ops (`LoadDynamicIdentifier`/`StoreDynamicIdentifier`/
`UpdateDynamicIdentifier`/`TypeOfDynamicIdentifier`/`ResolveDynamicIdentifierReference`/…,
enumerated at `TypedAstEvaluator.SyncFunctionInvoker.cs:3308-3318`). The nested-scope
binding-name set is `plan.ScopeLexicalBindings` for every non-root scope id
(`ExecutionPlan.cs:35`,`51`) plus the slot symbols in `plan.FlatSlotMappings` non-root
keys.

---

## 3. The fix — options and recommendation

### Option A — Narrow the guard to *name-collision only* (RECOMMENDED, cheapest)
Replace `plan.HasOnlyRootFlatSlotMappings` (at `SyncFunctionInvoker.cs:3911` and `:3957`)
with a new predicate `plan.HasNoCapturedNameShadowedByNestedScope` (compute-once on the
plan, mirroring the existing cached `HasOnlyRootFlatSlotMappings` at `ExecutionPlan.cs:60`):

1. Collect `capturedNames` = names appearing in any dynamic-identifier op in
   `plan.Instructions` (these are precisely the names the VM resolves through the live env
   chain — i.e. the captured/free names).
2. Collect `nestedBoundNames` = union of binding names over all **non-root** scope ids in
   `plan.ScopeLexicalBindings` ∪ non-root `plan.FlatSlotMappings` slot symbols.
3. Admit iff `capturedNames ∩ nestedBoundNames == ∅` (and the existing root-flat case is
   the trivial subset where there are no non-root scopes at all).

- **Pros:** tiny blast radius (one new plan-cached predicate + two call-site swaps); no VM
  change, no compiler change, no new binding model; uses data the plan already carries;
  the regression tests still decline (they are collisions) so they stay green by
  construction; immediately unlocks every non-colliding nested-scope closure/arrow — the
  common case (a closure with an inner `if`/loop using locals that don't shadow a captured
  name).
- **Cons:** still declines the *colliding* shapes (a strict superset of today's admissions,
  so no regression; just not a full fix). Requires care that "captured names" is computed
  from the *post-lowering* instruction stream (dynamic ops), not a pre-pass, so it
  reflects what the VM actually does.
- **Rejected-within-A risk:** a name read dynamically for a *different* reason (e.g. a true
  free/global identifier) is also in `capturedNames`; intersecting with a colliding nested
  local would over-decline (conservative, still safe) but never *mis-admit*. Conservative
  is the correct failure direction for a guard.

### Option B — Compiler fix: never give a captured name a flat inner slot
Make the inner's resolution refuse branch (b) of `TryResolve` when the symbol is *also*
read in a scope where it is not declared (i.e. when it is genuinely captured at some use
site). Concretely: the unscoped fallback at `SlotAssignmentRewriter.cs:1176-1184` is the
defect; it should only fire for a name that is *not* captured. This is the **real fix** and
would let the colliding shapes route too.

- **Pros:** removes the hazard at the source; the production VM then matches the IR runner
  for all nested-scope shapes; Option A's guard becomes unnecessary (could be relaxed to
  admit everything once B lands).
- **Cons:** the fallback at `:1176` is load-bearing for legitimate cases (re-stamping where
  the scope stack is incomplete; that is *why* it exists). Changing it risks regressing
  currently-correct resolutions across the whole engine (IR runner shares this rewriter).
  Higher blast radius; needs the full golden-harness sweep. Per-use-site capture analysis
  (a name can be a local at one use and captured at another within the same inner) makes
  the predicate subtle.

### Option C — Scope-aware (block-temporal) flat-slot resolution in the VM
Give the production VM the same block-entry/exit slot lifetime the IR runner has, so a
captured read outside a shadowing block resolves correctly even with one flat slot per
name. **Rejected:** this re-introduces exactly the dynamic per-block environment model the
flat-slot fast path exists to avoid — it dissolves the fast path's reason to exist for no
correctness gain that Option B doesn't already deliver more cheaply.

### Recommendation
**Land Option A first (1-stage quick win), then Option B as the durable fix.** A is sound,
non-regressing, and unlocks the bulk of the population now. B is the principled
follow-up that retires the guard entirely; sequence it after A so the colliding shapes are
the *only* remaining declines and can be proven in isolation. Reject C.

---

## 4. Staged execution plan (proof obligations per stage)

Mirrors the `stage2-closure-activation-design.md` cadence (Stage 0 by-hand bounded;
slices follow). Every stage's proof set = **eligibility** (decline-code) + **correctness**
(value) + **route-hit** (`unified-bytecode-production-fast-path func=` log present/absent,
the harness used by `MultiStatementArrowAdmissionTests`/`ClosureCapturedActivationTests`,
`tests/.../MultiStatementArrowAdmissionTests.cs:19`,`:27-29`) + **NestedFunctionScope
RegressionTests stay green**.

- **Stage 0 (by-hand, BOUNDED) — Option A predicate.** Add
  `ExecutionPlan.HasNoCapturedNameShadowedByNestedScope` (compute-once, beside
  `HasOnlyRootFlatSlotMappings` at `ExecutionPlan.cs:60`). Swap it in at
  `SyncFunctionInvoker.cs:3911` (A6) and `:3957` (A1).
  - Proof: new `NonCollidingNestedScopeClosure_RoutesAndComputes` (inner `if` with a
    non-captured local → route-hit + correct value); `CollidingNestedScopeClosure_Declines`
    (the regression-test shape → `DoesNotContain` ProdLog, still correct via IR);
    both `NestedFunctionScopeRegressionTests` green; the existing
    `NestedScopeArrow_DeclaresLetInNestedBlock_DeclinesUnderGuard`
    (`MultiStatementArrowAdmissionTests.cs:88-103`) must be **re-pointed**: under Option A
    its `let y` does NOT collide with a captured name, so it should now *route* — update the
    assertion to `Contains` and keep a separate *colliding* case for the `DoesNotContain`
    arm.
- **Stage 1 (slice) — read-only non-colliding nested scope, multi-statement body.** Inner
  reads captured names; nested `if`/`for` block declares disjoint locals.
- **Stage 2 (slice) — captured writes / compound updates** (`n++`, `n+=1`, `n=v`) with a
  non-colliding nested block present.
- **Stage 3 (slice) — `const` in nested block + TDZ in nested block**, non-colliding;
  captured-`const` write → TypeError, captured-`let` read-before-init → ReferenceError flow
  unchanged (verify via the dynamic-identifier path, do not reimplement).
- **Stage 4 (slice) — loop per-iteration `let` capture (A44 overlap)**: non-colliding
  per-iteration binding admitted; colliding per-iteration binding declined (resolve the §5
  HYP probe here).
- **Stage 5 (by-hand) — Option B real fix.** Tighten the `TryResolve` fallback
  (`SlotAssignmentRewriter.cs:1176-1184`) so captured names never take branch (b). Then
  relax the Stage-0 predicate toward "admit colliding shapes too", proving each previously
  declined collision now routes AND computes correctly, with the full golden IR-vs-VM
  harness and the regression tests still green.

---

## 5. Risk / adversarial test matrix

A future implementer MUST cover (golden: IR runner result == production VM result == spec):

1. **Shadowing read** — captured `x`, inner block `let x`; read `x` before/after the block
   (the two regression shapes). Decline under A; route+correct under B.
2. **Shadowing write** — captured `x` mutated in outer inner-scope; inner block `let x`
   mutated independently; outer `x` must not observe the block write and vice-versa.
3. **Partial collision** — capture `{a,b}`, inner block shadows only `a`. `b` reads must
   still route as dynamic; `a` is the decline trigger (the `captured` vs `outer` split in
   `ShadowingInsideInnerDoesNotCorruptOuterClosure`).
4. **Multi-level nesting** — block inside block inside the inner; collision at the deepest
   level only; collision two levels up; verify the captured-name set is gathered across the
   whole instruction stream, not just top-level ops.
5. **Loop per-iteration `let` capture** — `for (let i...) { fns.push(()=>i); }` where `i`
   is captured by a grandchild; and the **[HYP] collision probe**: a per-iteration `let`
   whose name equals a name captured from *outside* the loop, read in the loop's enclosing
   scope — confirm whether `IsSupportedPushEnvironment` (`:2835`) + branch (b) mis-stamps;
   if so A44 needs the same collision guard.
6. **`const` in nested block** — non-colliding (admit) vs colliding with a captured const
   (decline under A); re-assignment of captured const → TypeError preserved.
7. **TDZ in nested block** — `{ return x; let x; }` style; captured `x` outside vs TDZ `x`
   inside; ReferenceError semantics must match the IR runner exactly.
8. **Mixed IR/VM routes** — a VM-routed inner with a nested non-colliding block capturing
   from an IR-routed outer (and vice-versa) must alias the same heap `JsEnvironment`
   binding — the dynamic-identifier ops resolve through the shared env chain
   (`SyncFunctionInvoker.cs:3308-3318`); test that the seam holds.
9. **Re-entrancy / permanent-decline cache** — the colliding shape sets
   `MarkProductionEligibilityPermanentDecline` (`ExecutionPlan.cs:95`); ensure a *second*
   distinct function with the same name set but no collision is not poisoned by the cache
   (the decline is plan-scoped, so it should be fine — assert it).

---

## 6. Honesty note
The narrow-guard quick win (Option A) **is sound** and is the fastest unlock: the
miscompile is provably collision-specific (the only flat-slot path for a captured name is
the unscoped `TryResolve` fallback at `SlotAssignmentRewriter.cs:1176-1184`, which fires
only when a nested scope declares the same name). Admitting non-colliding nested scopes
cannot hit that path. Ship A first; B retires the guard entirely.

_Read-only design. No source or tests modified._
