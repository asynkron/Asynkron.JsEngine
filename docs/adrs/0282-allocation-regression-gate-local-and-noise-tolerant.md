# ADR 0282 — Keep the Allocation-Regression Gate Local and Noise-Tolerant

## Status

Accepted

## Context

Issue #2668 / PR #2671 asked for "a CI job" to guard the recently won evaluator
hot-path allocation reductions (PR #2661 `SyncFunctionInvoker` static-analysis
caching, commit `bd70ca63` `JsValue`-native coercion helpers, PR #2658 typed-AST
property-key caching) against silent regression. Two facts shaped the design:

1. **There is no GitHub Actions CI in this repo.** Actions were intentionally
   removed (commits `dcd3ff22`, `3d993683`); there is no `.github/workflows`.
   "Add a CI job" therefore could not mean silently resurrecting Actions.

2. **Allocation counts are nearly deterministic, but not perfectly.** Across ~10
   measurement runs a fixed ~24,624-byte blip occasionally lands inside the
   measured loop. That blip is a large *percentage* on the small profiles
   (+30% on `ir-arithmetic` ≈ 80 KB, +11% on `forloop` ≈ 221 KB) yet negligible
   on the large ones (`functioncalls` ≈ 24 MB). A pure percentage tolerance
   would either false-positive on small profiles (if tight) or miss real
   proportional regressions on large profiles (if loose enough to absorb the
   blip on small ones).

The measurement primitive already existed: `./benchmark.sh --allocations --smoke`
→ `tools/compare-jint-profiles` emits per-profile Asynkron allocated bytes via
`ProfileRunner --measure-allocations`. The gap was only a committed baseline, a
gate step, and a refresh path.

## Decision

Deliver a standalone, deterministic gate command —
`tools/check-allocation-regression` — plus a committed baseline
(`tools/allocation-baseline.txt`), wired into the local `/pre-pr` quality path
(Step 5), **not** into resurrected GitHub Actions. The command is CI-agnostic
and ready to be called from any future CI if true PR-CI is later adopted; that
remains a separate owner decision.

Two design choices make the gate robust:

- **Asynkron-only scope.** The guard is about Asynkron's own allocations, so the
  gate does not run Jint. This keeps it cheap.

- **Dual tolerance: `max(percentage, absolute floor)`.** A profile fails only
  when its increase exceeds the *larger* of a percentage headroom
  (`--tolerance`, default 15%, env `ALLOC_GATE_TOLERANCE`) and an absolute floor
  (`--abs-floor`, default 49,152 bytes ≈ 2× the observed blip, env
  `ALLOC_GATE_ABS_FLOOR`). The percentage dominates large profiles and catches
  proportional regressions; the floor absorbs the fixed measurement noise on
  small profiles. A pure percentage model is the wrong shape here.

The baseline is refreshed with one command — `./tools/check-allocation-regression
--update` — which re-measures and rewrites `tools/allocation-baseline.txt` (with
a self-documenting header), then `git add` + commit. The baseline is never
hand-edited.

The script is portable to the bash 3.2 shipped with macOS (no associative
arrays — baseline lookup reads the file line by line) and forces `LC_ALL=C` so
`awk` formats numbers with a `.` decimal separator regardless of host locale.
Its `smoke_profiles` list (`fib forloop ir-arithmetic functioncalls
functioncalls-lite`) must stay in sync with the `smoke_profiles` in
`tools/compare-jint-profiles`.

## Consequences

- The allocation guard runs in the same place agents already run pre-PR checks,
  with no CI infrastructure to maintain.
- Small-profile measurement noise no longer produces false regressions; a
  fixed-byte blip is absorbed while a real proportional regression on any
  profile still fails the gate. Verified: the gate passed on the current tree
  with the +30.5% `ir-arithmetic` / +11.1% `forloop` blips absorbed by the
  floor, and correctly reported `+101.4% REGRESSED` (exit 1) against a
  deliberately halved `functioncalls` baseline.
- An intentional allocation change requires an explicit baseline refresh commit,
  which makes the change visible in review.
- If the smoke set or the `compare-jint-profiles` list changes, both lists must
  be updated together or the gate silently stops guarding a profile.

## Related

- Durable rule: `docs/rules/pre-pr-required.md` (Allocation Regression Gate
  subsection).
- Guarded wins: PR #2661, commit `bd70ca63`, PR #2658.
- Guardrail family: `docs/rules/performance-profiling-guardrails.md` (separate
  CPU/memory evidence; profile-owned optimization proofs).
