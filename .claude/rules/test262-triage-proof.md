# Test262 Triage Proof

When a Test262 issue comes from a prior `.testrunner/summary.md` or broad
failure batch, prove that the exact listed method group or fixture still fails
on the current worktree before changing implementation or harness code.

## Rules

1. Start with the issue-supplied narrow proof command, usually:
   `rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=<MethodGroup>"`.
2. If directory-form `rtk dotnet test` fails because the SDK cannot resolve the
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
6. Focused internal regressions can still be the right closeout when the issue
   names concrete prior crash shapes and current main already passes the
   Test262 proof. Keep that change test-only, mirror the reported observable
   fixture shape or related edge-case cluster, and state explicitly that no
   runtime or harness fix was needed.
7. If the generated Test262 method group is much broader than the crash entries
   listed on the issue, noisy, or prone to the local inactivity guard, prove
   the exact issue-listed fixture files or file patterns first. Use the method
   group only as a later widening step when it is a useful semantic pack, not
   as a substitute for the reported evidence. Treat a group hang as proof
   friction, not as an implementation failure, when the reported rows are green.
8. If a stale Test262 crash slice is suspected to be stress-sensitive rather
   than plainly still failing, keep the proof bounded to representative
   issue-listed rows before editing source. Run a normal focused proof, repeat
   the same filter in a small loop, rerun a small neighboring semantic pack
   when adjacent rows exercise the same suspected owner surface, rerun with the
   Test262 harness/base-realm caches disabled when cache contamination is
   suspected, and enable `JSENGINE_DEBUG_POOL_GUARDS=true` for pooled-state
   suspects. If all of those focused stress checks stay green, close out
   without runtime or harness changes.
9. If the generated method group is host-contention-prone but the issue lists a
   small exact fixture set that should remain a reusable acceptance gate, add a
   focused Test262 wrapper class for those exact rows instead of relying on the
   broad `Name=<MethodGroup>` filter. Keep the wrapper named for the issue
   slice, include strict and non-strict variants when the issue listed both,
   and document that the wrapper exists to classify the exact rows without
   executing the noisy generated pack.
10. If a focused Test262 proof fails before execution with
    `NETSDK1022 Duplicate 'Compile' items` under
    `tests/Asynkron.JsEngine.Tests.Test262/Generated`, treat stale generated
    harness output as proof-environment friction. Remove only that generated
    output and rerun the exact same focused proof before changing engine,
    generated source, harness policy, or issue ownership.

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

Issue #1842 repeated this boundary for `Statements_try_dstr`. The initial
focused proof failed at project evaluation with duplicate `Compile` items under
`tests/Asynkron.JsEngine.Tests.Test262/Generated`; after clearing only that
stale generated output, the same `Name=Statements_try_dstr` proof passed 186/186
on current main. That failure was not source evidence and did not justify a
destructuring, try/catch, iterator-close, or Test262 harness patch.

Issue #1843 repeated the no-source-change closeout for legacy
`Statements_while` rows. The issue body correctly identified while-loop
completion and side-effect semantics as the plausible owner surface, but the
build-stage focused Release reruns for the exact `S12.6.2_A8.js` and
`S12.6.2_A9.js` filters both passed 2/2 on the current worktree. A plausible
loop-runtime owner is still triage context only; do not patch loop emitters,
completion bookkeeping, or Test262 harness behavior when the exact reported
rows are green.

Issue #1844 repeated the same boundary for legacy `Statements_with` rows. The
issue body named object-environment lookup, `with` scope lifetime, `var`
pre-resolution, and closure capture as plausible owners, but the build-stage
proof passed the whole issue-supplied `Name=Statements_with` filter 182/182 on
the current worktree, and focused `WithStatementTests` passed 21/21. Keep the
owner map as investigation context only; do not patch `WithEmitter`,
`JsEnvironment`, object-environment lookup, or the Test262 harness unless a
current focused `with` row fails reproducibly.

Issue #1867 repeated this boundary for the legacy direct-eval variable
statement fixture `language/statements/variable/12.2.1-11.js`. The issue body
correctly pointed at `EvalHostFunction` and the `var arguments;` collision
surface, but that semantic decision was already covered by issue #1834, ADR
0132, and `.claude/rules/ecmascript-direct-eval-declaration-instantiation.md`.
The build-stage proof passed both the issue-supplied `Name=Statements_variable`
filter 115/115 and the exact fixture filter 1/1, plus the focused internal
`TestVariableDeclarations.DirectEvalVarArgumentsInSloppyFunction_ShouldBeAllowed`
test. Keep this as an evidence-only closeout pattern: do not reopen direct eval
declaration-instantiation or generic var binding code when the current exact
fixture and the existing semantic regression are already green.

