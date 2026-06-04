# Block-Environment Foundation — Per-Iteration Copy Design (A44 / A43 / B23)

Branch: `codex/foundation-block-env` (off `e2f789699`)
Status: **DESIGN DOC** (investigated; no production code shipped — the sound change is
cross-cutting on the shared `PushEnvironment` path and is gated by the STOP clause).

## Executive summary

Three independently-declined burn-down items were reported to share one root: a
block-scoped lexical binding "has no flat-slot mapping, so `IsSupportedPushEnvironment`
declines and forcing the dynamic path throws spurious TDZ." This investigation **refines
that diagnosis** with file:line evidence and a working prototype:

- The production VM **already** has a full heap-environment model for captured/shadowed
  block `let` bindings. Shadowed-`let` if-blocks captured by closures **already route**
  through the production VM today (proven green by
  `NestedFunctionScopeRegressionTests`, which asserts `routedFunc`). So "block bindings
  lack flat slots" is **not** universally true.
- The missing-flat-slot symptom is real but **narrow**: it occurs only for the
  **per-iteration loop-head scope** of a *captured* `for (let …)` / `for (const … of …)`.
  That scope's binding is only ever *captured* (read through a closure's env chain), so
  `SlotAssignmentRewriter` never assigns it a flat-slot id (flat ids are allocated lazily
  on the first own-scope identifier read/write). Eligibility then declines at the
  `PushEnvironment` for that scope.
- **Backfilling the missing flat-slot mapping is necessary but not sufficient.** A
  prototype that eagerly allocates flat slots for every `PushEnvironment` lexical binding
  makes the function eligible and route — and it then throws the **same spurious TDZ**
  the A44 ledger predicted. The deeper, true root cause is:

  > The production VM's `PushEnvironment` handler **wipes** per-iteration lexical slots to
  > `Uninitialized` (TDZ) and never performs the spec's `CreatePerIterationEnvironment`
  > **value copy** from the previous binding into the fresh per-iteration environment. The
  > compiled `UnifiedBytecodeScopeDescriptor` carries no per-iteration copy information, so
  > the handler physically *cannot* copy.

So the foundation work is not "give block scopes flat slots" — it is "teach the production
`PushEnvironment` path to honor `PerIterationBindings` (copy-from-previous) instead of only
wiping to TDZ." That is a change to a path shared by the IR runner lowering and the
production VM, hence the design-doc deliverable.

## Root cause with file:line evidence

### The two per-iteration scopes
A captured `for (let i=0;i<3;i++){ if(i===1){ g=()=>i; } }` lowers to **two** lexical scopes
that both name `i` (verified by dumping the compiled `ExecutionPlan`):

- **Loop-head scope** (e.g. scopeId `1000006`): created around the leading `let i=0`
  initializer. `PushEnvironment ScopeId=1000006 SlotMap{i=slot0}`.
- **Per-iteration scope** (e.g. scopeId `1000005`): the fresh per-iteration copy, pushed
  after the body and before the increment. `PushEnvironment ScopeId=1000005 SlotMap{i=slot0}`,
  carrying `PerIterationBindings=[i]`.

The non-captured shape (`for (let i…){ s+=i; }`) **elides** the loop-head scope to a single
iteration scope (`CanElideNonCapturingForLoopScope`,
`src/Asynkron.JsEngine/Execution/Emitters/LoopEmitter.cs:28`) and therefore routes today.

### Why the loop-head scope has no flat-slot mapping
`SlotAssignmentRewriter` assigns flat-slot ids lazily, only when an identifier read/write
*resolves* to a `(scopeId, slotIndex)`:

- `GetOrCreateFlatSlotId` — `src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs:1109`
  (allocated from `RewriteAssignment`/identifier rewrite paths, ~`:1131`).
- `RewriteInstruction`'s `PushEnvironmentInstruction` arm
  (`SlotAssignmentRewriter.cs:247`) records the scope's `SlotMap`/`LexicalSlotIndices` but
  **does not** allocate flat-slot ids for the bindings it introduces.

All reads of `i` (inside the body and the captured arrow) resolve to the **per-iteration**
scope `1000005`, so only `1000005` gets a flat id. The loop-head `1000006` only ever has `i`
*written* by its initializer (which resolves elsewhere), so `1000006` is **absent from**
`BuildFlatSlotMappings()` (`SlotAssignmentRewriter.cs:49`).

