# Test262 Triage Proof

When a Test262 issue comes from a prior `.testrunner/summary.md` or broad
failure batch, prove that the exact listed method group or fixture still fails
on the current worktree before changing implementation or harness code.

## Rules

1. Start with the issue-supplied narrow proof command, usually:
   `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=<MethodGroup>"`.
2. If the focused proof passes, stop implementation work and report the issue as
   already green or non-reproducible on current main. Do not invent a nearby
   runtime or harness change just because the stale issue body named a
   plausible owner surface.
3. If a broader testrunner batch showed failures, treat that batch as triage
   input only. Worker crashes, collateral failures, stale binaries, or later
   merged fixes can make the batch disagree with the current focused proof.
4. Use the focused proof result to decide the next stage: implementation only
   after a current failing repro; learn/closeout when the exact issue proof is
   already green and no source change is needed.
5. A focused internal regression can still be the right closeout when the issue
   names a concrete prior crash shape and current main already passes the
   Test262 group. Keep that change test-only, mirror one exact reported fixture
   shape, and state explicitly that no runtime or harness fix was needed.
6. If the generated Test262 method group is much broader than the crash entries
   listed on the issue, prove the issue-listed fixture paths first. Use the
   method group only as a later widening step when it is a useful semantic pack,
   not as a substitute for the reported crash evidence.

## Why

Issue #815 was created from a 2026-05-17 testrunner summary that listed
`Object_values("built-ins/Object/values/primitive-numbers.js", ...)` failures.
By the build stage on 2026-05-19, the exact focused proof
`Name=Object_values` passed all 40 tests on the current worktree after
fast-forwarding to the locally available `origin/main`, so no implementation
change was warranted.

Issue #1024 repeated the same pattern from a 2026-05-19 broad Test262 runner
summary: `DisposableStack_prototype_move` had four reported crashed entries, but
the build-stage focused proof
`Name=DisposableStack_prototype_move` passed all 26 tests on the current
worktree. The correct outcome was to stop without implementation changes and
carry the learn-stage evidence as confirmation that crash-batch summaries are
not current failures until reproved.

Future agents should treat old Test262 batch summaries as suspects to reprove,
not as confirmed current failures.

Issue #1031 / PR #1103 applied the green-proof closeout path to
`Expressions_class_asyncGenMethodStatic`: current `origin/main` already passed
the reported Test262 method group, so the delivery added only a focused
internal regression for the static async generator `yield*` abrupt async
iterator lookup shape. The rule is to preserve that boundary: regression-only
coverage may be useful, but a green focused proof is not permission to change
runtime or harness behavior.

Issue #1028 / PR #1101 confirmed the same boundary for
`Expressions_arrowFunction_dstr`: the delivery stayed test-only and added
focused internal coverage for arrow-parameter array destructuring defaults
whose iterator is already complete or whose `next()` throws. Future agents
should keep that closeout shape narrow: pin the exact reported iterator
semantics locally when useful, but do not infer a source repair from an old
batch report after the focused Test262 proof is green on current main.

Issue #1032 / PR #1106 confirmed the inverse narrow-proof trap for
`Expressions_class_dstr`: the generated method group was much broader than the
23 reported crash entries. The build-stage proof had to enumerate the listed
class destructuring fixture paths in focused clusters before treating the
delivery as issue-resolved.