Issue #1918 repeated the direct-eval closeout pattern for arrow-function
parameter environments named `arguments`. The issue listed four
`EvalCode_direct` rows around `var arguments` plus assignment in arrow
parameter/default contexts, but the build-stage proof passed the exact reported
rows 4/4 and the full `Name=EvalCode_direct` method group 336/336 on current
main. Treat future direct-eval arrow/arguments batch reports the same way:
record the plausible `EvalHostFunction` owner surface, but stop without runtime
or harness changes when the current exact rows are already green and the
semantic rule/ADR coverage already exists.

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

Issue #1079 repeated the no-source-change boundary for
`Temporal_ZonedDateTime_prototype_eraYear`. Investigation correctly identified
`TemporalHelper` as the owner for the ZonedDateTime `eraYear` prototype getter,
receiver branding, and shared era helpers, but the build-stage proof on
2026-05-20 passed the issue-supplied method group on current `origin/main`.
Future Temporal Test262 issues should treat owner-surface discovery as context:
once the exact focused proof is green, do not add nearby accessor, branding, or
harness changes without a current failing fixture row.

Issue #1080 repeated the no-source-change closeout for
`Temporal_ZonedDateTime_prototype_withCalendar`. The 2026-05-19 crash batch
listed branding and builtin-shape rows as crashed, and investigation correctly
identified `TemporalHelper`, `JsTemporalZonedDateTime`, `HostFunction`, and
`JsValue` as plausible owner surfaces. The build-stage focused proof on current
`origin/main` passed the issue-supplied method group 40/40 with no source diff,
so the owner lookup stayed context only. Future Temporal Test262 crash issues
should still inspect the branded receiver and built-in function shape surface,
but once the exact focused proof is green, stop without changing Temporal,
HostFunction, or harness code unless a current failing fixture row is
reproduced.

Issue #1333 repeated the no-source-change closeout for a mixed
TypedArray/ArrayBuffer crash slice. The parent `.testrunner/summary.md` listed
representative TypedArray callback, detached-buffer, resizable-buffer, and
ArrayBuffer receiver rows as crashed, and investigation correctly identified
`TypedArrayBase`, `TypedArrayPrototype`, `TypedArrayHelper`, `JsArrayBuffer`,
and `ArrayBufferHelper.RequireArrayBuffer` as the plausible owner surface. The
build-stage proof on the issue worktree passed each representative focused
filter: `TypedArray_prototype_findIndex` callback/detached rows, the
`TypedArray_prototype_every` shrink-mid-iteration rows,
`TypedArrayConstructors_internals_Get` detached-buffer rows, and
`ArrayBuffer_prototype_transfer` `this-is-not-object.js`. Future mixed
TypedArray/ArrayBuffer crash reports should still map the receiver and buffer
state surface, but a stale batch row is not enough to change callback-loop,
detached/out-of-bounds, resizable-buffer, or receiver-validation behavior after
the current representative fixture filters are green.

Issue #1347 extended this green-closeout rule to stress-sensitive stale slices.
It revisited the mixed TypedArray/ArrayBuffer rows from #1333 after the normal
focused proof was already green. The delivery ran the representative detached,
resize/shrink, receiver-validation, and transfer filters once (28/28), repeated
the same focused filter three times (3/3 green), reran it with the Test262
harness and base-realm caches disabled (28/28), and reran the callback/iteration
subset with `JSENGINE_DEBUG_POOL_GUARDS=true` (28/28). That evidence was enough
to stop without changing `TypedArrayPrototype`, `TypedArrayBase`,
`JsArrayBuffer`, object pools, or Test262 harness cache/agent code.

Issue #1744 / PR #1768 repeated the same test-only closeout shape for
`WeakSet.prototype.add` rows. The reported crash list named a small related
edge-case cluster: duplicate return-this, non-registered symbol acceptance,
registered symbol rejection, incompatible receiver TypeError, and constructor
non-callability. The delivery added focused internal `WeakSetTests` coverage
for those observable cases and the retry confirmed the focused WeakSet suite
passed 26/26 after aligning assertions with current `ThrowSignal` wording. That
does not create a new WeakSet architecture decision; it reinforces that stale
Test262 crash closeouts may pin the exact observable cluster locally while
leaving runtime and harness code untouched when no current failing proof exists.

