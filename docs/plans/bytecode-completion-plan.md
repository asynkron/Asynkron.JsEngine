# Plan: Complete the Bytecode Runtime

How we finish the unified-bytecode burn-down — every non-dynamic JS construct on
the production VM, both fallback interpreter tiers retired. Companion to the
finite item list in [`bytecode-burndown-checklist.md`](bytecode-burndown-checklist.md)
(currently **41 / ~133 done**). This file is the *sequencing & operating model*;
the checklist is the *inventory*.

---

## 1. Definition of Done (the terminal state)

All six must hold simultaneously and be machine-checkable:

1. No decline code fires for any non-dynamic shape — only the **Dynamic Residue**
   (`eval` / live `with` / `Function`-constructor body) ever falls back.
2. `Execute` (sync) and `ExecuteResumable` (generators/async) admit the **same**
   non-dynamic surface — full engine parity.
3. A corpus route census shows ≈100% of non-residue functions/scripts on the
   production VM.
4. The two fallback tiers — `ExpressionProgram` (tier-1) and `ExecutionPlanRunner`
   (tier-2 IR) — are deleted or compile-gated unreachable for non-dynamic code.
5. A **standing CI gate** fails if any `isDynamicResidue=false` decline ever fires.
6. The only surviving interpreter is a minimal dynamic engine reachable *exclusively*
   for the residue.

When 1–6 are green, it's done — and #5 keeps it done.

---

## 2. Operating model (how we execute, so it stays correct)

These rules come straight from the bugs this burn-down already hit (for-of
try/finally regression, the `new.target` leak, three agent drifts, duplicate PRs):

- **Sequential gated pipeline is the engine.** Each item: implement → adversarial
  verify → merge, and the *next* item branches from freshly-merged main. Zero
  collisions on the shared resumable allowlist; the gate catches bugs pre-merge.
  This is the proven default for mechanical items (it cleanly landed 5/5 last run).
- **Full suite before every merge — no exceptions.** Both the implementer *and* the
  skeptic run it. The for-of and `new.target` regressions both slipped past
  filter-only runs.
- **One universal gate.** Every bytecode PR — including parallel-agent (Codex) work
  — goes through the adversarial skeptic + full suite. The two regressions that
  reached main came from PRs that bypassed it. Either route all bytecode work
  through the gate, or phase-split (Codex on sync/docs, this lane on resumable) so
  parallel work can't touch the resumable VM.
- **Foundation rocks are done by hand / duo, not slice agents.** Closure activation
  and suspension machinery are design work; slice agents drift on them. Use
  deep-thinker→coder (the `duo` skill) or hand implementation with layered tests.
- **The standing gate is built FIRST (Stage 0), not last.** It is the tripwire that
  makes everything after it safe — it would have caught the `new.target` leak the
  day it landed. Pulling it forward from Phase D is the single highest-leverage
  sequencing change.

---

## 3. The plan — six stages, in dependency order

### Stage 0 — Lock the perimeter (finiteness + tripwire)
*Goal: a provably-finite list and a regression net before we widen anything.*
- **P0.3** diff the `UnifiedBytecodeOpCode` enum vs the sync admit-switch and both
  resumable allowlists; name every enum-but-unadmitted opcode as its own leaf.
- **P0.4** decompose the coarse leaves (**B24** class-expression → ~8 member shapes,
  **A35** → 4 object-literal-member opcodes).
- **A51a–A51m** audit: confirm each named compiler-decline leaf is real vs already
  covered; close the bookkeeping ones.
- **D5 (pulled forward)** stand up the standing CI gate: run a corpus through the
  eligibility gate and fail on any `isDynamicResidue=false` decline that isn't on the
  known-remaining list. Start it as a *ratchet* (assert the count of non-residue
  declines only ever goes down).

*Effort: ~1–2 weeks. Confidence: high. Output: a true denominator + a safety net.*

### Stage 1 — Drain the mechanical admissions
*Goal: collapse the count with the proven sequential pipeline.*
- **Resumable parity batch** (sync already handles, just allowlist + handler port):
  B12–B14 (super), B15 (optional call), B20/B21/B23/B24 (import.meta, template,
  function literal, class expr), B25–B29 (dynamic free typeof/write/update/delete +
  ref plumbing), B36/B37 (decl hoisting, scaffolding).
- **Independent sync families**: A19 (deep writes), A22–A24 (free update/delete),
  A27 (super call-target), A28–A32 (optional-chain remainder), A33/A34 (non-simple
  spread), A36/A37 (private members), A46 (`**`/BigInt operators).

*Approach: 3–5 sequential-gated pipeline runs, ~5 items each. Effort: ~2–4 weeks.
Confidence: high. This is where the count visibly moves.*

### Stage 2 — Foundation rock A: closure / captured activation
*Goal: thread a heap activation environment into the production VM.*
- **A1/A2** (`CapturedOrDynamicActivation`) — the census's *single largest category*.
  Closures that capture outer locals, and `with`-in-chain activations, currently
  decline on both routes. Admitting them unblocks a wide swath of A-tier items and
  most real-world functions.
