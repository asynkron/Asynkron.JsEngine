# ADR 0320: Keep unified bytecode route-hit evidence explicit

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-7b8017c72a`
and PR #2962 closed the final production-decline proof slice by adding a
route-hit evidence path to `ProfileRunner` and `tools/profile`.

Before this delivery, benchmark/profile runs could show timing or allocation
rows for a manifest workload without proving whether that workload actually
entered the production unified-bytecode route. That made it too easy to read a
generic profile result as unified-bytecode coverage or performance evidence
when the workload might have stayed on the existing IR path. The existing
logger signal, `unified-bytecode-production-fast-path`, already identified the
route, but collecting it through normal trace logging was noisy and tied to
profiler-backed runs.

The delivery added `--route-hits` to `ProfileRunner`, counting only that
existing logger marker, and added a direct `tools/profile --route-hits` mode.
The direct mode uses fresh engines by default so route-hit-only runs avoid
shared-engine redeclaration noise while leaving normal CPU and memory profiler
runs unchanged.

## Decision

Keep route-hit evidence as an explicit companion signal for unified-bytecode
benchmark/profile claims.

- Use `rtk ./tools/profile <profile> --route-hits` when a manifest workload is
  cited as production unified-bytecode route evidence.
- Treat timing and allocation rows as performance or stability evidence only;
  they do not prove route coverage unless the matching route-hit count is
  reported.
- Count the existing `unified-bytecode-production-fast-path` logger marker
  without mirroring normal logs unless trace mode is enabled.
- Keep route-hit-only profile runs free of external profiler requirements and
  use fresh engines unless the caller explicitly opts into a different runner
  shape.
- Continue to report memory/profile rows as allocation stability only unless a
  separate before/after proof justifies a performance-improvement claim.

## Consequences

- Future unified-bytecode widening work can distinguish "this profile ran" from
  "this profile entered the production unified-bytecode VM."
- Route-hit proof can be collected cheaply for manifest profiles even when the
  external profiler is unavailable or unnecessary.
- Existing CPU and memory profiling behavior stays unchanged, so route
  observability does not perturb the profiler-backed timing/allocation flows.
- Workloads with zero route hits, such as the checked `forloop`,
  `propertyaccess`, `functioncalls-lite`, and `activation-noargs-lite` rows from
  PR #2962, must not be cited as production unified-bytecode coverage.

## Evidence

- Delivery PR #2962 merged as commit `b57f238a42dacab20d30a3d4b80bbeebb669bb91`.
- Build-stage verification recorded:
  - `rtk dotnet build tools/ProfileRunner/ProfileRunner.csproj -c Release -v q --nologo`
  - focused unified-bytecode proof suite: 684 passed
  - no matches from the AST seam scan over `TypedAstEvaluator.ExecutionPlanRunner*`
  - route-hit rows: `forloop=0`, `propertyaccess=0`,
    `functioncalls-lite=0`, `activation-noargs-lite=0`, `forofiteration=2000`
  - `rtk ./tools/profile forloop --memory` completed with `6.75 MB` allocated
  - `rtk git diff --check`
- Test262 was intentionally not run because the delivery changed only
  tooling/docs and did not alter runtime semantics.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `tools/ProfileRunner/Program.cs`
- `tools/profile`
