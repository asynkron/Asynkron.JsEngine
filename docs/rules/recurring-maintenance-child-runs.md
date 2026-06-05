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
- When a top-level entrypoint such as `README.md` or `AGENTS.md` summarizes
  recurring-child workflow, keep it as a short pointer to the owned surfaces:
  `agents/how-to-build-and-test.md` for the runnable checklist and `## Build
  Update` template, and this rule file for recurring-child policy ownership
  and durable compaction lessons. Include the sibling-summary check plus stable
  evidence field names, but do not duplicate the whole checklist or policy
  body in the entrypoint.
- When issue/workflow playbooks describe Faktorial context gathering or build
  handoffs, keep them aligned with the owned recurring-child evidence contract:
  supplied Source Context or API first, compact summaries before raw logs, no
  host `faktorial` daemon reads, and an explicit `Sibling check` in recurring
  child `## Build Update` evidence. Do not rely on the build/test checklist
  alone when review gates evaluate the issue-update handoff.
- Do not add or modify recurrence infrastructure in the child run; Faktorial
  owns the recurrence schedule.
- Treat issue and handoff markers such as `Part of automation template`,
  `trigger=automation recurrence`, `trigger=persistent recurrence`,
  `local:adr-rule-compaction`, or recurrence-normalization wording as
  classification for one runnable bounded child delivery. Normalize the
  evidence shape against this owned rule, but do not turn those markers into
  persistent setup, scheduler behavior, recurrence infrastructure, or duplicate
  repository policy.
- When refined acceptance criteria or review feedback names a specific
  recurrence or compaction marker literal, keep that literal in this
  classification boundary and the durable `## Why` traceability, then prove it
  with a targeted `rtk rg` check. Issue #2221 / PR #2230 showed that missing a
  named marker can send an otherwise-correct bounded rule update back to build.
- For docs-only maintenance, avoid full builds, Test262, package installs, or
  broad audits unless the edit directly depends on them.
- When a docs-only recurring child adds Mermaid diagrams, validate only the
  extracted Mermaid fence bodies with a focused parser/renderer check when that
  tool is available. Use temporary `.mmd` inputs rather than fragile shell
  extraction forms, and do not treat generic markdownlint output as a source
  blocker unless this repo has configured markdownlint as a required gate.
- When Mermaid labels contain punctuation that the parser can treat as syntax,
  such as parentheses in a `subgraph` label, use explicit quoted-label syntax
  before rendering (for example `subgraph Id["Label (details)"]`). Issue
  `autrun-diua6bg621y8-c7c0b0965b` / PR #2542 showed that an unquoted
  parenthesized subgraph label can fail the Mermaid gate after an otherwise
  clean docs-only Dreamer slice.
- If canonical quality verification reports a recurring-child docs or rule
  slice as failed but the available log is truncated or lacks a concrete
  file/line diagnostic, treat it as a verification-context gap first. Re-run
  `rtk git diff --check` and the exact local gate before changing source, and
  patch only when current evidence points at deterministic source drift.
- If a docs-only recurring child hits an unrelated internal-test failure or
  flake, do not make the PR pass by changing test timeouts, assertions, or
  source behavior. Re-run or record the quality evidence, then split any
  deterministic test/runtime problem into its own issue or delivery slice.
- When multiple recurring-maintenance children are active, check active sibling
  child log summaries before choosing a slice so two children do not target the
  same narrow docs cleanup in parallel.
- When sibling summaries influence slice selection, record that sibling check
  explicitly in the child-run evidence (issue update) so review can confirm the
  run stayed non-overlapping without reconstructing scheduler state.
- Do not substitute changed-file scope, changed paths, or PR diff scope for a
  sibling check. A valid recurring-child sibling check is the active
  sibling-child summary lookup result, or an explicit lookup-unavailable/gap
  note recorded before continuing.
- When a fan-in or conflict-resolution issue discovers two completed children
  that landed the same maintenance slice, consolidate through one canonical
  delivery. Prefer the superset branch when it cleanly contains the duplicate
  child's change, record why the other child is superseded, and do not land the
  overlapping documentation edit twice.
- Measure the baseline signal against the latest `origin/main`, not against a
  possibly stale branch base. A recurring child branched from old main can show
  a non-zero baseline (for example `rg -c '\.claude/rules/' = 13`) for a slice
  that already-merged sibling PRs have driven to zero on current main. The
  sibling check must therefore include *already-merged* sibling work
  (recently-landed PRs touching the same files), not only *active* sibling-child
  summaries. The active-summary lookup cannot see a sibling that already merged.
- If the slice's final diff against current `origin/main` is empty — the target
  edits already exist on main — treat the work as already delivered and abort
  the slice rather than landing an empty/no-op merge. An empty final diff is a
  collision tell, not a successful pass. Pick a different bounded slice (or
  record the no-remaining-work gap) instead of carrying the run to PR.
- When a docs slice enumerates filesystem contents (regression packs, demo
  directories, runsettings files, build targets), compare the doc against the
  actual directory listing as the baseline signal. Treat doc/filesystem drift
  as the bounded slice; do not widen the run to also edit unrelated examples
  in the same file.
