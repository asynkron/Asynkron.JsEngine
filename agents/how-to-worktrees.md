# Git Worktree Workflow

## Why
Isolate feature work from the main working directory; allows parallel branches and easy cleanup.

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
