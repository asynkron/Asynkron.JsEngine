# Recurring Maintenance Child Runs

When a spawned recurring maintenance child asks for one bounded repository
maintenance pass, keep the run small, repo-local, and directly reviewable.

## Rule

- Choose exactly one docs, tooling, test-fixture, dependency, or workflow
  simplification slice.
- Capture a cheap baseline signal before editing and the matching final signal
  after editing.
- Use a compact issue-update evidence shape: `Baseline signal`, `Final signal`,
  `Slice check`, and `Scope note`.
- Prefer the copy/paste `## Build Update` template in
  `agents/how-to-build-and-test.md` so child-run updates stay comparable across
  recurring issues.
- Do not add or modify recurrence infrastructure in the child run; Faktorial
  owns the recurrence schedule.
- For docs-only maintenance, avoid full builds, Test262, package installs, or
  broad audits unless the edit directly depends on them.
- When a docs slice enumerates filesystem contents (regression packs, demo
  directories, runsettings files, build targets), compare the doc against the
  actual directory listing as the baseline signal. Treat doc/filesystem drift
  as the bounded slice; do not widen the run to also edit unrelated examples
  in the same file.
- When the slice touches ADR creation guidance, include the cheap duplicate
  prefix signal as evidence, but keep ADR ID allocation aligned with
  `.claude/rules/adr-allocation.md`: Faktorial learn or knowledge-artifact work
  must reserve IDs through the runtime allocator, not by guessing from a
  directory scan.

## Why

Issue #1144 / PR #1155 was a spawned recurring maintenance child. The useful
delivery was not new recurrence machinery; it was making the bounded-run
contract explicit in `agents/how-to-build-and-test.md` so future agents choose
one safe slice and record before/after evidence.

Without this rule, recurring maintenance children can drift into broad audits,
duplicate scheduler behavior, or run expensive gates that are unrelated to a
documentation-only improvement.

Issue #1207 / PR #1217 was the same shape applied to a doc/filesystem drift
slice. `agents/how-to-build-and-test.md` enumerated only four regression-pack
examples (`full`, `temporal`, `regexp`, `proxy`) while
`tests/Asynkron.JsEngine.Tests.Test262/regression-packs/` actually contained
seven subsystem packs (`annexb`, `array-prototype`, `intl`, `language`,
`proxy`, `regexp`, `temporal`) plus `full`. The directory listing was the
baseline signal, the updated "Available packs" list was the final signal, and
no build, Test262, or recurrence-infrastructure work was needed.

Issue #1239 / PR #1251 was a docs-only maintenance slice triggered by the
pre-existing duplicate ADR prefix `0071`. The useful delivery was adding
prevention guidance to `agents/how-to-build-and-test.md` while leaving the
actual duplicate cleanup and ADR ID allocation policy to the dedicated ADR
allocation rule. This keeps the maintenance child small and prevents future
learn-stage agents from treating a filesystem scan as the allocator.

Issue #1240 / PR #1253 clarified that the issue update itself needs a stable
evidence shape. Without explicit `Baseline signal`, `Final signal`, `Slice
check`, and `Scope note` fields, review has to infer whether the maintenance
child actually proved before/after behavior, passed diff hygiene, and stayed
away from recurrence infrastructure or unrelated files.

Issue #1302 / PR #1309 turned that stable evidence shape into a concrete
copy/paste `## Build Update` template in `agents/how-to-build-and-test.md`.
Without a template, recurring maintenance children can still mention the right
fields while varying the structure enough that review has to reconstruct the
slice, before/after signals, diff hygiene, and scope boundaries by hand.
