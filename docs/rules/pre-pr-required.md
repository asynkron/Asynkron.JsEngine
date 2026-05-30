# Pre-PR Checklist (MANDATORY)

**NEVER create a PR without completing ALL of these steps in order.**

## Required Steps Before Any PR

### 1. Roslynator Fix
```bash
rtk roslynator fix src/Asynkron.JsEngine
rtk dotnet build src/Asynkron.JsEngine
```
If build fails, fix issues and rerun roslynator.

### 2. Run Full Internal Unit Tests
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests
```
- Rerun flaky tests to confirm
- Fix broken tests before proceeding
- **ALL tests MUST pass**

### 3. Check for Code Duplication
```bash
rtk quickdup --path src/Asynkron.JsEngine --ext .cs --select 0..20 --min 2 --exclude ".g."
```
If new duplications found: refactor, then restart from step 1.

### 4. Format Code
```bash
rtk dotnet format src/Asynkron.JsEngine
```

### 5. Create PR
Only after ALL checks pass.

### Canonical Local Gate
```bash
rtk make quality
```
Use this as the canonical internal build/test gate during local verification.
It does **not** replace the mandatory pre-PR checklist above (Roslynator, full
internal tests, QuickDup, and `dotnet format`); complete those steps before PR
creation.

Why: issue #1661 / PR #1662 clarified this after recurring maintenance wording
left room to treat `rtk make quality` as a replacement for the pre-PR checklist.
The gate is additive local build/test evidence; it must not weaken the required
PR-readiness steps.

## Allocation Regression Gate

`tools/check-allocation-regression` is a `/pre-pr` step (Step 5): it re-measures
Asynkron managed allocations for the benchmark smoke set and fails on a
per-profile regression beyond tolerance, guarding the evaluator hot-path
allocation wins (PR #2661, commit `bd70ca63`, PR #2658).

- **Gates live in the local `/pre-pr` quality path, not GitHub Actions.** This
  repo has no `.github/workflows` — Actions were intentionally removed (commits
  `dcd3ff22`, `3d993683`). When an issue asks to "add a CI job" or "a CI gate",
  deliver a standalone, runnable command wired into `/pre-pr`; do NOT resurrect
  GitHub Actions. Adopting true PR-CI is a separate owner decision.
- **The tolerance is `max(percentage, absolute floor)`, by design.** A fixed
  ~24 KB measurement blip occasionally lands in the measured loop; it is a large
  percentage on small profiles (`ir-arithmetic`, `forloop`) but negligible on
  large ones. Do not "simplify" the gate to a pure percentage tolerance — that
  reintroduces false positives on small profiles. The floor
  (`--abs-floor`/`ALLOC_GATE_ABS_FLOOR`, default 49152) absorbs the fixed noise;
  the percentage (`--tolerance`/`ALLOC_GATE_TOLERANCE`, default 15) catches
  proportional regressions on large profiles.
- **Refresh the baseline only via `--update`, never by hand.** When an
  intentional change legitimately moves the numbers, run
  `./tools/check-allocation-regression --update && git add
  tools/allocation-baseline.txt` and commit. The header is regenerated; do not
  edit `tools/allocation-baseline.txt` directly.
- **Keep the gate's `smoke_profiles` in sync with `tools/compare-jint-profiles`.**
  Changing one list without the other silently stops guarding a profile.

WHY: issue #2668 / PR #2671 added this gate after PR #2661 / `bd70ca63` / #2658
reduced evaluator hot-path allocations and nothing guarded those wins from
silently regressing. ADR:
`docs/adrs/0282-allocation-regression-gate-local-and-noise-tolerant.md`.

## Rules

- Do NOT skip any steps
- Do NOT create PR if any step fails
- Iterate until all checks pass
- If user asks to "just create the PR" without checks, explain this is mandatory and run the checks

## Makefile Quality Contract

Preserve the `make quality` split between `build-internal` and
`test-internal-no-build`. The `--no-build` test target is allowed only in this
quality-gate path because `quality` performs the build immediately first.

Keep every `build-internal` `dotnet build` line wired through
`$(DOTNET_BUILD_STABILITY_ARGS)`. The default `DOTNET_BUILD_STABILITY_ARGS ?=
/m:1 /nr:false` is part of the quality-gate reliability contract: it serializes
MSBuild and disables node reuse so host child-node failures do not masquerade as
source failures. Do not remove it or omit it from new internal build projects
without a current quality-window proof.

Keep `test.runsettings` blame collection tuned for the canonical gate. Do not
lower `CollectDumpOnTestSessionHang TestTimeout` below `300000` ms or disable
the blame collector without a current quality-window proof. WHY: issue #2084 /
PR #2127 failed after implementation because the previous 60-second inactivity
timeout aborted `dotnet test` during a long-silent internal-test stretch, while
focused reruns passed.

Keep Makefile command tools configurable through variables such as
`DOTNET ?= dotnet` and `GIT ?= git`. Do not hardcode `rtk`, `rtk proxy`, or
another local agent wrapper inside repository Make targets; wrap the shell
invocation externally when the current agent environment requires it.

When adding or changing Makefile variables that affect the quality, build, or
test entrypoints, keep the non-failing `make help` output aligned with those
overrides. Document the variable name, default, and purpose in the help
inventory instead of making future agents infer supported knobs from target
recipes.

Why: issue #753 / PR #888 repaired a build-back failure where the gate was
SIGTERMed during `test-internal` after debug builds had already completed.
Collapsing `quality` back to a single build-and-test command can reintroduce the
same orchestration-window failure. Standalone `test-internal` must continue to
build before it tests.

Issue #755 / PR #905 later repaired review feedback where the Makefile
preserved the correct quality sequence but still embedded the local `rtk`
wrapper. Repository targets must remain runnable outside the agent runtime while
agent sessions can still invoke them as `rtk make quality`.

Issue #2447 / PR #2462 found that the Makefile already exposed useful
quality/build/test knobs (`CONFIGURATION`, `DOTNET`, `GIT`, build/test args, and
`XUNIT_ARGS`), but `make help` listed only targets. The durable lesson is that
Makefile behavior and Makefile discovery are one contract: new or changed
operator-facing variables must be discoverable through `make help` without
probing invalid invocations or rereading recipes.

Issue #1830 / PR #1850 repaired a later quality-gate failure where MSBuild child
node "2" exited prematurely during `tests/Asynkron.JsEngine.Tests.csproj`.
Because the failure was build-host/process reliability rather than a test
assertion, the durable fix is to keep `build-internal` single-node and
node-reuse-free by default through the shared stability variable.
