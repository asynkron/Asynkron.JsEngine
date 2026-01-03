# Workflow and GitHub Issues

## Rolling Next Steps
- `continue.md` tracks in-progress priorities. When finishing items, remove them and add the new next steps.

## GitHub Issue Logging (persistent working memory)
- Treat GitHub issues as the long-lived log of progress, research, and reasoning.
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
