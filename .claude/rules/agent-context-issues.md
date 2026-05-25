# AgentContext: Persistent Memory via GitHub Issues

For any significant work session, maintain a GitHub issue as persistent memory.

When Faktorial Source Context or the Faktorial API is supplied, that runtime context takes precedence for issue details, comments, stage history, logs, and write actions. In that case, use the provided Faktorial helpers/API instead of `gh`, and do not report missing Docker `gh` auth as a blocker. The `gh` commands below are for ordinary local/manual GitHub workflows, or for explicit fallback cases where Faktorial context does not cover the operation.

When gathering Faktorial evidence, keep reads output-bounded. Start with compact
helpers or API summaries, split unrelated reads into small commands, scope
searches to specific existing paths, avoid broad `.faktorial/worktrees` scans
unless the task explicitly requires that surface, and use targeted raw-log
snippets with line caps when summaries are insufficient.

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
2. Create closed: `gh issue create --title "AgentContext: ..." --body "..." && gh issue close <id>`
3. Update the issue body whenever significant findings emerge
4. Keep it concise but complete - this is your memory across sessions
5. Use `gh issue edit` to update the body, not comments
6. Search for existing AgentContext issues before creating new ones:
   ```bash
   gh issue list --search "AgentContext: in:title" --state closed
   ```

## Why

Issue #1301 / PR #1310 was a recurring-maintenance child that codified
Faktorial output hygiene after worker logs showed oversized and repeated command
output while agents gathered local issue context. The durable lesson is not to
avoid evidence; it is to gather the same evidence through bounded helpers,
small scoped commands, and capped snippets so telemetry stays useful and future
reviewers can see the actual signal.

Issue #1331 was a `main is red: 8b1273e` repair where Faktorial mainverify
recorded `dotnet test tests/Asynkron.JsEngine.Tests` as failed, but the
build-stage reran that exact command and the timeout-shaped variant on the
current worktree and both passed 3919 tests. No source change was warranted; the
useful durable rule is to reprove main-health failures before patching and to
close stale/transient reds with evidence instead of changing unrelated owners.
Issue #1400 repeated the same pattern for `main is red: 391ce40`: the stored
mainverify status pointed at `dotnet build Asynkron.JsEngine.sln`, but the
build-stage reran that exact command on the current issue branch and it passed
with no source diff. That incident confirms the rule covers build-health reds as
well as test-health reds.
Issue #1568 repeated the test-health variant for `main is red: c08723b`: the
stored mainverify status pointed at `dotnet test tests/Asynkron.JsEngine.Tests`,
but the build-stage reran the exact command on the current issue branch and it
passed 4021 tests with a clean worktree. That incident confirms agents should
continue stopping on proven stale/transient reds instead of inventing a nearby
implementation patch just because the trigger was p0.
Issue #1708 repeated the build-health variant for `main is red: d944f3a`: the
stored mainverify excerpt only showed restore output from
`dotnet build Asynkron.JsEngine.sln`, but the build-stage reran that exact
command and it passed with no changed paths. That incident confirms that
truncated failure excerpts must still be reproved before source edits, and a
clean exact-command pass should close as stale/transient evidence rather than a
nearby repair.

Issue #1773 was a fan-in task for the shared signal
`repair_kind:merge_conflict` across source issues #1745 and #1756. By the time
the fan-in lane reached build/learn, both source repairs had already progressed
and local source-branch comparisons against `origin/main` had no remaining
implementation diff. The durable lesson is that a fan-in conflict signal is a
trigger to reprove current source-issue and branch-diff state, not permission to
invent a nearby patch or reopen already-absorbed source work.
