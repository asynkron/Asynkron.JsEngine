# Recurring Maintenance Child Runs

When a spawned recurring maintenance child asks for one bounded repository
maintenance pass, keep the run small, repo-local, and directly reviewable.

## Rule

- Choose exactly one docs, tooling, test-fixture, dependency, or workflow
  simplification slice.
- Capture a cheap baseline signal before editing and the matching final signal
  after editing.
- Do not add or modify recurrence infrastructure in the child run; Faktorial
  owns the recurrence schedule.
- For docs-only maintenance, avoid full builds, Test262, package installs, or
  broad audits unless the edit directly depends on them.
- When a docs slice enumerates filesystem contents (regression packs, demo
  directories, runsettings files, build targets), compare the doc against the
  actual directory listing as the baseline signal. Treat doc/filesystem drift
  as the bounded slice; do not widen the run to also edit unrelated examples
  in the same file.

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
