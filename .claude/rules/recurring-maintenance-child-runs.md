# Recurring Maintenance Child Runs

When a spawned recurring maintenance child asks for one bounded repository
maintenance pass, keep the run small, repo-local, and directly reviewable.

## Rule

- Choose exactly one docs, tooling, test-fixture, dependency, or workflow
  simplification slice.
- Capture a cheap baseline signal before editing and the matching final signal
  after editing.
- Use a compact issue-update evidence shape: `Baseline signal`, `Final signal`,
  `Sibling check`, `Slice check`, and `Scope note`.
- Policy ownership lives here; keep this file as the durable semantic home for
  recurring-child scope, sibling coordination, and lessons learned.
- Prefer the copy/paste `## Build Update` template in
  `agents/how-to-build-and-test.md` for the operational build-stage update
  format so child-run updates stay comparable across recurring issues.
- When a top-level entrypoint such as `README.md` summarizes recurring-child
  workflow, keep it as a short pointer to the owned playbook and include the
  sibling-summary check plus stable evidence field names. Do not duplicate the
  whole checklist outside `agents/how-to-build-and-test.md`.
- Do not add or modify recurrence infrastructure in the child run; Faktorial
  owns the recurrence schedule.
- For docs-only maintenance, avoid full builds, Test262, package installs, or
  broad audits unless the edit directly depends on them.
- If canonical quality verification reports a recurring-child docs or rule
  slice as failed but the available log is truncated or lacks a concrete
  file/line diagnostic, treat it as a verification-context gap first. Re-run
  `rtk git diff --check` and the exact local gate before changing source, and
  patch only when current evidence points at deterministic source drift.
- When multiple recurring-maintenance children are active, check active sibling
  child log summaries before choosing a slice so two children do not target the
  same narrow docs cleanup in parallel.
- When sibling summaries influence slice selection, record that sibling check
  explicitly in the child-run evidence (issue update) so review can confirm the
  run stayed non-overlapping without reconstructing scheduler state.
- When a fan-in or conflict-resolution issue discovers two completed children
  that landed the same maintenance slice, consolidate through one canonical
  delivery. Prefer the superset branch when it cleanly contains the duplicate
  child's change, record why the other child is superseded, and do not land the
  overlapping documentation edit twice.
- When a docs slice enumerates filesystem contents (regression packs, demo
  directories, runsettings files, build targets), compare the doc against the
  actual directory listing as the baseline signal. Treat doc/filesystem drift
  as the bounded slice; do not widen the run to also edit unrelated examples
  in the same file.
- For Test262 regression-pack inventory docs, compare both the backing
  `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/` files and
  `rtk ./tools/run-test262-regressions.sh --list` before editing. New named
  packs can be added by earlier feature work without the static docs list being
  refreshed.
- When a docs slice mirrors a command inventory already maintained in another
  repo document, compare both surfaces plus the backing filesystem inventory
  before editing. Update only the stale surface and keep the peer document as
  evidence instead of reworking every matching section.
- When a docs slice fixes links or status pointers to deleted files, use the
  missing target plus the current live evidence path as the baseline/final
  signal pair. Prefer redirecting readers to maintained source files over
  recreating stale status documents unless the issue explicitly asks for a new
  durable status artifact.
- When a tooling/docs slice needs agents to discover available options, prefer
  an explicit non-failing inventory command over making agents probe an invalid
  argument just to see an error message. Keep the listing path backed by the
  same computed source that validation uses.
- When documenting runner inventory output, state whether it is a static list
  or a live failure snapshot, and define what any displayed counts measure
  before agents use those counts for maintenance scoping.
- For recurring-child context reads, follow
  `.claude/rules/agent-context-issues.md` for Faktorial precedence and bounded
  evidence gathering. Keep recurring-child behavior explicit: use supplied
  Source Context first, then full issue/dashboard API details, then compact
  `/api/logs/<issue>/summary`, and use narrow line-capped
  `.faktorial/logs/ghNNNN.log` structural snippets only when API context is
  still insufficient.
- If an issue-detail endpoint returns only preview/truncated markdown (for
  example `...`), treat it as partial context and continue with compact summary
  plus bounded raw-log structural searches.
- Do not run the host `faktorial` daemon binary for issue, log, or state reads
  from an agent; older guidance that recommends `faktorial issue` or
  `faktorial log-summary` is stale for agent runtime context gathering.
