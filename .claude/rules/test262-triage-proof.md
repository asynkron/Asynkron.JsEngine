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

## Why

Issue #815 was created from a 2026-05-17 testrunner summary that listed
`Object_values("built-ins/Object/values/primitive-numbers.js", ...)` failures.
By the build stage on 2026-05-19, the exact focused proof
`Name=Object_values` passed all 40 tests on the current worktree after
fast-forwarding to the locally available `origin/main`, so no implementation
change was warranted.

Issue #1030 repeated the same failure mode from a 2026-05-19 testrunner batch:
the broad summary listed eight `Expressions_asyncGenerator_dstr` crashes, but
the build-stage focused proof
`Name=Expressions_asyncGenerator_dstr` passed all 744 tests on current
`origin/main` with no source diff. That issue should remain a reminder that a
plausible owner surface is investigation input, not permission to patch without
a current failing repro.

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
