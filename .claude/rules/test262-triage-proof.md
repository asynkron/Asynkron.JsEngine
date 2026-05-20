# Test262 Triage Proof

When a Test262 issue comes from a prior `.testrunner/summary.md` or broad
failure batch, prove that the exact listed method group or fixture still fails
on the current worktree before changing implementation or harness code.

## Rules

1. Start with the issue-supplied narrow proof command, usually:
   `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=<MethodGroup>"`.
2. If directory-form `dotnet test` fails because the SDK cannot resolve the
   test project from a folder argument, rerun the exact same focused proof
   against
   `tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`.
   Treat the first failure as command-shape friction, not source evidence.
3. If the focused proof passes, stop implementation work and report the issue as
   already green or non-reproducible on current main. Do not invent a nearby
   runtime or harness change just because the stale issue body named a
   plausible owner surface.
4. If a broader testrunner batch showed failures, treat that batch as triage
   input only. Worker crashes, collateral failures, stale binaries, or later
   merged fixes can make the batch disagree with the current focused proof.
5. Use the focused proof result to decide the next stage: implementation only
   after a current failing repro; learn/closeout when the exact issue proof is
   already green and no source change is needed.
6. A focused internal regression can still be the right closeout when the issue
   names a concrete prior crash shape and current main already passes the
   Test262 group. Keep that change test-only, mirror one exact reported fixture
   shape, and state explicitly that no runtime or harness fix was needed.
7. If the generated Test262 method group is much broader than the crash entries
   listed on the issue, noisy, or prone to the local inactivity guard, prove
   the exact issue-listed fixture files or file patterns first. Use the method
   group only as a later widening step when it is a useful semantic pack, not
   as a substitute for the reported evidence. Treat a group hang as proof
   friction, not as an implementation failure, when the reported rows are green.

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

Issue #826 added the method-group fallback clause. The issue listed 14
`Statements_class_elements` private inner-function fixture rows, and every
listed private field/getter/setter/method file pattern passed individually on
current main. The broad `Name=Statements_class_elements` proof hit the
inactivity guard because it selected the generated method group, so the durable
lesson is to close from exact row proof when the batch report is stale and the
group filter is the noisy part.

Issue #872 / PR #1227 repeated the green-on-main closeout shape for
`TypedArrayConstructors_from`. The reported fixture
`built-ins/TypedArrayConstructors/from/new-instance-from-zero.js` was already
covered by the live method group (58/58 passing on current `origin/main`), so
the delivery stayed test-only and added a single focused regression mirroring
the fixture across `Int8Array`, `Uint8Array`, `Int32Array`, and `Float64Array`.
The same delivery also surfaced an adjacency conflict with sibling typed-array
closeouts (#869 / #1223 / #1224 ToIndex regressions in the same test file);
resolve those by keeping both non-overlapping test blocks rather than
collapsing one closeout into the other.

Issue #876 repeated this pattern for `TypedArray_prototype_at`: the stale batch
listed only the strict and sloppy
`built-ins/TypedArray/prototype/at/returns-undefined-for-holes-in-sparse-arrays.js`
rows, while current main passed the exact fixture filter (2/2) and the full
method group (28/28). Future TypedArray Test262 issues should still inspect the
owner surface enough to understand the claimed failure, but once the focused
proof is green, stop without inventing a nearby runtime or harness patch.

Issue #877 repeated the no-source-change variant for
`TypedArray_prototype_fill`. The 2026-05-17 testrunner summary listed the
strict and sloppy `fill-values-conversion-operations.js` rows as failures, but
the build-stage proof on 2026-05-20 passed the issue-supplied method group
66/66 on current `origin/main`. Future agents should stop at that proof result
for this class of stale batch report unless a current failing fixture row is
reproduced.

Issue #878 repeated the same no-source-change closeout for
`TypedArray_prototype_indexOf`. The investigation identified
`TypedArrayBase.IndexOfInternal` as the plausible owner for fromIndex coercion
and strict comparison, but the build-stage proof on 2026-05-20 passed the
issue-supplied method group 86/86 on current `origin/main`. Future agents should
treat that owner lookup as context only: once the exact focused proof is green,
do not patch strict equality, signed-zero, or fromIndex handling without a
current failing fixture row.

Issue #1065 repeated the fixture-exact fallback for `Statements_forAwaitOf`.
The issue listed seven crashed `async-func-dstr-const-ary-ptrn-*` rows from the
2026-05-19 Test262 runner summary. The build-stage method-group proof hit the
local 60 second inactivity guard, but each listed fixture pattern passed when
run directly on current `origin/main`. The durable lesson is the same: a broad
generated group hang is proof friction, not source evidence, when the exact
reported rows are green.

Issue #879 repeated the same current-proof boundary for
`TypedArray_prototype_indexOf_BigInt`: investigation still identified
`TypedArrayBase.IndexOfInternal` and BigInt strict equality as the plausible
owner surface, but the build-stage focused proof
`Name=TypedArray_prototype_indexOf_BigInt` passed on current main with no
source or test diff. A stale BigInt TypedArray row is therefore not enough to
change `fromIndex`, signed-zero, or equality code after the current method
group is green.

Issue #1043 repeated the same current-proof boundary for
`Language_evalCode_indirect`, and added a proof-command pitfall. The directory
form of the issue-supplied Test262 command failed under this local SDK, but the
same filter against
`tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj`
passed, including the previously crashing Annex B indirect-eval fixture. Future
agents should repair the command shape before treating a focused Test262 proof
as source failure, then stop without eval or harness changes when the exact
project-file proof is green.