- When older completed sibling issue logs are no longer available through the
  supplied context or HTTP summary path, treat the failed lookup itself as
  baseline evidence and continue from durable in-repo artifacts (for example
  ADRs and owned rule documents) rather than blocking or widening into external
  source-host reads.
- When the slice touches ADR creation guidance, include the cheap duplicate
  prefix signal as evidence, but keep ADR ID allocation aligned with
  `.claude/rules/adr-allocation.md`, which is the allocator authority:
  Faktorial learn or knowledge-artifact work must reserve IDs through the
  runtime allocator, not by guessing from a directory scan.
- For persistent ADR/rule compaction children, verify overlap against the
  current semantic home first and update that existing document when guidance
  is already covered. Do not create duplicate ADRs, rules, or durable notes
  for guidance that already has an owned home.
- When overlap spans a cross-cutting rule and accepted helper-specific ADRs,
  keep the detailed decisions in the ADRs and add a short ownership note to the
  rule instead of copying every helper boundary into the rule body.
- When a persistent ADR/rule compaction child updates an existing semantic
  home, record baseline and final evidence from the same overlap check command
  (for example a targeted `rg` over the owned rule file) so review can confirm
  both non-duplication and the exact wording delta without reopening broad
  history.
- If that overlap check shows the semantic home already covers the selected
  slice, treat the run as evidence-only: keep the issue update evidence
  complete, explain why no wording delta was needed, and do not invent a
  mechanical docs change just to produce file churn.
- If review sends a recurring-child build back only because an acceptance
  criterion lacks evidence fields, make the re-entry explicitly evidence-only:
  restate the baseline/final signal pair, `git diff --check`, changed-file
  scope, and no-unrelated-change note in the handoff instead of adding a new
  source/docs tweak to satisfy an already-delivered slice.
- Keep recurring-child progress updates plain and bounded while work is in
  flight. Reserve machine-readable structured schema output for the actual
  final stage result only; avoid emitting final-response-shaped interim status
  messages
  that can be misclassified as failed stage outcomes in issue logs.
- When a recurring documentation slice updates agent/operator workflow commands,
  align command examples with the current agent invocation contract and
  canonical local gate. In this repo, commands that an agent is expected to run
  should show the `rtk` prefix and normal verification should point at
  `rtk make quality`, while repository executable targets themselves must stay
  wrapper-free per `.claude/rules/pre-pr-required.md`. Treat `rtk make quality`
  as local build/test evidence only; it does not replace the mandatory pre-PR
  checklist in `.claude/rules/pre-pr-required.md`.
- When fixing docs command examples, make the final command copy/paste-safe as
  a single shell invocation when possible. Do not use `rtk cd ...` as a setup
  line or rely on cwd state crossing command examples; prefer explicit path
  flags such as `--project <path>` so the example works exactly as pasted.
- When a roadmap or documentation-maintenance slice adds or preserves evidence
  links to repository files, check that each cited path exists before finalizing
  the slice. If a planned ADR or report citation does not exist, either cite the
  maintained evidence surface that does exist or keep the roadmap claim
  boundary-only without the missing file reference.
- When a recurring roadmap child starts from issue or investigation context that
  says `docs/roadmap.md` is missing, re-check the file on the current branch
  before treating creation as the slice. If latest main already has the roadmap,
  turn the slice into a bounded refresh of the existing document or an
  evidence-only closeout instead of recreating a stale "missing file" premise.
- When a roadmap refresh only links already-accepted ADR/rule boundaries into
  `docs/roadmap.md`, the learn pass should inventory the existing semantic
  rule homes before creating another knowledge artifact. If the proxy,
  control-flow, tail-call, or other domain rule already captures the lesson,
  record that overlap and avoid duplicate ADR/rule churn.
- For recurring code-reduction children, prefer deleting one proven-dead
  internal helper or overload over reshaping a surrounding feature. Prove the
  slice with a targeted symbol/caller search immediately before and after the
  edit, pair it with a code-size signal such as `rtk cloc --vcs=git
  --include-lang=C#`, and keep behavior, tests, and recurrence infrastructure
  out of scope unless the exact deletion no longer compiles.
