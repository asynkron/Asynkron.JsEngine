# Stage 2 Design: Admit Closure / Captured-Activation (A1/A2)

Read-only design investigation for the `CapturedOrDynamicActivation` decline — the
single largest remaining sync-route category. **Headline finding: this is an
*admission* gap, not a VM capability gap.** Execute against this; companion to
[`bytecode-completion-plan.md`](bytecode-completion-plan.md) Stage 2.

## The crucial finding

The production VM **already resolves captured variables by name through the live
`JsEnvironment` chain**: `LoadDynamicIdentifier` / `StoreDynamicIdentifier` /
`UpdateDynamicIdentifier` walk into the enclosing activation's heap slot
(`UnifiedBytecodeVirtualMachine.Execute` `:85-107`, `:258`; `GetDynamicIdentifierValue`
`:5508`, `StoreDynamicIdentifierValue` `:5527`). The call environment is chained
`body → functionEnvironment → _closure` (`CreateSimpleIrActivationEnvironment`
`:4274-4285`), so **reads and writes both work by reference for free** — a captured
`n++` mutates the outer binding because the store goes straight to the enclosing
environment's heap slot.

There is already an admitted captured-closure path —
`CanUseProductionUnifiedBytecodeCapturedClosureActivation`
(`SyncFunctionInvoker.cs:3895-3922`) — that compiles captured accesses as
dynamic-identifier ops. **But it is gated to `SimpleReturnProgram` only** (single
`return <expr>;` bodies; `CanUseProductionUnifiedBytecodeArrowProgramShape` `:3924-3928`,
`ExecutionPlan.cs:138-155`). So `function outer(){ let n=0; function inner(){ n++;
return n; } return inner; }` declines purely because `inner`'s body is multi-statement.

**The real work = lift the captured-closure path from "single-return-expression body"
to "general multi-statement body."** No new binding model, no VM change.

## A1 vs A2 vs with-residue
- **A1 (real work):** `_hasCapturedActivationInClosure && !arrow && !captured` disjunct
  (`SyncFunctionInvoker.cs:3499-3525`; `HasCapturedActivationInClosure` `:4257-4270`
  walks `closure.Enclosing` for a non-global function/body scope). Lift this.
- **A2 (residue):** `hasUnprovenDynamicActivation` — body contains `with`/direct-eval
  (`ScopeDynamicnessAnalyzer.AllowsIdentifierCaching` `:14-40`). Leave declining.
- **with-in-chain (residue):** `_hasClosureWithObject = closure.HasWithObjectInChain()`
  (`:185`). Leave declining.

## Recommended approach: Option A — reuse the existing dynamic-identifier + env path
Rejected: **Option B** (box captured locals into V8-style cells) — high risk, creates
an aliasing seam between the VM cell model and the IR runner's heap-environment model
for no correctness gain. **Option C** (extend `CanUseMaterializedActivationDynamicLookup`
`:9086`) — `MaterializedBindingNames` is the *current* function's own names, not the
enclosing activation's; can't identify captured locals. Option A keeps the single shared
`JsEnvironment` binding model across IR / VM / resumable.

## Staged plan (Stage 0 + 5 by-hand; Stages 1–4 slice-pipeline)

- **Stage 0 (by-hand, BOUNDED):** add
  `CanUseProductionUnifiedBytecodeCapturedClosureGeneralShape(plan)` that validates the
  full instruction stream admits with `allowsOrdinaryDynamicIdentifiers: true`, and route
  `CanUseProductionUnifiedBytecodeCapturedClosureActivation` (`:3895`) through it instead
  of `SimpleReturnProgram`. Keep `!_hasClosureWithObject` + `_allowIdentifierCache`
  guards. Proof: eligibility test (`inner` → `DeclineCode.None`) + counter execution +
  `unified-bytecode-production-fast-path` route-hit assertion.
- **Stage 1 (slice):** read-only captured access, multi-statement body.
- **Stage 2 (slice):** captured writes / compound updates (`n++`, `n+=1`, `n=v`).
- **Stage 3 (slice):** captured-`const` write → TypeError, captured-`let` read-before-init
  → ReferenceError (flows through `SetIdentifierJsValue`/`TryGetIdentifierJsValue` — verify,
  don't reimplement).
- **Stage 4 (slice):** mix own slots / loops / `for-of` / `new.target` + captured access.
- **Stage 5 (by-hand):** resumable parity — lift the resumable `CapturedOrDynamicActivation`
  decline (`UnifiedBytecodeProductionEligibility.cs:354-355`) and give
  `AsyncFunctionInvoker`/`SyncGeneratorInvoker`/`AsyncGeneratorInvoker` (`:116/:78/:145`,
  which today have no captured path) an analog. `CallingEnvironment` already survives
  suspension (`UnifiedBytecodeProgram.cs:286-294`); the work is admission + the resumable
  opcode allowlist for the dynamic-identifier opcodes.

## Downstream unblocks
- **A6 arrow lexical-this** (shares the program-shape gate — Stage 0 lifts arrows out of
  `SimpleReturnProgram`-only too).
- **A7 class-ctor activation** (drop captured-closure exclusion in
  `CanUseProductionUnifiedBytecodeBaseClassConstructorActivation` `:4016`).
- **Resumable A-tier** items blocked only by capturing an enclosing local (Stage 5).
- A1 is the gate for the entire "function nested in a function" population — most
  non-trivial real-world functions capture something. **Single largest sync-route
  multiplier.**

## Risk / test strategy (mirror the for-of / new.target adversarial probes)
- By-reference mutation: `counter()` returns `1,2,3`; verify NO flat `SlotMap` entry for
  the captured name (must lower to a dynamic op, not a copied slot).
- Nested 3-level capture; innermost mutates outermost; all frames observe it.
- TDZ / const equivalence vs the IR runner (golden harness).
- **Mixed route:** a VM-routed inner closure capturing from an IR-routed outer (and vice
  versa) must alias the same heap binding — the seam Option B would have broken; under
  Option A it's one shared `JsEnvironment`, but test it explicitly.
- Capture-across-`yield`/`await` (Stage 5).
- `with` boundary: closure whose enclosing chain has a live `with` still declines
  (residue) — Stage 0 must NOT admit it; A2 in-function `with`/eval still declines.

_Read-only design. No code modified._
