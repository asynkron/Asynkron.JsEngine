# ADR 0260: Keep Shared Workload Profile Evidence Command-Complete

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-4232277e24`
/ PR #2510 requested evidence for a broad shared-bytecode proof batch:
`propertyaccess`, `simplearithmetic`, object/array literal-heavy workloads,
activation-lite workloads, and representative loop/control-flow cases. The
issue explicitly required exact commands, allocation rows, profile excerpts,
baseline accounting, and no broad runtime-win claims from noisy data.

The accepted delivery added
`docs/performance/shared-bytecode-surface-and-parallel-workloads-profile-evidence.md`.
Review sent it back twice before deploy:

1. the first pass recorded the non-lite `activation-arguments` profile where
   the acceptance criteria asked for an activation-lite workload, and baseline
   accounting missed comparable checked-in rows for some workloads; and
2. the second pass listed `propertyaccess --memory` and
   `simplearithmetic --memory` in the command block but did not record their
   parse-failure outcomes in the evidence section.

The final note became useful because it captured all listed command outcomes,
including trace conversion/parsing failures for profiler tooling and usable
allocation output for `activation-arguments-lite --memory` and
`forloop --memory`.

## Decision

Keep shared or multi-workload performance evidence notes command-complete and
scope-limited.

When an evidence note lists a profiling or benchmark command, the note must
include the observed outcome for that command. A successful table excerpt,
profile excerpt, empty/no-results output, conversion failure, parse failure, or
tool-unavailable result are all valid outcomes when reported explicitly. A
command must not be listed as run and then silently omitted from the evidence
body.

Workload-family names in acceptance criteria must map to the exact profile key
being evidenced. For example, an activation-lite requirement must use one of
the `activation-*-lite` profiles, not the adjacent non-lite activation profile,
unless the note calls out that the requested profile was unavailable and treats
the substitution as a tooling gap rather than satisfaction of the criterion.

Before/current or baseline/current accounting stays per workload. If a
comparable checked-in row exists, cite the source document and current row. If
no comparable checked-in baseline is found, say that explicitly and keep the
current row as baseline-establishing evidence instead of inventing or implying
an improvement.

Trace conversion and allocation-parse failures are profiler/tooling outcomes.
Record them as tooling constraints unless a separate runtime proof connects the
failure to engine behavior.

## Consequences

- Reviewers can verify evidence fidelity from the note without rerunning every
  profile.
- Noisy benchmark tables remain useful as current rows without being converted
  into broad runtime-win claims.
- Profiler conversion failures stay visible for later tooling work, while the
  runtime conclusion remains limited to the captured rows.
- Future evidence-only batches should update the existing performance note or
  create one focused note with complete command accounting instead of spreading
  partial command outcomes across logs.

## Related

- `docs/performance/shared-bytecode-surface-and-parallel-workloads-profile-evidence.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0216-keep-simplearithmetic-profiler-scope-comparable-before-runtime-retries.md`
- `docs/adrs/0256-keep-unified-bytecode-coverage-matrix-and-boundary-docs-synchronized.md`
