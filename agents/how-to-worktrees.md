# Git Worktree Workflow

## Why
Isolate feature work from the main working directory; allows parallel branches and easy cleanup.

## When to Use Worktrees
**Always use a worktree for:**
- Bug fixes (especially multi-file changes)
- New features
- Any task spawned to a background coder agent
- Refactoring work

**Skip worktrees only for:**
- Single-line typo fixes
- Documentation-only changes
- Trivial config updates

## Create a Worktree
```bash
git worktree add ../Asynkron.JsEngine-<feature> -b feature/<branch-name>
# example:
git worktree add ../Asynkron.JsEngine-typing -b feature/type-narrowing
```

## Work in the Worktree
1. Make changes in the new directory.
2. Build/test: `dotnet build && dotnet test tests/Asynkron.JsEngine.Tests`.
3. Commit changes.
4. Push and open PR: `git push -u origin feature/<branch-name> && gh pr create`.
5. Merge: `gh pr merge <pr-number> --squash`.

## Cleanup After Merge
```bash
git pull origin main
git worktree remove ../Asynkron.JsEngine-<feature> --force
git branch -D feature/<branch-name>
```

## Naming Suggestions
- `Asynkron.JsEngine-typing` (type narrowing)
- `Asynkron.JsEngine-perf` (performance work)
- `Asynkron.JsEngine-fix-123` (bug fix for issue #123)

## Spawning Background Coder Agents
When launching a background coder agent for a big task:

1. **First create the worktree:**
   ```bash
   git worktree add ../Asynkron.JsEngine-fix-420 -b feature/fix-strict-mode-scoping
   ```

2. **Tell the agent to work in that directory** in your prompt:
   ```
   Work in the worktree at ../Asynkron.JsEngine-fix-420
   When done, commit changes and create a PR (do NOT push to main directly)
   ```

3. **After agent completes**, review and merge the PR:
   ```bash
   gh pr merge --squash
   ```

4. **Evaluate agent findings and act on them:**
   - Read the agent's output summary carefully - it contains valuable findings
   - If subtasks were blocked, document the blockers on the relevant GitHub issues
   - If new issues were discovered, create GitHub issues for them
   - Update the roadmap to reflect current progress and blockers
   - Close any issues that were completed
   - Link related issues together (e.g., "Blocked by #X", "Related to #Y")

   This step is critical - the agent's investigation findings are lost if not documented!

5. **Cleanup:**
   ```bash
   git pull origin main
   git worktree remove ../Asynkron.JsEngine-fix-420 --force
   git branch -D feature/fix-strict-mode-scoping
   ```

This workflow ensures all big changes go through PR review.

## Quick Minimal Template (old flow)
If you need a very quick setup, you can also:
1. Ask for the task ($TASK) and extract a short name ($NAME).
2. `git worktree add ../jsengine-$NAME -b jsengine-$NAME`
3. `cd ../jsengine-$NAME`
4. Create `todo.md` with:
   ```
   # $NAME
   $TASK
   ```
