# Workflow and GitHub Issues

## Big Tasks: Use Worktrees
For any non-trivial bug fix or feature (especially when spawning background coder agents):
1. **Create a worktree** before starting work - see [how-to-worktrees.md](how-to-worktrees.md)
2. Work in the isolated branch
3. Push and create a PR for review
4. Merge via PR (squash)
5. Cleanup the worktree

This ensures code review and avoids pushing directly to main.

## After Background Agent Completes
When a coder agent finishes, **always evaluate its findings and act on them**:

1. **Read the agent output carefully** - it contains investigation findings, blockers, and recommendations
2. **Document blockers** - if tasks couldn't be completed, add comments to the relevant issues explaining why
3. **Create new issues** - if the agent discovered new problems or prerequisites, create issues for them
4. **Update the roadmap** - run `/roadmap` to refresh the roadmap with current progress
5. **Close completed issues** - mark finished subtasks as closed
6. **Link related issues** - add "Blocked by #X" or "Related to #Y" references

This step is **critical** - agent investigation findings are lost if not documented in GitHub issues!

## GitHub Issue Logging (persistent working memory)
- Treat GitHub issues as the long-lived log of progress, research, and reasoning.
- When running inside Faktorial and Source Context or the Faktorial API is supplied, use that context/API as the authoritative source for issue details, comments, stage history, logs, and write actions. Do not treat missing `gh` CLI auth in that runtime as a blocker.
- Use the `gh` CLI patterns below for ordinary local/manual GitHub workflows, or only in Faktorial when explicitly instructed and the runtime context does not provide the needed operation.
- For each session, update an existing issue or create one if none fit.
- Find/confirm an issue:
  - `gh issue list -S "keyword"`
  - `gh issue view <number>`
- Create if needed:
  - `gh issue create -t "Title" -b "Context, plan, next steps"`
- Update progress:
  - `gh issue comment <number> -b "Update text"`
  - Patch an existing comment: `gh api --method PATCH repos/asynkron/Asynkron.JsEngine/issues/comments/<comment_id> -f body="$(cat /tmp/body.txt)"`
- Link related work with markdown references (e.g., `Related to #344`, `Blocked by #123`).
- Always summarize changes, remaining work, and test results so the next agent can resume quickly.
