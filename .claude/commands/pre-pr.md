---
description: Run mandatory pre-PR checks before creating a pull request
allowed-tools: Bash(roslynator:*), Bash(dotnet:*), Bash(quickdup:*), Bash(gh:*)
---

# Pre-PR Checklist

**MANDATORY**: Run this before creating any PR to GitHub.

## Execute All Steps In Order

### Step 1: Roslynator Fix

```bash
roslynator fix src/Asynkron.JsEngine
```

Then verify compilation:
```bash
dotnet build src/Asynkron.JsEngine
```

**If build fails**: Fix the issues and rerun roslynator before proceeding.

### Step 2: Run Full Internal Unit Tests

```bash
dotnet test tests/Asynkron.JsEngine.Tests
```

**Handle failures:**
- **Flaky tests**: Rerun to confirm
- **Broken tests**: Fix before proceeding
- **All tests MUST pass** before moving to step 3

### Step 3: Check for Code Duplication

```bash
quickdup --path src/Asynkron.JsEngine --ext .cs --select 0..20 --min 2 --exclude ".g."
```

**If new duplications are found:**
1. Refactor to eliminate duplications
2. Go back to Step 1 (roslynator fix)
3. Repeat until no new duplications

### Step 4: Format Code

```bash
dotnet format src/Asynkron.JsEngine
```

### Step 5: Final Verification

Confirm all checks passed:
- [ ] Roslynator fix applied
- [ ] Code compiles cleanly
- [ ] All unit tests pass
- [ ] No new code duplications
- [ ] Code formatted

### Step 6: Create PR

Only after ALL checks pass:
```bash
gh pr create --title "..." --body "..."
```

## Important

- Do NOT skip any steps
- Do NOT create PR if any step fails
- Iterate until all checks pass