- If the code-reduction slice targets dormant test source files, only delete
  files that are entirely non-compiled/commented-out or otherwise proven absent
  from the test project. Confirm no live references or explicit project includes
  before editing, use file-level line-count evidence for the deleted slice, and
  treat a focused filter with no remaining matching tests as confidence that no
  compiled test contract was removed, not as a behavioral regression proof.
- When the code-reduction slice targets duplicated smoke-test fixtures rather
  than dead helpers, extract only the invariant fixture body and leave semantic
  differences explicit through named parameters or separate call sites. Prove
  every affected fixture variant with a focused test filter; line-count
  reduction alone is not behavioral proof.

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

Issue #1881 / PR #1900 repeated the same inventory drift after the
`gh1832-private-accessor-logical-assignment` regression pack existed on disk
and in `rtk ./tools/run-test262-regressions.sh --list`, but was missing from
`agents/how-to-build-and-test.md`. Future Test262 named-pack docs slices should
prove both the pack-file inventory and runner `--list` output so feature-added
packs do not silently disappear from the agent-facing static inventory.

Issue #1431 / PR #1434 was the same docs/filesystem drift pattern on the
top-level README demo list. `README.md` still omitted `EventQueueDemo`, pointed
at the obsolete S-expression-era surface, and did not name the maintained Node
host demo path even though those runnable example directories and host scripts
were present. Future README demo-list maintenance should compare the listed
examples against `examples/` first, then update the README as the bounded slice
without widening into demo behavior or broad validation work.

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

Issue #1845 / PR #1888 closed the top-level README drift around that same
evidence shape. `README.md` already pointed at the maintenance-child playbook,
but it did not name sibling-summary checks or the stable `Baseline signal`,
`Final signal`, `Sibling check`, `Slice check`, and `Scope note` fields. Future
entrypoint docs should expose those anchors while leaving the detailed
checklist in `agents/how-to-build-and-test.md`, so operators see the contract
without maintaining two full copies.

Issue #1324 captured a sibling-coordination gap while concurrent issue #1323
was already handling the README stale-link candidate
(`docs/remaining-test262-gaps.md`). The durable lesson for #1324 was not to
duplicate the README slice, but to add an explicit sibling-summary check so
parallel recurring children choose different bounded slices.

Issue #1323 / PR #1327 was the stale-link slice itself: at that time README
still pointed operators at missing `docs/remaining-test262-gaps.md`, while the
maintained Test262 evidence lived in
`tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt` and
`tests/Asynkron.JsEngine.Tests.Test262/regression-packs/`. The durable lesson
is to prove the missing target before editing and then point docs at the active
evidence source, not to recreate an obsolete tracking document. If
`docs/remaining-test262-gaps.md` exists again, treat it as historical snapshot
context only and keep current regression-session evidence anchored to the
active filter file, regression packs, runner `--list`, and Test262 README.

Issue #1325 / PR #1326 made `tools/run-test262-regressions.sh --list` exit
successfully and print the same pack list used by invalid-pack validation.
Before that, agents had to call an intentionally invalid pack name to discover
available regression packs, which turned normal workflow discovery into a
failing command and added avoidable noise to recurring maintenance evidence.

Issue #1549 / PR #1558 tightened that same runner-inventory documentation: the
Test262 `--list` output is a static inventory of runnable regression-pack files,
not a live failure snapshot, and the displayed counts are non-empty,
non-comment filter entries. Future runner docs should define inventory
freshness and count semantics up front so agents do not misread pack size as
current pass/fail evidence or widen a docs slice into a Test262 run.

Issue #1365 / PR #1370 hardened the recurring-child operational playbook after
the build agent found `faktorial-api` unavailable in its local environment. The
durable lesson is that helper availability is not the unit of progress:
recurring children should continue from supplied context and bounded local
runtime evidence, while treating missing `gh` auth or missing optional helpers
as an environment limitation rather than a source blocker.

Issue #1484 / PR #1487 tightened that fallback after another recurring child
needed full issue context, not only compact log evidence, while `faktorial-api`
was unavailable. That incident established the need for both body/comment
context and compact runtime history before treating helper availability or
missing source-host credentials as blockers.