- Also clears **A6/A7** (arrow lexical-this / class-constructor activation) and the
  resumable side of A1.

*Approach: deliberate design (duo: deep-thinker plans the env-threading, coder
implements behind layered tests). Effort: multi-week. Confidence: medium — bounded
but real design work. Highest single unblock on the sync route.*

### Stage 3 — Foundation rock B: suspension machinery
*Goal: persist driver / try-finally / iterator state across `yield`/`await` on the
resume state.*
- **B32** try/catch/finally across suspension · **B33** break/continue + driver
  cleanup across suspension.
- **B38/B39** `yield*` over dynamic/async iterables · **B41** `for await…of`
  async-iterator driver · **B40/B43/B44** `with` / awaited-`with` / awaited binding
  in resumable.
- **Async generators** end-to-end (retire the `AsyncGeneratorInvoker` IR fallback,
  **E6**).

*Approach: the hard part — the original roadmap rated this low-confidence. Build the
resume-state machinery incrementally (one driver family at a time), each behind
adversarial nested-in-constructor / cleanup-on-return probes (the exact edges that
bit for-of and new.target). Effort: the longest pole, multi-week. Confidence:
low–medium.*

### Stage 4 — Long tail
*Goal: mop up the remaining named shapes once the foundations exist.*
- A8–A16 (wider calls, free dynamic lookup — much of this falls out of Stage 2),
  A44 (per-iter `let`), A41/A45/A48/A49 (residual UnsupportedPlanShape leaves),
  **A52** `debugger;` (new parser/AST owner), **C3** script-route union gate
  (closes automatically as A/B complete), **B8a** const-slot bitmap.

*Effort: ~2–3 weeks. Confidence: high (most are pipeline items or fall out of 2/3).*

### Stage 5 — Erect the dynamic quarantine (Phase D)
*Goal: prove the only thing that can reach a fallback is the residue.*
- **D1** direct-eval gate · **D2** eval-injected-binding gate · **D3** `with`/awaited-
  `with` gate · **D4** `Function`-produced-body gate · **D5** flip the Stage-0 ratchet
  to a hard gate (zero non-residue declines).

*Effort: ~1–2 weeks. Confidence: high. Precondition for Stage 6.*

### Stage 6 — Retire the fallback tiers (Phase E = the finish line)
*Goal: delete the interpreters; only the dynamic engine remains.*
- **E3** final opcode-coverage diff (gate check).
- **E4** remove `ExpressionProgram` (tier-1) from the hot path — smaller/simpler, do
  first; prove its coverage is admitted, then delete/quarantine.
- **E5** remove `ExecutionPlanRunner` (tier-2 IR) — the big one; gated on full A/B/C
  parity + the standing gate being airtight.
- Shrink the legacy AST evaluator to the residue-only dynamic interpreter.

*Effort: careful, ~2–4 weeks. Confidence: medium — the payoff, but deleting fallbacks
is only safe once D5 proves nothing non-dynamic reaches them. Do it tier-1 first as a
rehearsal.*

---

## 4. Critical path & shape of the work

```
Stage 0 (finiteness + tripwire)
   └─> Stage 1 (mechanical drain)  ──┐
   └─> Stage 2 (closure activation) ─┤
   └─> Stage 3 (suspension machinery)┤──> Stage 4 (long tail)
                                     │        └─> Stage 5 (quarantine)
                                     │              └─> Stage 6 (retire tiers) = DONE
```

- Stages 1, 2, 3 can run **concurrently on separate tracks** (1 = pipeline lane,
  2 = sync-foundation lane, 3 = resumable-foundation lane) — *if* the universal gate
  + phase-split discipline holds. That's the throughput lever.
- Stages 5–6 are strictly serial and last.
- **Longest pole = Stage 3 (suspension machinery).** It gates async-generator
  completeness and therefore Stage 6. Start it early even though it's slow.

## 5. Honest assessment

- **~40% of remaining items are mechanical** (Stages 1 + most of 4) — fast, the
  pipeline eats these.
- **The real cost is two foundation rocks** (closure activation, suspension
  machinery) and the **tier-retirement endgame**. These are the months the original
  census estimated; nothing here makes them small, but the path is now concrete and
  every step is gated.
- Biggest risk: a parallel-agent regression slipping past the gate (it happened
  twice). Stage 0's standing gate + a universal-gate rule is the mitigation.

## 6. Immediate next three moves
1. **Stage 0 first** — stand up the D5 ratchet gate + finish P0.3/P0.4 so we have a
   true denominator and a tripwire. (Highest leverage; cheap.)
2. **Open the Stage 1 pipeline** in parallel — next sequential batch = the resumable
   parity group (B12–B15, B20–B29).
3. **Scope Stage 2** — a duo (deep-thinker→coder) design pass on closure/captured
   activation env-threading, since it's the biggest single unblock and the longest
   to get right.