- When a docs slice touches first-time setup or build entrypoint guidance,
  verify the required SDK/runtime version from the owning project target
  frameworks before finalizing. If repo projects target a newer TFM such as
  `net10.0`, the top-level README and build/test playbook must name the
  matching SDK prerequisite before showing `make quality` or other build
  commands. Use a targeted baseline/final `rg` signal for the docs-only
  prerequisite check instead of widening into broad builds.
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
- When a cleanup slice removes obsolete architecture-era terminology, start
  with a targeted text inventory over maintained docs, source comments, tests,
  examples, and tracked presentation text. Replace live comments with the
  current owner terms (for example typed AST, IR, expression bytecode, or
  source locations), delete obsolete demo/project artifacts only after their
  docs references are removed, and avoid widening into runtime behavior unless
  the stale term points at live code rather than wording drift.
- When a tooling/docs slice needs agents to discover available options, prefer
  an explicit non-failing inventory command over making agents probe an invalid
  argument just to see an error message. Keep the listing path backed by the
  same computed source that validation uses.
- When a recurring maintenance slice adds or changes top-level Makefile quality
  or test entrypoints, keep the non-failing `make help` inventory aligned with
  those targets and keep `agents/how-to-build-and-test.md` as a short pointer.
  Preserve existing target command bodies unless the slice explicitly changes
  behavior.
- When documenting runner inventory output, state whether it is a static list
  or a live failure snapshot, and define what any displayed counts measure
  before agents use those counts for maintenance scoping.
- For recurring-child context reads, follow
  `docs/rules/agent-context-issues.md` for Faktorial precedence and bounded
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
  `docs/rules/adr-allocation.md`, which is the allocator authority:
  Faktorial learn or knowledge-artifact work must reserve IDs through the
  runtime allocator, not by guessing from a directory scan.
- When the slice does not create an ADR, skip the allocator call entirely. If
  an ADR is required but `faktorial-api adr-next` is unavailable, record that
  environment limitation in the evidence and follow current runtime allocator
  guidance instead of widening into `gh` auth workarounds or host-daemon reads.
- For persistent ADR/rule compaction children, verify overlap against the
  current semantic home first and update that existing document when guidance
  is already covered. Do not create duplicate ADRs, rules, or durable notes
  for guidance that already has an owned home.
- For persistent ADR/rule compaction children, keep the overlap proof and
  final handoff evidence anchored to the same semantic-home boundary used in
  the rule above; do not duplicate the marker-based classification here.
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
  scope, sibling/no-recurrence evidence, and no-unrelated-change note in the
  handoff instead of adding a new source/docs tweak to satisfy an
  already-delivered slice.
- When a recurring-child `Slice check` or build-evidence artifact names changed
  source paths, cross-check those paths against the actual diff before
  finalizing. If review only catches a stale or wrong evidence path after the
  runtime slice is complete, correct the evidence artifact only and keep the
  delivery-source lifecycle intact.
- Keep recurring-child progress updates plain and bounded while work is in
  flight. Reserve machine-readable structured schema output for the actual
  final stage result only; avoid emitting final-response-shaped interim status
  messages
  that can be misclassified as failed stage outcomes in issue logs.
- When a recurring documentation slice updates agent/operator workflow commands,
  align command examples with the current agent invocation contract and
  canonical local gate, including example README run/smoke snippets. In this
  repo, commands that an agent is expected to run should show the `rtk` prefix
  and normal verification should point at `rtk make quality`, while repository
  executable targets themselves must stay wrapper-free per
  `docs/rules/pre-pr-required.md`. Treat `rtk make quality` as local
  build/test evidence only; it does not replace the mandatory pre-PR checklist
  in `docs/rules/pre-pr-required.md`. For piped command examples, check each
  agent-run command segment too: search/filter helpers after `|` should use
  the current `rtk`-wrapped form, such as `| rtk rg ...`, rather than a bare
  `grep`.
- When fixing docs command examples, make the final command copy/paste-safe as
  a single shell invocation when possible. Do not use `rtk cd ...` as a setup
  line or rely on cwd state crossing command examples; prefer explicit path
  flags such as `--project <path>` or, for npm script examples,
  `rtk npm --prefix <path> ...` so the example works exactly as pasted from
  the repo root.
- When a roadmap or documentation-maintenance slice adds or preserves evidence
  links to repository files, check that each cited path exists before finalizing
  the slice. If a planned ADR or report citation does not exist, either cite the
  maintained evidence surface that does exist or keep the roadmap claim
  boundary-only without the missing file reference.
- When a roadmap slice names an ADR, report, or performance note as evidence,
  verify that the cited artifact actually supports the same claim being made,
  not only that the file exists or is recently accepted. If the available
  evidence is adjacent but not claim-specific, keep the roadmap wording
  boundary-only and require a dedicated evidence surface before future agents
  add the stronger claim.
- When a roadmap or documentation-maintenance slice links tracked follow-up
  work, use actual Markdown links to the GitHub issue URLs instead of inline
  code issue IDs. Treat acceptance criteria that ask for "links" as rendered
  link requirements, not just visible `#NNNN` references.
- When a recurring roadmap child starts from issue or investigation context that
  says `docs/roadmap.md` is missing, re-check the file on the current branch
  before treating creation as the slice. If latest main already has the roadmap,
  turn the slice into a bounded refresh of the existing document or an
  evidence-only closeout instead of recreating a stale "missing file" premise.
