# Pre-PR Checklist (MANDATORY)

**NEVER create a PR without completing ALL of these steps in order.**

## Required Steps Before Any PR

### 1. Roslynator Fix
```bash
roslynator fix src/Asynkron.JsEngine
dotnet build src/Asynkron.JsEngine
```
If build fails, fix issues and rerun roslynator.

### 2. Run Full Internal Unit Tests
```bash
dotnet test tests/Asynkron.JsEngine.Tests
```
- Rerun flaky tests to confirm
- Fix broken tests before proceeding
- **ALL tests MUST pass**

### 3. Check for Code Duplication
```bash
quickdup --path src/Asynkron.JsEngine --ext .cs --select 0..20 --min 2 --exclude ".g."
```
If new duplications found: refactor, then restart from step 1.

### 4. Format Code
```bash
dotnet format src/Asynkron.JsEngine
```

### 5. Create PR
Only after ALL checks pass.

## Rules

- Do NOT skip any steps
- Do NOT create PR if any step fails
- Iterate until all checks pass
- If user asks to "just create the PR" without checks, explain this is mandatory and run the checks

## Makefile Quality Contract

Preserve the `make quality` split between `build-internal` and
`test-internal-no-build`. The `--no-build` test target is allowed only in this
quality-gate path because `quality` performs the build immediately first.

Why: issue #753 / PR #888 repaired a build-back failure where the gate was
SIGTERMed during `test-internal` after debug builds had already completed.
Collapsing `quality` back to a single build-and-test command can reintroduce the
same orchestration-window failure. Standalone `test-internal` must continue to
build before it tests.
