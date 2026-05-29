# AgentContext: Persistent Memory via GitHub Issues

For any significant work session, maintain a GitHub issue as persistent memory.

When Faktorial Source Context or the Faktorial API is supplied, that runtime context takes precedence for issue details, comments, stage history, logs, and write actions. In that case, use the provided Faktorial helpers/API instead of `gh`, and do not report missing Docker `gh` auth as a blocker. The `gh` commands below are for ordinary local/manual GitHub workflows, or for explicit fallback cases where Faktorial context does not cover the operation.

When gathering Faktorial evidence, keep reads output-bounded. Start with compact
helpers or API summaries, split unrelated reads into small commands, scope
searches to specific existing paths, avoid broad `.faktorial/worktrees` scans
unless the task explicitly requires that surface, and use targeted raw-log
snippets with line caps when summaries are insufficient.

Keep in-flight Faktorial progress updates as plain prose, not final-response
schema JSON. Reserve machine-readable structured stage results for the actual
final response so the worker does not misclassify status updates as failed
stage outcomes.

For agent runtime context gathering, use this practical order: supplied Source
Context or issue/dashboard API details first, compact `/api/logs/<issue>/summary`
next, and bounded raw-log snippets only when those are still insufficient.
Agents must not run the host `faktorial` binary for issue, log, or state reads;
older guidance that suggests `faktorial issue` or `faktorial log-summary` is
stale for agent runtime context reads.

For `main is red` or main-health repair issues, re-run the exact failing
main-health command on the current worktree before changing source code. Treat
the stored mainverify status as a trigger and evidence pointer, not as proof
that the failure still reproduces after later branch movement or transient
environment cleanup. If the exact command now passes and the worktree is clean,
stop without inventing a nearby implementation patch and report the issue as
stale or transient.

For fan-in or repair-lane issues whose shared signal is a merge conflict, prove
the current source-issue state and branch diff before doing repair work. Use
Faktorial issue/API/log context first, then compare the named source branches
against the current local `origin/main`; do not treat local branch existence,
stale Source Context, or an old failed merge/deploy signal as proof of a live
conflict. If the source issues have already progressed and the source branches
carry no remaining implementation diff, close the fan-in task as evidence-only
rather than creating an unrelated source patch.

## Naming Convention

- **GitHub issue work**: `AgentContext: issue/NNN` (e.g., `AgentContext: issue/465`)
- **General topics**: `AgentContext: <descriptive title>` (e.g., `AgentContext: byte code emit`)

## When to Create/Update

Create an AgentContext issue when:
- Starting work on a GitHub issue
- Researching a concept or area of the codebase
- Debugging a complex problem
- Any task that spans multiple turns or sessions

## Content Structure

Update the issue **body** (not comments) with:

```markdown
## Summary
Brief description of what this context tracks

## Findings
- Key discoveries and observations
- Code locations: `file.cs:123`
- Patterns identified

## Hypotheses
- [ ] Hypothesis 1 - status/outcome
- [x] Hypothesis 2 - confirmed/rejected with reason

## Test Results
Relevant test outputs, failure patterns

## Next Steps
What remains to be done
```

## Rules

1. **Keep AgentContext issues closed** - they're hidden from users but still readable/writable
2. Create closed: `rtk gh issue create --title "AgentContext: ..." --body "..." && rtk gh issue close <id>`
3. Update the issue body whenever significant findings emerge
4. Keep it concise but complete - this is your memory across sessions
5. Use `rtk gh issue edit` to update the body, not comments
6. Search for existing AgentContext issues before creating new ones:
   ```bash
   rtk gh issue list --search "AgentContext: in:title" --state closed
   ```

## Why

Issue #1301 / PR #1310 was a recurring-maintenance child that codified
Faktorial output hygiene after worker logs showed oversized and repeated command
output while agents gathered local issue context. The durable lesson is not to
avoid evidence; it is to gather the same evidence through bounded helpers,
small scoped commands, and capped snippets so telemetry stays useful and future
reviewers can see the actual signal.

Issue #2031 / PR #2034 refined that Faktorial evidence rule after recurrent
child guidance still left the read order and host-daemon boundary ambiguous.
The durable lesson is that agent runtime context must start with supplied
Source Context or the issue/dashboard API, use compact log summaries next, and
treat older host `faktorial issue` or `faktorial log-summary` instructions as
stale so agents do not accidentally start or depend on the daemon binary.

Issue #2293 / PR #2301 exposed a narrower command-example drift in this same
surface: local/manual GitHub CLI examples still appeared as runnable bare
`gh ...` commands even though Faktorial agents should use supplied context/API
first and all shell commands in this repo should be `rtk` wrapped. The durable
lesson is to label fallback GitHub CLI examples as local/manual and write
agent-runnable examples as `rtk gh ...`, so future agents do not confuse those
examples with normal Faktorial issue reads or violate the repo command
contract.

Issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-6-doc-f034e52976`
learn-stage logs showed final-response-shaped JSON emitted as interim progress
while preparing the post-PR knowledge pass. The worker recorded those messages
as failed stage outcomes before the actual learn decision. The durable lesson is
to keep interim Faktorial updates human-readable and reserve schema-shaped JSON
for the final stage result only.

Issue #2421 / PR #2428 found that this output-boundary rule was already durable
here and in recurring-child guidance, but the workflow issue-logging playbook
still lacked the same boundary. The durable lesson is to mirror cross-cutting
Faktorial output rules into `agents/how-to-workflow-and-issues.md` when that
playbook is an agent entrypoint, while keeping this file as the semantic home.

Issues #1331, #1400, #1568, and #1708 all repeated the same `main is red`
pattern across test-health and build-health variants: the stored mainverify
status pointed at a failing command, but the build-stage reran that exact
command on the current issue branch and it passed with no source diff or changed
paths. The durable rule across all four incidents is to reprove main-health
failures before patching, close stale/transient reds with evidence, and never
invent a nearby repair just because the trigger was p0. Truncated failure
excerpts must also be reproved before any source edits are made.

Issue #1773 was a fan-in task for the shared signal
`repair_kind:merge_conflict` across source issues #1745 and #1756. By the time
the fan-in lane reached build/learn, both source repairs had already progressed
and local source-branch comparisons against `origin/main` had no remaining
implementation diff. The durable lesson is that a fan-in conflict signal is a
trigger to reprove current source-issue and branch-diff state, not permission to
invent a nearby patch or reopen already-absorbed source work.