- When a recurring dreaming-doc child asks to update `docs/dreaming.md`, first
  compare the current document against the actual acceptance criteria before
  editing. If the current branch already has the requested critique, top-down
  runtime architecture, Mermaid diagrams, and roadmap-aligned caveats, close the
  child as evidence-only with baseline/final doc-structure signals instead of
  forcing wording churn into the dream.
- When a roadmap refresh only links already-accepted ADR/rule boundaries,
  failed-trial evidence, or follow-up issue links into `docs/roadmap.md`, the
  learn pass should inventory the existing semantic rule homes before creating
  another knowledge artifact. If the proxy, control-flow, tail-call,
  performance guardrail, or other domain rule already captures the lesson,
  record that overlap and avoid duplicate ADR/rule churn.
- For recurring code-reduction children, prefer deleting one proven-dead
  internal helper or overload over reshaping a surrounding feature. Prove the
  slice with a targeted symbol/caller search immediately before and after the
  edit, pair it with a code-size signal such as `rtk cloc --vcs=git
  --include-lang=C#`, and keep behavior, tests, and recurrence infrastructure
  out of scope unless the exact deletion no longer compiles.
- When a code-reduction slice targets a narrower branch after a broader value
  predicate, first prove the broader predicate's exact semantics at the owning
  runtime type, then delete only the now-unreachable branch. Issue
  `autrun-dit71y9shmaw-7f32a188cb` / PR #2277 removed a
  `prototypeValue.IsNull` branch after `prototypeValue.IsNullish` in static
  super-binding resolution; the safe part was preserving the earlier guard,
  caller contracts, and class static/super proof while removing only the
  unreachable narrow branch.
- If the code-reduction slice targets an empty conditional block, first prove
  the condition expression is metadata-only or otherwise side-effect-free, then
  delete only the empty block. Use a targeted before/after occurrence or
  line-count signal plus `git diff --check`; do not reshape nearby semantic
  code just because the empty guard sits in an active runtime path.
- When a recurring code-reduction child uses named cleanup tools such as
  QuickDup, Roslynator, or cloc, follow each tool's documented discovery path
  before marking it unavailable. A local `dotnet tool list --local` result is
  not enough to prove Roslynator unavailable; check PATH/global tool discovery
  such as `rtk which roslynator` and the documented
  `dnx Roslynator.DotNet.Cli` fallback, then record the exact command and
  failure if analyzer evidence still cannot run. If QuickDup reports
  `No .go files found` during a C# cleanup, treat that as the default
  extension being wrong and rerun with `-ext .cs` before recording QuickDup as
  unavailable or unsuitable evidence.
- Treat QuickDup output as candidate evidence, not as an exhaustive
  no-duplication proof. If a code-reduction handoff names a narrow file, test
  class, or fixture family, inspect that surface manually for parameterizable
  near-duplicates even when QuickDup finds no exact structural clone; record
  both the tool result and the manual slice rationale.
- If the code-reduction slice targets dormant test source files, only delete
  files that are entirely non-compiled/commented-out or otherwise proven absent
  from the test project. Confirm no live references or explicit project includes
  before editing, use file-level line-count evidence for the deleted slice, and
  treat a focused filter with no remaining matching tests as confidence that no
  compiled test contract was removed, not as a behavioral regression proof.
- If the code-reduction slice targets a top-level scratch playground project,
  first prove it is absent from solution files, maintained docs, scripts, and
  external references. When deleting it, remove paired `InternalsVisibleTo`
  grants and tracked generated or binary artifacts in the same slice; do not
  leave orphaned internal-access grants or delete maintained `examples/` or
  `tools/` entries by analogy.
- If the code-reduction slice targets an active scratch or debug smoke test
  with no assertions, delete it only after proving it is output-only or
  non-owning and naming the maintained owner coverage that remains. Do not
  treat `Output.WriteLine`, temporary result collection, or line-count
  reduction as behavioral proof; run a focused filter that covers the retained
  owner plus the touched scratch class when possible.
- If the code-reduction slice targets an active scratch or debug test method
  with assertions, delete it only after naming the maintained owner test that
  already proves the same behavior more directly. Preserve neighboring scratch
  probes when no stronger owner exists, record the owner-test path in the
  handoff, and prove the retained owner plus the touched scratch class with a
  focused filter; line-count reduction alone is not behavioral proof.
- When the code-reduction slice targets duplicated smoke-test fixtures rather
  than dead helpers, extract only the invariant fixture body and leave semantic
  differences explicit through named parameters or separate call sites. Prove
  every affected fixture variant with a focused test filter; line-count
  reduction alone is not behavioral proof.
- When the code-reduction slice targets duplicated JavaScript regression
  harness setup, extract only the invariant harness into a local helper or
  constant. Preserve per-test source bodies, eval boundaries, and named
  semantic differences; prove the affected regression family with a focused
  filter plus the usual code-size and diff checks.
- When the code-reduction slice targets duplicated behavioral dispatch in a
  sensitive runtime path, first look for an existing semantic owner that already
  handles every case. Prefer delegating to that owner over creating a new helper
  or reshaping the surrounding feature, and prove the owner surface with a
  focused test filter plus the usual code-size and diff checks.
- When the code-reduction slice targets paired sync/async standard-library
  prototypes, extract only identical input-validation guards into a named owner
  helper. Keep method-specific TypeError text at the call sites, and keep
  direct sync throw/return paths separate from promise-producing async
  resolve/reject paths unless focused behavior tests prove the observable
  contract unchanged.
