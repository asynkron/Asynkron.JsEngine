---
description: Create a git worktree for a feature and cd into it
allowed-tools: Bash(git:*)
argument-hint: <featurename>
---

Create a git worktree for the specified feature and change into that directory.

Execute the following commands:

```bash
git worktree add ../Asynkron.JsEngine-$ARGUMENTS -b feature/$ARGUMENTS
cd ../Asynkron.JsEngine-$ARGUMENTS
```

After creating the worktree, confirm the new working directory with `pwd`.

## Examples

Create a worktree for a new feature:
```
/wt type-narrowing
```

This will:
1. Create `../Asynkron.JsEngine-type-narrowing` worktree
2. Create branch `feature/type-narrowing`
3. Change into the new worktree directory

## Cleanup

When done with the feature (after PR is merged), run from the main repo:
```bash
git pull origin main
git worktree remove ../Asynkron.JsEngine-<featurename> --force
git branch -D feature/<featurename>
```