### Why eligibility declines
`IsSupportedPushEnvironment`
(`src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs:3229`)
requires `flatSlotMappings.TryGetValue(instruction.ScopeId, …)` to succeed for every
lexical scope. For scope `1000006` it misses → returns false → the `TryFindPlanDecline`
gate at `:617` declines with `UnsupportedPlanShape` ("Only non-iterating lexical block
environments with flat slot mappings are eligible…"). Verified: dumping
`UnifiedBytecodeProductionEligibility.Evaluate(plan, …)` for the captured shape returns
`ELIGIBLE=False CODE=UnsupportedPlanShape`.

### Why relaxing the gate alone throws spurious TDZ
`RemapSlotIndices` (`UnifiedBytecodeCompiler.cs:670`) **drops** unmapped lexical slots, so
without a mapping the loop-head `PushEnvironment` would compile to an empty environment.

Prototype (eager flat-slot allocation in the `PushEnvironment` rewrite arm) gives head and
iteration *distinct* flat slots (`1000006.i→flat0`, `1000005.i→flat1`) and makes the
function `ELIGIBLE=True`. Running it then **throws** at runtime:

```
ReferenceError: Cannot access 'i' before initialization
```

(reproduced via `A44PerIterationLetDeclineTests.PerIterLet_CapturedClosure_*`). Trace:

1. `PushEnvironment` VM handler
   (`src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs:2138`)
   sets `slots[lexicalSlotIndices[i]] = JsValue.Uninitialized` for every lexical slot and
   creates a fresh `scopeEnvironment` with `SetSlotsLexicalUninitialized`
   (`CreateScopeEnvironment`, `:6375`).
2. The per-iteration `PushEnvironment 1000005` wipes `flat1` to `Uninitialized`. Nothing
   copies the loop-head value (`flat0`) into `flat1`.
3. The per-iteration read of `i` (its own scope's `flat1`) is `Uninitialized` → TDZ throw.

### Why the VM cannot fix this without new data
The compiled scope descriptor `UnifiedBytecodeScopeDescriptor`
(`src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs:162`) carries
only `ScopeId`, `LexicalSlotIndices`, `ConstSlotIndices`. It does **not** carry
`PerIterationBindings` nor any "copy source flat slot." The instruction-level
`PushEnvironmentInstruction.PerIterationBindings`
(`Instructions.cs:289`, documented "symbols that need copying from previous iteration") is
**discarded** when the compiler emits the descriptor (`UnifiedBytecodeCompiler.cs:1537`–`1564`).
The IR runner performs the spec's `CreatePerIterationEnvironment` copy; the production VM
has no equivalent.

### The machinery that already works (so the fix is local)
The VM keeps each flat slot's bound heap environment in `slotEnvironments[]` and rebinds it
on every `PushEnvironment` (`UnifiedBytecodeVirtualMachine.cs:2163`–`2190`). Every flat-slot
store calls `SyncSlotEnvironment` (`:780`, `:838`, `:847`; body at `:6291`
`binding.Environment.SetSlotDirect`), so a closure that captured a per-iteration env sees
mutations correctly and `PopEnvironment` restores prior owners
(`RestoreSlotEnvironmentOwners`, `:6429`). **The only missing piece is the per-iteration
copy at push time.**

## Recommended change (minimal, gated to per-iteration scopes)

Add a per-iteration **copy** to the production push path, gated so ordinary block scopes
keep their TDZ wipe untouched.

1. **Compiler — carry copy info.** Extend `UnifiedBytecodeScopeDescriptor` with an optional
   `PerIterationCopySlotIndices : ImmutableArray<int>` (the flat slots that must be
   *copied-from-current* instead of wiped). Populate it in the `PushEnvironmentInstruction`
   arm (`UnifiedBytecodeCompiler.cs:1537`) from `pushEnvironment.PerIterationBindings`
   mapped through `slotLayout.FlatSlotMappings`.

2. **Slot rewriter — allocate the head scope's flat slot and SHARE it with the iteration
   scope.** In the `PushEnvironmentInstruction` rewrite arm
   (`SlotAssignmentRewriter.cs:247`) eagerly allocate a flat-slot id for each lexical
   binding (new helper `EnsurePushEnvironmentFlatSlots`, prototyped). For per-iteration
   sibling scopes (loop-head + iteration that share a binding name), reuse **one** flat slot
   for that name across both scopes — the VM rebinds the single flat-slot storage to the
   current scope env on each push and keeps the captured env objects distinct, so freshness
   is preserved while the value survives the push.

3. **VM — copy instead of wipe for per-iteration slots.** In the `PushEnvironment` handler
   (`UnifiedBytecodeVirtualMachine.cs:2138`), for slots listed in
   `PerIterationCopySlotIndices`, snapshot the current flat-slot value *before* creating the
   new scope env, then write that value into both the fresh env slot and the flat slot
   (initialized, not `Uninitialized`). Non-listed slots keep the existing TDZ wipe.

4. **Eligibility — admit per-iteration scopes with a copy mapping.** Relax
   `IsSupportedPushEnvironment` (`UnifiedBytecodeProductionEligibility.cs:3229`) so a scope
   whose bindings all map to flat slots is admitted (the eager allocation from step 2
   guarantees the mapping). Keep the existing all-slots-mapped requirement. Flip the A44
   tripwire (`A44PerIterationLetDeclineTests`) to assert routing for the captured shapes.

## Blast radius

- **Shared `PushEnvironment` semantics** (IR-runner lowering + production VM): HIGH. Both
  the captured *and* non-captured loop shapes, plus every ordinary block scope, flow through
  the same handler. The copy must be strictly gated to per-iteration slots or it corrupts
  TDZ for ordinary `let` blocks.
- **Flat-slot allocation order**: eager allocation in the rewriter changes flat-slot id
  assignment for any function containing a `PushEnvironment`, including currently-routing
  shapes (e.g. `NestedFunctionScopeRegressionTests`). Slot *count* grows; ids shift. Must
  re-verify those stay green and produce identical values.
- **Regression guards that must stay green**: `NestedFunctionScopeRegressionTests`
  (shadowed-`let` capture, already routes), `StrictModeBlockFunctionScopingTests`
  (strict block-fn scoping — the A43 tripwire), `TailCallTests` (40/40).

## Staged plan + what each stage unlocks

- **Stage 1 — Per-iteration copy in the production VM (A44).** Steps 1–4 above. Unlocks
  **A44** (captured per-iteration `let`/`const`, incl. the multi-capture
  `for (const x of …)` variant). Highest value; self-contained to the non-resumable VM.
  Tripwire flip: `A44PerIterationLetDeclineTests` captured cases assert routing.

- **Stage 2 — Annex-B block-function flat slot (A43).** Builds on Stage 1's
  flat-slot-for-`PushEnvironment` allocation. Give the descriptor-backed block function name
  a real flat-slot mapping AND strict-correct VM env resolution so the block `f` does **not**
  leak to function scope in strict mode. Relax the
  `FunctionDeclarationInstruction { Descriptor: not null }` gate
  (`UnifiedBytecodeProductionEligibility.cs:599`). Unlocks **A43**. Risk: strict-mode block
  scoping (`StrictModeBlockFunctionScopingTests`) — the precise reason a naive gate relax
  broke before.

- **Stage 3 — Resumable nested-function capture (B23).** *Different root, separate
  infrastructure.* B23 fails because a generator/async body's locals are **flat slots on the
  resume state**, not env bindings, so a nested function literal capturing a body local
  cannot see it through its env chain
  (`UnifiedBytecodeResumableNestedFunctionTests`). Stage 1/2 do **not** unlock B23; it needs
  free-variable capture analysis threaded into the resumable invokers (and hoisted-decl slot
  population for the sibling B36). Recommend deferring; not part of this foundation.

## Biggest risk

Corrupting TDZ for **ordinary** `let`/`const` block scopes by over-applying the
copy-instead-of-wipe behavior. Mitigation: the copy is keyed off
`PerIterationCopySlotIndices`, which is populated *only* from
`PushEnvironmentInstruction.PerIterationBindings` (non-empty solely for loop per-iteration
pushes). Every non-per-iteration scope retains the current wipe. The full internal suite
(≈6020/0/2) plus the three named regression guards must be green before landing.

## Verification performed for this doc

- Dumped compiled `ExecutionPlan` for captured vs non-captured per-iteration `let` and the
  routing shadowed-`let` shape (`FlatSlotMappings`, `PushEnvironment` scope ids, eligibility
  result).
- Confirmed `NestedFunctionScopeRegressionTests` shadowed-`let` capture already routes
  through production (`routedFunc` assertions).
- Prototyped eager flat-slot allocation; confirmed it flips eligibility to `True` and that
  the captured A44 cases then throw the **spurious TDZ**, isolating the per-iteration copy
  as the true blocker. Prototype reverted (it regresses A44 to a crash without Stage 1's VM
  copy).