- When the code-reduction slice targets duplicated AST or shape-analysis
  traversal, compare the traversal boundaries before merging walkers that look
  structurally similar. If one existing owner is reused as a probe, pin any
  intentionally skipped node families with focused tests, and rescan for dead
  state left behind by earlier review iterations. Issue
  `autrun-ditjxyijy9e0-99946d8d6a` / PR #2405 removed
  `SingleYieldLocator` only after `TryFindSingleYield` reused the
  `SingleYieldRewriter` boundary, added class-expression boundary tests, and
  deleted the obsolete `ShapeCounter.FirstYieldExpression` state.
- When the duplicate code is locale-sensitive formatting, keep the semantic
  split between named and numeric formatting paths explicit. Reuse the existing
  named-format owner when it already handles locale separators, but do not fold
  numeric joins or adjacent overload-specific value extraction into a larger
  formatter rewrite just to reduce lines. Prove the affected formatter families
  with focused tests plus code-size and duplicate-pattern signals.

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

Faktorial issue `autrun-dit1y6xmdd3k-15c84954bd` / PR #2225 removed an empty
`if (function.IsDefaultDerivedConstructor) { }` block from
`FunctionExpressionExtensions.cs`. The useful lesson is to treat empty
conditionals as valid code-reduction slices only after proving the condition has
no observable work, then keep the edit to that dead block and record the
before/after reduction signal.

Issue #1431 / PR #1434 was the same docs/filesystem drift pattern on the
top-level README demo list. `README.md` still omitted `EventQueueDemo`, pointed
at an obsolete parser-era surface, and did not name the maintained Node
host demo path even though those runnable example directories and host scripts
were present. Future README demo-list maintenance should compare the listed
examples against `examples/` first, then update the README as the bounded slice
without widening into demo behavior or broad validation work.

Faktorial issue `agentmanual1780666854898820000` / PR #3222 applied that drift
pattern to obsolete Lisp/S-expression/cons/CPS-era wording. The accepted
delivery removed the dead `examples/SExpressionDemo` project and stale
presentation asset, then retargeted live source comments, tests, README text,
and tracked presentation text to current typed-AST/IR/source-location wording.
Without the targeted-inventory boundary, future cleanup agents can either leave
stale architecture terms in user-visible artifacts or overreach into behavior
changes when the actual defect is wording and dead-example drift.

Issue `autrun-discmtuc3nyg-6afa45ba2d` / PR #2014 exposed the stronger
roadmap-evidence version of the same problem. The delivery initially connected
future Promise constructor/combinator roadmap wording to ADR 0151, but ADR 0151
only covered error-constructor shared initialization argument mapping. The
review fix removed the unsupported Promise-specific attribution and kept the
Promise roadmap item evidence-first until a dedicated Promise evidence surface
exists. Future roadmap refreshes must check citation relevance, not just path
existence or recent acceptance.

Issue `autrun-ditjxyk0fpg8-e5a9846dca` / PR #2404 was a recurring Roadmapper
child that only linked accepted ADR boundaries, failed activation-trial
evidence, and two follow-up issues into `docs/roadmap.md`. The useful learn
boundary was to inventory existing semantic homes and avoid another ADR/rule
when the current recurring-maintenance, performance, and roadmap-architecture
rules already captured the durable policy.

Issue #1239 / PR #1251 was a docs-only maintenance slice triggered by the
pre-existing duplicate ADR prefix `0071`. The useful delivery was adding
prevention guidance to `agents/how-to-build-and-test.md` while leaving the
actual duplicate cleanup and ADR ID allocation policy to the dedicated ADR
allocation rule. This keeps the maintenance child small and prevents future
learn-stage agents from treating a filesystem scan as the allocator.

Issue #1973 / PR #1975 closed the next ADR-allocation edge case in that
operational checklist. The delivery clarified that no allocator call is needed
when a slice does not create an ADR, and that an unavailable
`faktorial-api adr-next` helper should be recorded as an environment limitation
only when an ADR is actually required. Without this rule, recurring children can
widen a docs-only slice into source-host credential work or risky host-daemon
reads even though the durable policy already keeps runtime allocation as the
source of truth.

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

Issue #2030 / PR #2036 was an evidence-only NodeHostDemo
dependency-maintenance child where the README change and dependency decision
were correct, but review sent the build stage back because the handoff lacked
explicit evidence for active sibling lookup, package/lockfile pins,
`npm outdated` wanted/latest output, diff hygiene, and no-unrelated-change
scope. The accepted closeout was to restate those AC-1/AC-2/AC-6/AC-7 signals
in the Build Update, with an evidence-only marker commit, rather than changing
the already-delivered README or package files again.

Issue #1845 / PR #1888 closed the top-level README drift around that same
evidence shape. `README.md` already pointed at the maintenance-child playbook,
but it did not name sibling-summary checks or the stable `Baseline signal`,
`Final signal`, `Sibling check`, `Slice check`, and `Scope note` fields. Future
entrypoint docs should expose those anchors while leaving the detailed
checklist in `agents/how-to-build-and-test.md`, so operators see the contract
without maintaining two full copies.

Issue #2098 / PR #2105 closed the follow-up ownership-pointer gap in that same
top-level README entrypoint. `README.md` pointed at the operational
maintenance-child checklist and evidence shape, but it did not explicitly name
`docs/rules/recurring-maintenance-child-runs.md` as the recurring-child
policy and durable-compaction home. Future entrypoint docs should preserve that
split: point runnable checklist/template ownership at
`agents/how-to-build-and-test.md`, point semantic policy ownership here, and
avoid turning README into a second policy body.

