# AgentContext: Persistent Memory via GitHub Issues

For any significant work session, maintain a GitHub issue as persistent memory.

When Faktorial Source Context or the Faktorial API is supplied, that runtime context takes precedence for issue details, comments, stage history, logs, and write actions. In that case, use the provided Faktorial helpers/API instead of `gh`, and do not report missing Docker `gh` auth as a blocker. The `gh` commands below are for ordinary local/manual GitHub workflows, or for explicit fallback cases where Faktorial context does not cover the operation.

When gathering Faktorial evidence, keep reads output-bounded. Start with compact
helpers or API summaries, split unrelated reads into small commands, scope
searches to specific existing paths, avoid broad `.faktorial/worktrees` scans
unless the task explicitly requires that surface, and use targeted raw-log
snippets with line caps when summaries are insufficient.

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
