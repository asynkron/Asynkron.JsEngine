# Build and Test

## Standard Commands
```bash
# Restore dependencies
rtk dotnet restore

# Build everything
rtk dotnet build

# Main test suite
rtk dotnet test tests/Asynkron.JsEngine.Tests

# Discover supported repo quality/test entrypoints
rtk make help
```
When running shell commands in this repo via Codex/Faktorial runtime, prefix
them with `rtk` (see `/Users/rogerjohansson/.codex/RTK.md`).

Do not use `--no-build` for ad hoc test runs; keep code compiled with latest
changes.

Exception: `make quality` intentionally runs `build-internal` followed by
`test-internal-no-build`. Keep that build-before-no-build sequence intact; it
was added for issue #753 / PR #888 after the quality gate was SIGTERMed during a
single build-and-test `test-internal` run.

## Narrow Test Runs
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

## Recurring Maintenance Child Runs

When a spawned maintenance-child issue asks for one bounded repository
maintenance pass, keep the slice repo-local and reviewable:

- Policy ownership: recurring-child policy and edge-case lessons live in
  `.claude/rules/recurring-maintenance-child-runs.md`.
- Operational ownership: this section owns the runnable checklist and `## Build
  Update` template used in build-stage issue updates.
- ADR allocation ownership: `.claude/rules/adr-allocation.md` owns ADR ID
  reservation (`faktorial-api adr-next`) and duplicate-prefix guardrails.
- Persistent ADR/rule compaction details stay in
  `.claude/rules/recurring-maintenance-child-runs.md`; keep this section
  focused on the operational checklist and Build Update template.

1. Check active sibling child log summaries before choosing a slice so this
   run intentionally avoids overlapping sibling work; record the sibling check
   in the issue update.
   If sibling summaries or other issue context are unavailable, record that
   lookup as unavailable evidence and continue from maintained repository docs
   and local code context instead of blocking the run.
2. Choose exactly one docs, tooling, test-fixture, or workflow simplification
   slice. Do not add or change recurrence infrastructure; Faktorial owns the
   recurrence schedule.
3. Capture a cheap baseline signal before editing. Prefer evidence such as
   `rtk make -n quality`, a targeted `rg` check, `git diff --check`, or another
   narrow command tied directly to the chosen slice.
   For recurring-child context reads and Faktorial precedence/fallback order,
   follow `.claude/rules/agent-context-issues.md` and
   `.claude/rules/recurring-maintenance-child-runs.md` instead of duplicating
   those details in this operational checklist.
   In Faktorial runs, use supplied Source Context or the HTTP API before raw
   logs; keep raw-log searches narrow and do not run the host `faktorial`
   daemon binary for issue, log, or state reads.
4. If the slice adds a new ADR under `docs/adrs/`, reserve the ID with
   `faktorial-api adr-next` first and use the returned `adr_id`; do not guess
   ADR IDs from filesystem scans. If the slice does not create an ADR, skip the
   allocator call. If an ADR is required but the local `faktorial-api` helper is
   unavailable, record that environment limitation in the issue evidence and
   follow current Faktorial/runtime allocator guidance instead of widening scope
   into `gh` auth workarounds or host-daemon reads. If the lesson fits an
   existing durable document, update that file instead of creating a
   duplicate-number ADR. After writing or renaming ADRs, run a duplicate-prefix
   check over `docs/adrs` and record a clean result in the issue update.
5. Make only the small change required for that slice.
6. Capture the matching final signal after editing and record both signals in
   the issue update.
7. When the slice adds or keeps evidence links to repository files (especially
   roadmap/docs claims), verify each cited path exists before finalizing. If a
   planned ADR/report path is missing, cite a maintained existing evidence
   surface instead of keeping the broken reference.
8. Keep in-flight progress updates plain and bounded. In any stage, reserve
   machine-readable structured schema output for the actual final stage result
   only, so interim status notes are not misclassified as failed stage
   outcomes.

Use a compact evidence shape in the issue update so reviewers can compare
before/after quickly:

- `Baseline signal:` exact command and a short output excerpt captured before
  editing.
- `Final signal:` the same command after editing with a short output excerpt.
- `Sibling check:` issue numbers and one-line note of which active sibling
  slices were intentionally avoided (or `none` when not applicable).
- `Slice check:` `git diff --check` result and changed file list for the slice.
- `Scope note:` one line confirming no recurrence infrastructure, Makefile
  contract, or unrelated files were changed.

Preferred issue update template for recurring child runs:

```md
## Build Update
- Slice: <one bounded docs/tooling/test-fixture/workflow slice>
- Baseline signal: `<command>`
  - Output: <short excerpt>
- Final signal: `<same command>`
  - Output: <short excerpt>
- Sibling check: <issue numbers reviewed + avoided slice note, or `none`>
- Slice check: `git diff --check` and changed files: <paths>
- Scope note: No recurrence infrastructure or unrelated surfaces changed.
```

If the run is evidence-only, keep the same template and state why no file
change was required.

For docs-only maintenance, do not run full builds, Test262, package installs,
or broad audits unless the edit directly depends on them. The canonical local
quality gate remains `make quality`, which builds and tests the internal suite
only.

## ECMAScript Test262 Suite

Run the full LanguageTests class (43,000+ tests):
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings
```

Run the BuiltInsTests class:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings
```

### Targeting Specific Test262 Tests

List available tests to find exact names:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --list-tests 2>&1 | grep -i "SomePattern"
```

**Filter syntax:**
- `~` = contains
- `&` = AND
- `|` = OR
- Parentheses for grouping

Run a single parameterized test by method + path:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&FullyQualifiedName~block-decl-onlystrict"
```

Run multiple specific tests with OR:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&(FullyQualifiedName~block-decl-onlystrict|FullyQualifiedName~switch-case-decl-onlystrict|FullyQualifiedName~switch-dflt-decl-onlystrict)"
```

Run all tests for a specific method:
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "Name=FunctionCode"
```

Run tests matching a pattern (e.g., all strict-mode-only tests):
```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&FullyQualifiedName~onlystrict"
```

## Demos
```bash
rtk dotnet run --project examples/Demo
rtk dotnet run --project examples/PromiseDemo
rtk dotnet run --project examples/EventQueueDemo
rtk dotnet run --project examples/NpmPackageDemo
rtk dotnet run --project examples/NodeHostDemo
```

## Test262 regression session
- Source list: `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt`
- Runner: `rtk ./tools/run-test262-regressions.sh`
- This pack is intentionally Test262-only and excludes non-Test262 regressions such as `Asynkron.JsEngine.Tests.Array_indexOf_OnlyCallsHasPropertyOnPrototypeAfterLengthZeroed`.
- Smaller subsystem packs live under `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/`.
- Available packs: `full`, `annexb`, `array-prototype`, `gh1832-private-accessor-logical-assignment`, `intl`, `language`, `proxy`, `regexp`, `temporal`.
- Named-pack inventory (`--list`) comes from those files in
  `regression-packs/`; it is a static inventory of runnable pack names, not a
  snapshot of current failures.
- List packs without failing (with per-pack entry counts): `rtk ./tools/run-test262-regressions.sh --list`.
- Entry counts shown by `--list` are line counts from each pack file (typically
  one Test262 filter entry per line). Treat them as pack-size signals; they are
  not pass/fail totals from a live run.
- Examples: `rtk ./tools/run-test262-regressions.sh temporal`, `rtk ./tools/run-test262-regressions.sh intl`, `rtk ./tools/run-test262-regressions.sh regexp`.