Issue #2519 / PR #2523 applied that same entrypoint-pointer rule to
`AGENTS.md`. The delivery added one concise pointer to the operational checklist
and this durable policy file, instead of copying the checklist or rule body.
Future top-level agent playbooks should keep recurring-child guidance as a
pointer to the owned surfaces so entrypoints stay discoverable without becoming
secondary policy homes.

Issue #2100 / PR #2108 exposed the same mirror problem in the issue/workflow
playbook. `agents/how-to-build-and-test.md` already required `Sibling check`
evidence for recurring children, but `agents/how-to-workflow-and-issues.md`
owned the Faktorial issue-logging flow and did not name that evidence gate.
Review failed AC-4 until the handoff and workflow doc explicitly carried the
sibling-check requirement. Future workflow-doc maintenance should keep that
issue-logging mirror aligned with the owned recurring-child evidence template
and Faktorial runtime read-order rules instead of expecting reviewers or agents
to infer them from another playbook.

Issue #2293 / PR #2301 repeated that evidence gate with a subtler AC-2
failure: the documentation diff was correct, but review rejected a `Sibling
check` that only described changed-file scope. The accepted re-entry performed
the compact summary/API lookup and explicitly recorded that no sibling overlap
data was available in the summary. Future agents must not substitute diff scope
for sibling coordination evidence; record the actual sibling-summary result or
the lookup gap.

Issue #1324 captured a sibling-coordination gap while concurrent issue #1323
was already handling the README stale-link candidate
(`docs/remaining-test262-gaps.md`). The durable lesson for #1324 was not to
duplicate the README slice, but to add an explicit sibling-summary check so
parallel recurring children choose different bounded slices.

