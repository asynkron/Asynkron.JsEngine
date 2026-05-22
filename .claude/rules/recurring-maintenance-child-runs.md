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
- Do not add or modify recurrence infrastructure in the child run; Faktorial
  owns the recurrence schedule.
- For docs-only maintenance, avoid full builds, Test262, package installs, or
  broad audits unless the edit directly depends on them.
- When multiple recurring-maintenance children are active, check active sibling
  child log summaries before choosing a slice so two children do not target the
  same narrow docs cleanup in parallel.
- When sibling summaries influence slice selection, record that sibling check
  explicitly in the child-run evidence (issue update) so review can confirm the
  run stayed non-overlapping without reconstructing scheduler state.
- When a docs slice enumerates filesystem contents (regression packs, demo
  directories, runsettings files, build targets), compare the doc against the
  actual directory listing as the baseline signal. Treat doc/filesystem drift
  as the bounded slice; do not widen the run to also edit unrelated examples
  in the same file.
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
- When gathering issue details, comments, stage output, or logs in a recurring
  child agent environment, use supplied Faktorial Source Context first. If more
  evidence is needed, use the Faktorial HTTP API, starting with the compact
  `/api/logs/<issue>/summary` endpoint. Use narrow, line-capped searches over
  `.faktorial/logs/ghNNNN.log` only when the summary is insufficient.
- Do not run the host `faktorial` daemon binary for issue, log, or state reads
  from an agent. Treat older guidance that recommends `faktorial issue` or
  `faktorial log-summary` as stale for agent runtime context gathering.
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
- When a persistent ADR/rule compaction child updates an existing semantic
  home, record baseline and final evidence from the same overlap check command
  (for example a targeted `rg` over the owned rule file) so review can confirm
  both non-duplication and the exact wording delta without reopening broad
  history.
- Keep recurring-child progress updates plain and bounded while work is in
  flight. Reserve machine-readable structured schema output for the final build
  result only; avoid emitting final-response-shaped interim status messages
  that can be misclassified as failed stage outcomes in issue logs.

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

Issue #1464 tightened persistent-compaction evidence guidance: when the slice
updates an existing semantic home instead of creating a new durable artifact,
the issue update should still show baseline/final output from one stable
overlap-check command. This keeps the run auditable as an intentional
compaction pass rather than an unproven wording edit.

Issue #1534 added an output-boundary lesson from recurring-child runtime
evidence: when interim progress messages mimic final structured responses, the
issue log can record false `success=false` stage events before implementation
completes. The durable policy is to keep interim updates plain and bounded, and
emit structured schema output only for the actual final build response.