Issue #1432 extended that fallback to older completed siblings where the
then-used compact summary helper no longer had history (for example #1403 or
#1365). The durable lesson is to record the unavailable summary attempt as
baseline evidence, then continue from maintained ADR/rule artifacts already
capturing the completed work (for example ADR 0095 and
`.claude/rules/expression-bytecode-packing.md`) instead of blocking.

Issue #1548 closed the remaining context-retrieval gap for recurring-child
agents: compact helpers and dashboard/API summary endpoints can both be
unavailable in a local run, while the timestamped raw issue log still contains
enough structural stage evidence to continue. The durable lesson is to use
bounded `.faktorial/logs/ghNNNN*.log` searches as the last local fallback,
searching only structural markers and keeping snippets line-capped, rather
than blocking or widening into broad log dumps or external source-host reads.

Issue #1572 / PR #1574 superseded the old host-binary fallback in the runnable
checklist. Running the host `faktorial` binary from an agent can start or
interfere with the daemon instead of acting as a bounded context helper, so
future recurring-child agents must prefer supplied Source Context, then the
HTTP API, then tightly scoped raw-log snippets.

Issue #1586 captured a compact-context edge case where the issue-detail API
response returned a preview/truncated body (`...`) instead of full markdown.
The durable lesson is to treat preview payloads as partial context and continue
with `/api/logs/<issue>/summary` plus bounded `.faktorial/logs/ghNNNN.log`
searches for structural markers, rather than blocking on external source-host
fallbacks.

Issue #1641 / PR #1644 clarified the normal ordering before that compact-log
fallback: recurring-child agents need full issue body/comment context from the
Faktorial issue/dashboard API before relying on compact summaries, because
compact logs are stage history and can omit acceptance criteria, comments, or
human direction that should steer the bounded slice.

Issue #1756 / PR #1760 moved the unavailable sibling/context fallback from
semantic policy into the runnable operational checklist in
`agents/how-to-build-and-test.md`. The incident showed that agents can treat a
missing sibling summary, missing issue context, or unavailable helper as a
blocker even when the maintained repo docs and local code context are enough
for a bounded documentation slice. Future recurring-child runs should record
the failed lookup as unavailable evidence, continue from the durable in-repo
context, and preserve the Source Context/API-before-raw-logs ordering.

Issue #1715 / PR #1721 added the related quality-gate edge case. The delivery
commit was already present, the available prior quality artifact was truncated,
and current local `rtk git diff --check` plus `rtk make quality` passed. The
durable lesson is to avoid inventing a mechanical docs or rule patch from an
opaque failed verifier record; re-run the exact local evidence first, then only
edit when a current deterministic diagnostic names source drift.

Issue #1464 tightened persistent-compaction evidence guidance: when the slice
updates an existing semantic home instead of creating a new durable artifact,
the issue update should still show baseline/final output from one stable
overlap-check command. This keeps the run auditable as an intentional
compaction pass rather than an unproven wording edit.

Issue #1879 / PR #1896 closed the follow-up evidence-only edge case for that
same persistent-compaction flow. When the stable overlap check shows the owned
semantic home already covers the selected slice, the right delivery is complete
issue-update evidence plus an explicit no-wording-delta rationale, not a
mechanical documentation tweak created only to produce changed files.

Issue `autrun-dis78sv4x35c-716ec19cb6` / PR #1964 repeated the evidence-only
pattern in a recurring roadmap refresh. The docs slice itself had already
landed, but AC-4 needed an explicit baseline/final signal pair, `git diff
--check`, changed-file scope, and no-unrelated-change note. The corrective
build re-entry was an evidence-only handoff, not another roadmap edit. Future
AC evidence re-entries should close the missing proof fields directly and keep
the delivered artifact lifecycle intact.

Issue #1814 / PR #1819 applied that compaction pattern to overlapping
`JsValue` object-carrier guidance. The accepted ADRs for array length helpers,
Array prototype result helpers, and number receiver extraction already owned
their helper-specific boundaries, while `.claude/rules/jsvalue-core-values.md`
owned the cross-cutting migration policy. The durable lesson is to clarify that
ownership split in the existing rule and leave accepted ADR detail intact,
rather than creating another ADR or duplicating every helper-specific decision
in the rule.

Issue #1534 added an output-boundary lesson from recurring-child runtime
evidence: when interim progress messages mimic final structured responses, the
issue log can record false `success=false` stage events before implementation
completes. The durable policy is to keep interim updates plain and bounded, and
emit structured schema output only for the actual final stage response.

Issue #1757 / PR #1761 applied that output-boundary policy to the operational
checklist in `agents/how-to-build-and-test.md` after the same issue log shape
showed schema-like interim status entries during a recurring-child run. The
delivery also reverted an out-of-scope activation test assertion change, which
confirms future recurring-child fixes should keep process guidance docs-only
when the selected slice is operational documentation.

Issue #1882 refined the same policy to be stage-neutral: schema-shaped output
belongs only in the actual final stage response, not just in build-stage
handoffs. The evidence was investigate-stage progress updates that resembled a
final structured reply and polluted run-state interpretation before
implementation finished.

Issue #1650 / PR #1652 was a recurring documentation child that found
`agents/how-to-worktrees.md` still teaching unwrapped `git`, `gh`, and
`dotnet build && dotnet test` examples even though the repo-level agent
contract requires `rtk`-prefixed shell commands and `rtk make quality` is the
canonical local gate. Future documentation-maintenance children should treat
stale command examples as workflow drift and fix them in the owned doc instead
of leaving operators to reconcile conflicting playbooks.

Issue #1816 / PR #1818 repeated the same command-example drift on the secondary
development-rules playbook: the Test Timeouts snippet still showed an
unwrapped `dotnet test` invocation with no internal test project path. The
useful lesson is to make operator-facing workflow examples both `rtk`-aligned
and copy/paste-safe from the repo root, while keeping the slice limited to the
owned stale line.

Issue #1924 / PR #1929 repeated the same workflow-doc drift inside the
Test262 triage proof rule. The durable fix was intentionally tiny: update the
agent-facing focused proof examples from bare `dotnet test` to
`rtk dotnet test`, without changing Test262 policy, source code, recurrence
infrastructure, or broader proof guidance.

Issue #1925 / PR #1930 closed the adjacent Test262 README ambiguity: the
operator command is `rtk ./tools/run-test262-regressions.sh`, while the raw
`dotnet test` line shown underneath is the script's internal runner invocation,
not a second command agents should copy/paste directly. Future workflow-doc
slices should make that distinction explicit when documenting wrapper scripts,
so command examples stay aligned with the repo's `rtk` contract without
rewriting the wrapper's own internals.

Issue #1666 / PR #1674 clarified the remaining ambiguity in that same workflow
guidance: `rtk make quality` is the canonical local build/test evidence gate for
recurring-child runs, but it is not permission to skip the mandatory pre-PR
checklist. Without this traceability, future compaction slices can accidentally
weaken PR-readiness requirements while trying to simplify recurring-child docs.

Issue #1678 / PR #1679 was a fan-in repair for duplicate recurring-child
deliveries from #1669 and #1670. Both children touched
`agents/how-to-profiling.md` for the same `rtk` command-example alignment, while
#1669 was the superset because it also updated `AGENTS.md`. The repair landed
one canonical copy from the superset branch and treated the narrower child as
superseded, preventing duplicated docs churn and avoiding a false merge-conflict
repair against an already clean index.

Issue #1670 / PR #1676 found the next edge case in profiling docs: after the
examples were `rtk`-prefixed, the BenchmarkDotNet quick start still used a
two-line `rtk cd ...` plus `rtk dotnet run ...` shape. That looked wrapped but
was not actually copy/paste safe because cwd state does not carry between
independent command invocations. Future docs command maintenance should collapse
that kind of setup into one runnable command, for example by passing
`--project` with an explicit path.

Issue #1717 / PR #1719 applied that same command-shape lesson to the top-level
README demo section. Several examples still used `cd examples/<Demo>` followed
by `rtk dotnet run`, which made the snippet depend on shell state instead of
being a direct runnable command. Future README/demo command slices should prefer
`rtk dotnet run --project examples/<Demo>` when the example can be expressed as
one stable invocation.

Issue #1729 / PR #1733 found the follow-up drift in the agent build/test
playbook: the README already listed the maintained runnable demo set
(`Demo`, `PromiseDemo`, `EventQueueDemo`, `NpmPackageDemo`, `NodeHostDemo`),
and `examples/` contained the backing projects, but
`agents/how-to-build-and-test.md` still omitted `EventQueueDemo` and
`NodeHostDemo`. Future mirrored demo-inventory slices should compare the peer
doc and filesystem together, then update only the stale playbook/list rather
than widening into demo behavior or unrelated example docs.

Issue `autrun-dir1l0mv6bm8-42faac3141` / PR #1703 fixed a roadmap maintenance
slice that cited a non-existent
`docs/adrs/0107-constant-folding-boundaries-and-operator-safety.md`. The
roadmap still needed to describe constant-folding follow-up boundaries, but the
durable evidence had to be limited to existing ADRs, reports, and rule files.
Without this rule, future roadmap children can accidentally turn directional
claims into broken evidence trails by naming planned or guessed ADR files.

Issue `autrun-dirtf03idm34-94f651a981` exposed a stale roadmap premise: the
issue investigation described `docs/roadmap.md` as missing, while the current
branch already contained a maintained roadmap from prior recurring slices. The
durable lesson is to re-check the current branch before acting on a "missing
roadmap" claim, so recurring children refresh or close out against live repo
state instead of preserving obsolete context.

Issue `autrun-dirzat33gjzc-e6a91380e8` / PR #1910 refreshed the roadmap with
ADR 0137, ADR 0138, and ADR 0139 boundaries after those decisions and their
domain rules already existed. The learn-stage lesson was not another ADR; it
was to verify `.claude/rules/ecmascript-proxy-realm-errors.md`,
`.claude/rules/ecmascript-labeled-statements.md`, and
`.claude/rules/proper-tail-calls.md` first, then keep the knowledge pass scoped
to compaction instead of duplicating the accepted proxy, control-flow, and
tail-call rules.

Issue `autrun-dis251iagv80-00c9769c91` / PR #1933 repeated that roadmap-link
pattern for ADR 0140, ADR 0141, and ADR 0142. The accepted semantic homes were
already `.claude/rules/performance-profiling-guardrails.md` for trampoline and
destructuring profile lessons, plus `.claude/rules/jsvalue-core-values.md` for
HTMLDDA string-coercion precedence. Future learn passes for roadmap refreshes
should record that overlap and avoid creating duplicate ADR/rule artifacts when
the delivery only surfaced already-accepted evidence in `docs/roadmap.md`.

Issue `autrun-dis3ezc3dlnc-5e34c6d44c` / PR #1944 was a recurring code
reduction child that removed the unused internal
`IntlNumberFormatResult.FromLiteral(string value)` factory. The useful pattern
was not an Intl architecture decision: the build stage reran the exact
`FromLiteral(` caller search, deleted only the dead helper, recorded the search
turning from declaration-only to no matches, and showed the C# line count drop.
Future code-reduction children should reuse that narrow evidence shape instead
of widening a dead-helper cleanup into formatter behavior, tests, or scheduler
policy.

Issue `autrun-dis4ox67wxio-f9e609a6f1` / PR #1953 applied code reduction to
duplicated JS smoke fixtures instead of a dead helper. The safe slice extracted
the shared resizable TypedArray fixture script while keeping the indexed-loop
and `for...of` traversal differences visible at the two test call sites, then
proved both tests with one focused filter. Future fixture-dedup slices should
follow ADR 0147: shared setup belongs in the builder, semantic differences stay
named, and every affected fixture variant remains in the proof command.

Issue `autrun-dis5yv0b12so-8430e3939a` / PR #1958 removed three fully
commented-out dormant test source files:
`TypedAstEvaluatorTests.cs`, `SunSpiderTests.cs`, and `NBodyFiveBodyTest.cs`.
The useful lesson was not to preserve commented test archives in the active
test project: after checking that the files had no compiled members and no
project-file includes, the build stage deleted only those files, recorded the
228-line slice reduction, ran `git diff --check`, and used the now-empty
focused filter as a cheap compile/test confidence check. Future dormant
test-source reductions should keep that boundary clear so agents do not delete
active scratch tests, resurrect obsolete fixtures, or overstate a no-match
filter as proof of runtime behavior.

Issue `autrun-dis78sue3600-cfedc9e361` / PR #1965 was the adjacent but
distinct test-source reduction: `AsyncIterableDebugTest.cs` was compiled and
active, but it was an output-only async iterable scratch probe with no
assertions, while assertion-bearing async iteration coverage remained in the
owning test classes. Future code-reduction children may delete compiled debug
probes only after proving they are not the owner contract, checking neighboring
asserted coverage, recording the file-level line-count deletion, and running
the focused owner test filter. Do not treat every `DebugTest` suffix as dead
code, and do not claim runtime proof from line-count reduction alone.