Issue #2655 / PR #2658 hardened that sibling check against *already-merged*
siblings and a stale branch base. The child branched from old main and redid
two slices that had already landed: the `dreaming.md` 4-tier execution-model
rewrite (already merged via PR #2647) and the stale `.claude/rules/` →
`docs/rules/` cross-reference fixes in `agents/how-to-build-and-test.md` and
`docs/rules/recurring-maintenance-child-runs.md` (merged concurrently via
PR #2659 and PR #2660). The build stage recorded a clean-looking baseline
(`rg -c '\.claude/rules/' = 13` → final `0`), but that baseline was measured on
the stale worktree; current `origin/main` already had `0`. When PR #2658
finally merged, its diff against main was empty — a pure no-op delivery that
consumed a full investigate + build + review lifecycle. The durable lessons:
measure the baseline against latest `origin/main`, include recently-merged
sibling PRs in the sibling check (the active-summary lookup cannot see a sibling
that already merged), and treat an empty final diff against current main as a
collision-abort signal rather than a successful pass.

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

Issue #2101 / PR #2112 applied the same non-failing inventory pattern to the
repo Makefile quality/test entrypoints. `quality`, `build-internal`,
`test-internal`, and `test-internal-no-build` existed, but there was no `help`
target, so agents had to scan files or guess supported commands. Future
Makefile maintenance should keep `make help` as the discoverable target list
and only update the build/test playbook as a pointer unless command behavior is
the selected slice.

Issue `autrun-ditb2qor9jzc-2ff03f0be6` / PR #2332 refreshed
`docs/dreaming.md` with Mermaid diagrams. Review proved the diagrams only after
writing the fenced bodies to explicit `/tmp/*.mmd` files; an earlier process
substitution check reached Mermaid CLI as empty input, and default
markdownlint-cli output produced style noise even though the repo does not name
markdownlint as a gate. Future docs-only Mermaid slices should validate the
diagram syntax directly and keep optional generic markdown style output from
becoming source churn.

Issue `autrun-diua6bg621y8-c7c0b0965b` / PR #2542 repeated the same Mermaid
gate on a Dreamer runtime-spine diagram. The initial `subgraph Spine[Runtime
spine (execution-owned path)]` label failed Mermaid CLI with a parser error on
the parenthesized label, and the accepted build fix quoted the label before
rerunning extracted-block Mermaid validation. WHY: without this punctuation
guard, future docs-only Mermaid slices can pass diff hygiene and still bounce
at review on deterministic diagram syntax.

Issue #2292 / PR #2300 closed the adjacent first-time setup prerequisite drift:
`README.md` showed the canonical local quality gate but did not tell operators
to install a .NET 10 SDK even though the engine, internal tests, and profiling
tooling target `net10.0`. Future README setup/build docs slices should verify
the current target framework first, then keep the entrypoint concise: name the
required SDK, point at `rtk make help`, and keep `rtk make quality` as the local
gate without changing Makefile behavior or source/test runtime code.

Issue #2348 / PR #2357 closed the same prerequisite drift in
`agents/how-to-build-and-test.md`: the operator playbook listed restore, build,
test, and quality-discovery commands before naming the .NET 10 SDK requirement.
Future setup/build playbook maintenance should keep the playbook aligned with
the README and project TFMs, and should avoid pinning an SDK patch version when
`global.json` does not pin one.

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

Issue #1484 / PR #1487 and Issue #1641 / PR #1644 together clarified the same
ordering rule: recurring-child agents need full issue body/comment context from
the Faktorial issue/dashboard API before relying on compact summaries, because
compact logs are stage history and can omit acceptance criteria, comments, or
human direction that should steer the bounded slice.

Issue #1432 extended that fallback to older completed siblings where the
then-used compact summary helper no longer had history (for example #1403 or
#1365). The durable lesson is to record the unavailable summary attempt as
baseline evidence, then continue from maintained ADR/rule artifacts already
capturing the completed work (for example ADR 0095 and
`docs/rules/expression-bytecode-packing.md`) instead of blocking.

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

The same pair of incidents also established the fallback boundary: after full
issue context and compact runtime history, treat unavailable helper paths as
environment limitations before considering source-host credential gaps as
blockers.

Issue #2418 / PR #2424 compacted those two incidents from separate durable
guidance blocks into the single ordering rule above. The durable lesson is to
merge overlapping incident lessons inside the existing semantic home, while
preserving the original issue traceability and keeping the fallback-boundary
note separate from the context-ordering rule.

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

Issue `autrun-disjoqeep8co-51ba6a407f` / PR #2085 repeated the same pattern in
a recurring code-reduction child. The source change was already the intended
one-file deletion of an obsolete `JsEnvironmentPool.Return(RentedEnvironment?,
ILogger?)` overload, but review sent AC-6 back until the handoff explicitly
recorded the sibling check, `git diff --check`, changed-file scope, and
no-recurrence/no-unrelated-scope note. Future code-reduction re-entries should
repair the evidence fields only and leave the merged delivery branch lifecycle
intact.

Issue `autrun-disq6cxaox20-ce13f02487` / PR #2153 repeated the evidence-field
edge case for a code-reduction child whose implementation was already complete:
review only needed the `Build Update` to explicitly carry the sibling-overlap
gate from the investigation handoff. The corrective build pass was
evidence-only, recorded that only this active Code reduction child overlapped
the slice, and kept the delivery PR lifecycle intact. Future recurring
code-reduction re-entries should repair missing sibling/no-overlap evidence in
the handoff instead of making another source change.

Issue `autrun-diu8sjsjlk3s-e66ed39168` / PR #2529 repeated the evidence-only
pattern for a stale changed-path claim. The runtime code slice only removed an
empty deferred branch from `src/Asynkron.JsEngine/JsTypes/ModuleNamespace.cs`,
but the first build evidence named the obsolete
`src/Asynkron.JsEngine/Ast/Modules/ModuleNamespace.cs` path until review sent
the build back. The corrective commit fixed only the evidence artifact. Future
recurring-child evidence should verify `Slice check` path claims against the
actual diff before handoff, and evidence-path corrections should not reopen the
already-complete source slice.

Issue `autrun-ditawgqprig0-51fe82c197` / PR #2330 removed the unused top-level
`playground/` project, scratch probes, a tracked binary, and the paired
`InternalsVisibleTo("Playground")` grant after investigation proved the surface
was absent from the solution and maintained references. The durable lesson is
that an internal-access grant for a proven-dead scratch project is part of the
dead surface; future code-reduction children should remove both together and
record final absence evidence.

Issue #1814 / PR #1819 applied that compaction pattern to overlapping
`JsValue` object-carrier guidance. The accepted ADRs for array length helpers,
Array prototype result helpers, and number receiver extraction already owned
their helper-specific boundaries, while `docs/rules/jsvalue-core-values.md`
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

Issue #2171 / PR #2176 repeated the same scope-control failure mode through a
docs-only README status slice: after the local quality gate exposed an async
test timeout-shaped failure, the branch briefly widened into longer async test
timeouts before review forced a revert and merged only the README wording fix.
Future recurring documentation children must not absorb unrelated test-timeout
or runtime-stability repairs just to get a docs slice through verification; keep
that evidence in the handoff and split deterministic test work into a separate
issue.

Issue #1882 refined the same policy to be stage-neutral: schema-shaped output
belongs only in the actual final stage response, not just in build-stage
handoffs. The evidence was investigate-stage progress updates that resembled a
final structured reply and polluted run-state interpretation before
implementation finished.

Issue #2029 / PR #2035 confirmed the operational checklist mirror must carry
that same stage-neutral boundary. The semantic home already owned the rule, but
the checklist wording was easier to read as build-stage-only guidance. Future
compaction passes should verify both the semantic home and the operational
mirror with a targeted overlap signal instead of adding a new incident-specific
rule.

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

Issue #1974 / PR #1977 found the same stale command shape in the Test262
harness-policy rule, where a historical focused proof still showed bare
`dotnet test` even though agents must run shell commands through `rtk`. Future
workflow-doc slices should treat durable rule examples as agent-facing command
surfaces too: fix the stale invocation in place, keep the historical proof
meaning intact, and avoid widening the slice into Test262 harness behavior.

Issue #2518 / PR #2521 found the same command-wrapper drift inside a pipeline:
the Test262 list-tests example started with `rtk dotnet test` but still piped
into bare `grep`. Future docs maintenance should scan the whole shell pipeline,
not only the first command, and convert agent-facing search helpers to the
current wrapped command form such as `rtk rg`.

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

Issue #2172 / PR #2177 repeated the command-example drift in an example-specific
README: the Avalonia SVG Browser demo run and smoke-test snippets used bare
`dotnet run --project examples/AvaloniaSvgBrowserDemo` lines even though they
are copy/paste operator commands. Future example README maintenance should
treat run/smoke snippets as agent-facing command surfaces and normalize them in
place to the repo invocation contract without changing demo behavior.

Issue #1717 / PR #1719 applied that same command-shape lesson to the top-level
README demo section. Several examples still used `cd examples/<Demo>` followed
by `rtk dotnet run`, which made the snippet depend on shell state instead of
being a direct runnable command. Future README/demo command slices should prefer
`rtk dotnet run --project examples/<Demo>` when the example can be expressed as
one stable invocation.

Issue #2032 / PR #2046 found the same command-shape drift inside
`examples/NodeHostDemo/README.md`: the README still showed bare `dotnet run`,
bare `npm`, and `cd examples/NodeHostDemo` setup lines. Future example-doc
slices should keep both .NET and package-script commands runnable from the repo
root, using `rtk dotnet run --project ...` and `rtk npm --prefix ...` rather
than relying on caller cwd state.

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

Issue `autrun-diteiepqykvs-23ac6b4870` repeated that live-doc premise for the
dreaming architecture document. The run contract asked agents to improve
`docs/dreaming.md`, but current main already contained the explicit critique,
top-down runtime/product architecture, two Mermaid diagrams, and roadmap caveats
the handoff required. The correct delivery was an evidence-only closeout with
matching baseline/final doc-structure checks and no changed paths. Without this
rule, future Dreamer recurrence children can manufacture subjective wording churn
after the branch already satisfies the concrete acceptance criteria.

Issue `autrun-dirzat33gjzc-e6a91380e8` / PR #1910 refreshed the roadmap with
ADR 0137, ADR 0138, and ADR 0139 boundaries after those decisions and their
domain rules already existed. The learn-stage lesson was not another ADR; it
was to verify `docs/rules/ecmascript-proxy-realm-errors.md`,
`docs/rules/ecmascript-labeled-statements.md`, and
`docs/rules/proper-tail-calls.md` first, then keep the knowledge pass scoped
to compaction instead of duplicating the accepted proxy, control-flow, and
tail-call rules.

Issue `autrun-dis251iagv80-00c9769c91` / PR #1933 repeated that roadmap-link
pattern for ADR 0140, ADR 0141, and ADR 0142. The accepted semantic homes were
already `docs/rules/performance-profiling-guardrails.md` for trampoline and
destructuring profile lessons, plus `docs/rules/jsvalue-core-values.md` for
HTMLDDA string-coercion precedence. Future learn passes for roadmap refreshes
should record that overlap and avoid creating duplicate ADR/rule artifacts when
the delivery only surfaced already-accepted evidence in `docs/roadmap.md`.

Issue `autrun-dit71yapajf4-92057cb61e` / PR #2284 repeated the same compaction
boundary for failed performance trials: the roadmap refresh linked the
`simplearithmetic` and `classdef` failed-trial evidence plus follow-up issues
#2281 and #2282, while ADR 0214, ADR 0216, and
`docs/rules/performance-profiling-guardrails.md` already owned the durable
negative-trial lessons. Future learn passes should record that overlap instead
of creating another ADR or one-off rule just because the roadmap gained
follow-up links.

Issue `autrun-disf6pj6b97s-d66702792b` / PR #2042 exposed a roadmap
traceability gap in the same recurring-docs family: the delivery selected and
created follow-up issues #2040 and #2041, but review sent AC-5 back because the
roadmap used inline issue references instead of actual links. The accepted fix
converted those references to GitHub issue URLs. Without this rule, future
roadmap children can appear to satisfy "link follow-up work" acceptance
criteria while leaving readers without durable rendered links.

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

Issue `autrun-disooxlrk6co-208b40e934` / PR #2138 applied the same boundary to
duplicated generator/yield* regression harness setup. The safe slice extracted
only the repeated minimal `assert.sameValue` helpers into
`BasicAssertSameValueHarness`, kept the Test262-derived scenario bodies and
separate eval boundaries intact, recorded `GeneratorTests.cs` reductions from
6684 to 6650 lines and 5380 to 5346 counted C# lines, and proved the affected
family with the focused `FullyQualifiedName~Generator_YieldStar` filter. Future
regression-harness reductions should share identical setup, not delete coverage
or collapse scenario-specific scripts only to reduce lines.

Issue `autrun-ditjxyijy9e0-99946d8d6a` / PR #2405 applied code reduction to
duplicated single-yield AST traversal in `Ast/ShapeAnalyzer`. Review caught
that the first reduction merged discovery into `ShapeCounter`, whose
class-expression traversal boundary differed from the old locator and the
rewriter. The accepted slice reused `SingleYieldRewriter` as the single-yield
probe, added focused class-expression boundary tests, removed the separate
locator, and then deleted leftover `FirstYieldExpression` state. Future
AST-walker reductions should treat traversal boundaries as behavior, not as
mechanical duplication, and should scan for dead state after review-driven
design changes.

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

Issue `autrun-disnezru01ow-917e81d28d` / PR #2126 was the asserted active
scratch-test variant of that boundary. The safe slice removed only
`IntlScratchTests.InspectSupportedValuesCoercion` after confirming that
`IntlSupportedValuesTests.SupportedValuesCoerceKeysWithToString` already
compared direct `Intl.supportedValuesOf("calendar")` output against both
`new String("calendar")` and plain-object `toString()` coercion. The build kept
the other `IntlScratchTests` probes because no stronger owner was identified,
recorded the `IntlScratch.cs` line-count drop from 105 to 64 C# lines, and
proved the retained owner plus scratch class with the focused
`IntlScratchTests|IntlSupportedValuesTests` filter. Future active scratch-test
reductions should make that retained-owner proof explicit before deleting
asserted tests.

Issue `autrun-disu06f97he8-c70475043e` / PR #2193 was the no-assert active
scratch-smoke variant of that boundary. The safe slice removed only
`IntlScratchTests.ListFormatExists`, which evaluated `Intl.ListFormat`, captured
formatted strings, and wrote the result to output without assertions. The build
handoff named retained `Intl.ListFormat` constructor coverage in
`IntlLocaleDebugTests.IntlConstructors_TolerateUnsupportedUnicodeExtensionValuesInLocale`,
recorded the `IntlScratch.cs` line-count drop from 73 to 57 lines, ran
`git diff --check`, and proved the retained owner plus scratch class with the
focused `IntlScratchTests` plus
`IntlLocaleDebugTests.IntlConstructors_TolerateUnsupportedUnicodeExtensionValuesInLocale`
filter. Future no-assert scratch-smoke reductions should use that owner-proof
shape instead of preserving output-only probes or claiming coverage from
printed values.

Issue `autrun-diskyo8ie1gg-e962a681ea` / PR #2113 applied recurring code
reduction to duplicated async-generator step settlement. `CreateStepPromise`
carried the same `Yield`/`Completed`/`Throw`/`Pending` switch already owned by
`ResolveFromStep`, so the safe reduction was to delegate to that existing
semantic owner, record the file-level C# line count drop from 233 to 218, run
`git diff --check`, and prove the async-generator owner surface with
`AsyncGeneratorTests` (19 tests). Future runtime dispatch reductions should use
that evidence shape; line-count reduction alone is not enough proof when the
dispatch path settles promises or other observable runtime behavior.

Issue `autrun-disrgarna6fc-5f88c8087c` / PR #2168 removed redundant
ArrayBuffer and SharedArrayBuffer constructor fallback properties. The build
handoff said Roslynator was unavailable after checking only the local tool
manifest, but review found a global Roslynator install and then completed the
documented `dnx Roslynator.DotNet.Cli` fallback with zero diagnostics for the
touched constructor files. Future code-reduction children should treat tool
availability as the documented discovery chain, not as a single manifest check,
so static-analysis evidence is accurate without reopening an already-correct
delivery.

Issue #1971 / PR #1976 clarified a persistent ADR/rule compaction child whose
issue context included automation markers such as `Part of automation template`,
`trigger=automation recurrence`, `trigger=persistent recurrence`, and later
`local:adr-rule-compaction`. Issue #2221 / PR #2230 continued the same boundary
for recurrence-normalization wording in persistent ADR/rule compaction handoffs.
The durable lesson is one rule, not several: those markers classify a single
runnable bounded child delivery that should normalize the recurring-child
evidence contract in this existing semantic home. They are not prompts to add
persistent setup, scheduler behavior, recurrence infrastructure, or another ADR
for the same classification boundary. Because the first #2221 build pass missed
the literal `trigger=persistent recurrence` marker, future agents should carry
named recurrence or compaction markers through both the top-level boundary and
this traceability note before claiming the slice complete.

Issue `autrun-dit4i2lxx7hk-821f1277ce` / PR #2259 consolidated four
`PerfDebugging` for-loop smoke tests into one parameterized theory. The narrow
QuickDup check did not report exact structural clones, but manual inspection of
the named file found repeated script, timing, and engine setup that could be
shared while preserving all loop ranges and debug-logger coverage as explicit
`InlineData`. Future code-reduction children should treat a QuickDup no-match
as non-exhaustive for tiny fixture duplication, keep the variant data visible,
and prove every affected fixture variant with the focused test filter.

Issue `autrun-ditio0o02i68-fde9ef00e5` / PR #2389 applied recurring code
reduction to `IntlDateTimeFormatPrototype` named-month date joining. The safe
slice replaced only the two duplicate named-month `StringBuilder` loops in the
DateTimeOffset and ProlepticDateTime `FormatLocaleDateString(...)` overloads
with `JoinNamedCalendarParts(order, parts)`, while leaving numeric-date joins
as `string.Join(numericSep, parts.Select(p => p.value))`. The proof paired a
C# line-count drop, a lower Intl QuickDup pattern count, and focused Temporal
plus proleptic Gregorian DateTimeFormat tests. Future Intl formatting
reductions should preserve that named-versus-numeric boundary and avoid
reshaping calendar value extraction when an existing separator-aware owner is
available.

Issue `autrun-diu4yqab0uq8-1e1088957e` / PR #2494 applied recurring code
reduction to `DisposableStackPrototype` and `AsyncDisposableStackPrototype`.
The accepted slice shared only the duplicated callable-disposer guard through
`DisposableStackHelper.RequireCallable(...)`, kept prototype-specific TypeError
messages at each `adopt`/`defer` call site, and left `dispose` separate from
`disposeAsync` because one completes synchronously and the other resolves or
rejects a promise. The build evidence also marked QuickDup unsuitable after a
default `.go` invocation returned `No .go files found`; future C# cleanup runs
should rerun QuickDup with `-ext .cs` before treating that output as a tooling
limitation.
