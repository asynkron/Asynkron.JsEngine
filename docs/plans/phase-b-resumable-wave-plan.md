# Phase B (Resumable Parity) — Triage + Wave Plan

Source: triage audit a5b0c09 (read-only probes on `main`, verified vs IR fallback — **no miscompiles found**).
Resumable fast-path logs: `unified-bytecode-resumable-{generator,async,async-generator}-fast-path func=<name>`.

## Already routing (PINNED — `ResumableAlreadyRoutingPinTests`)
- **B2** — `yield*` over a LOCAL/param iterable routes + correct. Remaining open scope = free/dynamic iterable (== **B38**).
- **B32** — try/**finally** with yield-in-try routes + correct (empty + non-empty finally). Remaining open scope = try/**catch** across suspension.
- (B1 partial) — `return await p` and `await; discard` async forms already route. Remaining = awaited value bound into a slot/binding read back across suspension (== **B44**).

## Wave ordering (minimize merge contention on `UnifiedBytecodeProductionEligibility.cs` + the opcode allowlist)

`resumeclosure` (branch `codex/b-resumable-captured-closure`) owns the allowlist right now — **everything touching `TryFindUnsupportedResumableOpcode` (must stay 1:1 with the `ExecuteResumable` switch) or the shared plan-decline guards lands AFTER it.**

### Wave 1 — SERIALIZED on the eligibility file, after resumeclosure (each adds opcodes/handlers)
1. **B1 + B44** (awaited value → slot/binding across resume) — best first lift; mirrors the admitted sync `let [a]=expr` / `var x=expr` slot stores + the existing `AwaitValue`/`StoreResumeValue` path.
2. **B23 + B36** (nested function literal + decl hoisting) — same opcode family.
3. **B21** (tagged template) — mirrors admitted sync tagged-template.
4. **B26/B27/B28/B29** (dynamic free write/update/delete/compound) — new resumable handlers for dynamic-mutation opcodes; mirrors admitted sync handlers.
5. **B33** (break/continue across suspension) — driver-cleanup over suspension.
6. **B8a** (generator own-const) — const-slot bitmap threading (ledger option a); plan-level lexical-slot guard.
7. **B24** (class expression) — largest; decompose per P0.4 (~8 member shapes) as its own mini-wave.

### Wave 2 — plan-level guards (not the opcode list), still same file → serialize but separable
- **B38** (`yield* freeIter`) + **B39** (async `yield* asyncIterable`) — `YieldStar`/`Iterator*` opcodes already admitted; only the plan guards (~:1062, ~:1140-1150) block them.

### Wave 3 — PARALLELIZABLE (separate file surfaces, no allowlist 1:1 risk until they land their own opcodes)
- **B12/B13/B14** (super call/read/write) — super-env on resume state + super handlers. Pairs with the `codex/A1-super` workflow.
- **B40/B43** (`with` in generator / awaited `with`) — dynamic-`with`-env on resume state; pairs with super-env plumbing.
- **B41** (`for await…of`) — async-iterator driver; large independent shape (declines both routes today).
- **B20** (`import.meta`) — small independent meta-property.

### Wave 0 — zero-impl pins (DONE)
- B2 (local/param yield*), B32-finally — pinned + re-scoped above.

## Policy/infra items (not shape-probable)
- **B45 / B46 / B47a** — allowlist-default master gaps + resume-state layout. Address as the allowlist/resume-state foundation when threading the above.
