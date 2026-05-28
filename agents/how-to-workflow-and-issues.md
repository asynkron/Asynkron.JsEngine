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
- Runtime read order for Faktorial agent runs:
  - 1) Use supplied Source Context first.
  - 2) Use supplied Faktorial HTTP API endpoints next.
  - 3) Prefer compact summaries before raw logs.
  - 4) Use bounded raw-log snippets only when those sources are still insufficient.
- Do not run the host `faktorial` binary for issue, log, or state reads from an agent runtime.
- Compact summary example:
  - `curl -fsS "${FAKTORIAL_URL:-http://127.0.0.1:8787}/api/logs/<task-id>/summary"`
- Use the local/manual `gh` CLI examples below only outside Faktorial agent runtime, or only in Faktorial when explicitly instructed and the runtime context does not provide the needed operation.
- In Faktorial runs, keep evidence collection output-bounded:
  - Prefer compact helpers or API summaries before opening raw logs or broad state snapshots.
  - Split unrelated reads into small commands instead of large chained `sed`/`rg`/log commands.
  - Scope `rg` to specific existing paths and avoid broad `.faktorial/worktrees` searches unless the task explicitly requires that path.
  - When raw logs are required, use narrow snippets (targeted patterns and line caps) rather than full dumps.
  - For recurring maintenance child runs, ensure `## Build Update` explicitly includes a `Sibling check:` line (or states the lookup was unavailable) so evidence gates can verify overlap avoidance.
- In Faktorial issue-stage runs, keep in-flight progress updates as plain prose; reserve machine-readable structured schema output for the actual final stage result.
- For each session, update an existing issue or create one if none fit.
- Local/manual `gh` examples (non-Faktorial runtime):
  - Find/confirm an issue: `rtk gh issue list -S "keyword"` and `rtk gh issue view <number>`
  - Create if needed: `rtk gh issue create -t "Title" -b "Context, plan, next steps"`
  - Update progress: `rtk gh issue comment <number> -b "Update text"`
  - Patch an existing comment: `rtk gh api --method PATCH repos/asynkron/Asynkron.JsEngine/issues/comments/<comment_id> -f body="$(cat /tmp/body.txt)"`
- Link related work with markdown references (e.g., `Related to #344`, `Blocked by #123`).
- Always summarize changes, remaining work, and test results so the next agent can resume quickly.
