# ADR 0310: Keep SLO gate target status evidence-only

## Status

Accepted

## Context

Issue #2905 followed the first committed ProfileRunner SLO baselines for
`startup` and `microtask`. The repository had a `make slo-gate` path and
`tools/perf-slo-baseline.md`, but the surrounding roadmap and dreaming docs
could still be read as though the committed averages proved the directional
Node.js-competitive SLOs.

That was too strong. The gate measures `ProfileRunner --force-timing` average
milliseconds per iteration on local hardware. The cold-start target in
`docs/dreaming.md` is `< 5 ms p95`, and the broader direction is
Node.js-competitive runtime behavior. A committed average timing guardrail is
useful regression evidence, but it is not p95 evidence and not same-run
Node.js parity proof.

## Decision

Keep `tools/check-slo-gate` as a hard committed-baseline regression gate and
make directional target status non-failing evidence.

The accepted policy is:

1. `startup` and `microtask` hard-fail only when current average timing exceeds
   the committed baseline by more than the configured tolerance.
2. The gate may print target-status columns for SLO targets from
   `docs/dreaming.md`, but those columns are evidence only.
3. The gate output and baseline comments must say when a target is p95 while
   the committed value is avg-ms.
4. Roadmap and dreaming prose must keep committed baselines separate from
   Node.js parity or directional SLO completion claims.

## Consequences

- Future SLO-gate changes can make regression output clearer without making CI
  flaky by treating noisy timing targets as hard parity gates.
- A green `make slo-gate` proves "no committed-baseline regression beyond
  tolerance"; it does not prove cold-start p95, microtask target compliance, or
  Node.js parity.
- To advance an SLO from guardrail evidence to a stronger claim, attach the
  matching measurement shape: p95 for p95 targets and same-run comparison proof
  for parity language.
- New SLO profiles must preserve the existing gate consistency rule: add the
  profile, add it to the gate, regenerate the committed baseline, and document
  whether any target-status output is failing or evidence-only.

## Evidence

- Delivery PR #2908 merged as commit `5acdd30a`.
- Changed surfaces:
  - `tools/check-slo-gate`
  - `tools/perf-slo-baseline.md`
  - `docs/dreaming.md`
  - `docs/roadmap.md`
  - `Makefile`
- The delivery added `hard_ceiling_ms` and non-failing target-status output to
  `tools/check-slo-gate`, documented the avg-ms vs p95 boundary in
  `tools/perf-slo-baseline.md`, and changed roadmap/dreaming wording so
  committed baselines are guardrails rather than parity claims.

## Issue / PR

Issue #2905 / PR #2908.

## Related

- `docs/rules/performance-profiling-guardrails.md`
- `docs/rules/roadmap-architecture-claims.md`
- `docs/rules/tooling-shell-wrappers.md`
- `tools/check-slo-gate`
- `tools/perf-slo-baseline.md`