Issue #1746 repeated the no-source-change closeout for a suspected
stress-sensitive `Intl.NumberFormat.prototype.format` crash slice. The reported
rows covered `signDisplay` plus locale-sensitive `ko-KR`/`zh-TW` and rounding
interactions, making `IntlNumberFormatter` a plausible owner surface. The build
stage still stopped after the four exact issue-listed rows and a neighboring
NumberFormat signDisplay/roundingPriority subset passed on current main. Future
Intl.NumberFormat crash-list issues should classify exact rows and small
adjacent semantic packs before touching formatter, digit-option, cache, or
harness code; a plausible locale/signDisplay theory is not source evidence
after the current focused proof stays green.

Issue #1747 repeated the no-source-change closeout for
`Intl.NumberFormat` `roundingPriority` mixed-options rows. The old crash list
named the strict and sloppy
`intl402/NumberFormat/test-option-roundingPriority-mixed-options.js` fixtures,
but the build-stage proof on current main passed the exact fixture filter
2/2 and a neighboring `Intl402 & NumberFormat & roundingPriority` slice 4/4.
Future Intl.NumberFormat crash-batch issues should still inspect option parsing
and formatter ownership, but once the exact current proof and a small adjacent
semantic slice are green, stop without changing Intl runtime, Test262 harness,
or nearby regression tests unless a current failing row is reproduced.

Issue #1825 repeated the no-source-change closeout for a stale
`DecodeURI` four-byte percent-decoding crash report. The URI decoder was the
right owner surface to inspect, and existing URI percent-decoding rules already
covered the shared `decodeURI` / `decodeURIComponent` UTF-8 invariants, but the
build-stage proof on current main passed `Name=DecodeURI` 110/110 in Release.
Future URI Test262 crash issues should still check the shared decoder and the
focused URI rule, but a prior four-byte crash report is not enough to edit
`GlobalHelper` or Test262 harness policy after the current focused method group
is green.

Issue #1827 repeated the evidence-only closeout for `Intl.supportedValuesOf`
and `Intl[@@toStringTag]` crash rows. The issue named plausible `IntlHelper`,
`IntlUtilities`, object metadata, and generated Test262 owner surfaces, and an
early overlapping run showed one `Intl.supportedValuesOf` row canceled during a
large currency/DisplayNames loop. The bounded focused rerun on current
`origin/main` then passed the combined filter
`FullyQualifiedName~Intl_supportedValuesOf|FullyQualifiedName~Intl_toStringTag`
52/52 with no repository diff. Future Intl crash-list issues should treat owner
lookup and transient cancellation as context only after a bounded current proof
passes; do not change Intl runtime, object `@@toStringTag` descriptors, or the
Test262 harness without a reproducible failing row.

Issue #1826 repeated the same exact-row proof boundary for generated RegExp
Unicode property escape crash rows. The issue listed emoji, Script, and
Script_Extensions fixtures from `RegExp_propertyEscapes_generated`; current main
passed representative affected rows and then the full issue-listed affected file
set 28/28 in 12s. The broad `Name=RegExp_propertyEscapes_generated` filter was
attempted but killed after roughly three minutes because that generated method
group includes broader known offenders outside the issue slice. Future generated
RegExp property-escape issues should still inspect `JsRegExp` and Unicode
property data ownership, but once the exact listed rows are green, treat a broad
method-group hang as proof friction rather than permission to edit RegExp
runtime, generated Unicode data, or Test262 harness code.

Issue #1828 repeated the same no-source-change boundary for
`language/expressions/call/trailing-comma.js`. The parser owner surface already
accepted ordinary call trailing commas while keeping dynamic `import(...)`
argument validation strict. A prior focused-ish `FullyQualifiedName~trailing-comma`
run passed 1150 selected rows in 46.5s, while the exact file filter
`FullyQualifiedName~language/expressions/call/trailing-comma.js` timed out under
a 180s guard before producing final rows. Future call-expression Test262 crash
reports should inspect `ParseArgumentList` and call-target lowering as context,
but do not patch parser, expression bytecode, or harness timeout policy from a
stale crash row when the current evidence shows green adjacent coverage and
bounded proof friction rather than a deterministic failing fixture.

Issue #1833 / PR #1854 repeated the exact-row proof boundary for BigInt
modulus crash rows. The broad `Name=Expressions_modulus` pack was
host-contention-prone in the local environment, but the issue-listed BigInt
fixtures were small enough to promote into
`ExpressionsModulusBigIntFocusedTests`. That wrapper proved
`bigint-and-number.js`, `bigint-arithmetic.js`, `bigint-errors.js`, and
`bigint-modulo-zero.js` across strict and non-strict variants 8/8, while
internal `BigIntTests` pinned the sign, mixed BigInt/Number TypeError, and
zero-modulo RangeError cases. Future Test262 crash-list issues may use the
same focused-wrapper shape when the generated method group is the noisy part
and the exact listed fixture set is the real acceptance boundary.
