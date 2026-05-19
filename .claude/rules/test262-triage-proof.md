# Test262 Triage Proof

When a Test262 issue comes from a prior `.testrunner/summary.md` or broad
failure batch, prove that the exact listed method group or fixture still fails
on the current worktree before changing implementation or harness code.

## Rules

1. Start with the issue-supplied narrow proof command, usually:
   `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=<MethodGroup>"`.
2. If the focused proof passes, stop implementation work and report the issue as
   already green or non-reproducible on current main. Do not invent a nearby code
   change just because the stale issue body named a plausible owner surface.
3. If a broader testrunner batch showed failures, treat that batch as triage
   input only. Worker crashes, collateral failures, stale binaries, or later
   merged fixes can make the batch disagree with the current focused proof.
4. Use the focused proof result to decide the next stage: implementation only
   after a current failing repro; learn/closeout when the exact issue proof is
   already green and no source change is needed.

## Why

Issue #815 was created from a 2026-05-17 testrunner summary that listed
`Object_values("built-ins/Object/values/primitive-numbers.js", ...)` failures.
By the build stage on 2026-05-19, the exact focused proof
`Name=Object_values` passed all 40 tests on the current worktree after
fast-forwarding to the locally available `origin/main`, so no implementation
change was warranted.

Future agents should treat old Test262 batch summaries as suspects to reprove,
not as confirmed current failures.
