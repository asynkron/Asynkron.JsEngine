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

## Why

Issue #1144 / PR #1155 was a spawned recurring maintenance child. The useful
delivery was not new recurrence machinery; it was making the bounded-run
contract explicit in `agents/how-to-build-and-test.md` so future agents choose
one safe slice and record before/after evidence.

Without this rule, recurring maintenance children can drift into broad audits,
duplicate scheduler behavior, or run expensive gates that are unrelated to a
documentation-only improvement.
