# Build and Test

## Standard Commands
```bash
# Restore dependencies
dotnet restore

# Build everything
dotnet build

# Main test suite
dotnet test tests/Asynkron.JsEngine.Tests
```
Do not use `--no-build` for ad hoc test runs; keep code compiled with latest
changes.

Exception: `make quality` intentionally runs `build-internal` followed by
`test-internal-no-build`. Keep that build-before-no-build sequence intact; it
was added for issue #753 / PR #888 after the quality gate was SIGTERMed during a
single build-and-test `test-internal` run.

## Narrow Test Runs
```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

## Recurring Maintenance Child Runs

When a spawned maintenance-child issue asks for one bounded repository
maintenance pass, keep the slice repo-local and reviewable:

1. Choose exactly one docs, tooling, test-fixture, or workflow simplification
   slice. Do not add or change recurrence infrastructure; Faktorial owns the
   recurrence schedule.
2. Capture a cheap baseline signal before editing. Prefer evidence such as
   `make -n quality`, a targeted `rg` check, `git diff --check`, or another
   narrow command tied directly to the chosen slice.
3. Make only the small change required for that slice.
4. Capture the matching final signal after editing and record both signals in
   the issue update.

For docs-only maintenance, do not run full builds, Test262, package installs,
or broad audits unless the edit directly depends on them. The canonical local
quality gate remains `make quality`, which builds and tests the internal suite
only.

## ECMAScript Test262 Suite

Run the full LanguageTests class (43,000+ tests):
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings
```

Run the BuiltInsTests class:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings
```

### Targeting Specific Test262 Tests

List available tests to find exact names:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --list-tests 2>&1 | grep -i "SomePattern"
```

**Filter syntax:**
- `~` = contains
- `&` = AND
- `|` = OR
- Parentheses for grouping

Run a single parameterized test by method + path:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&FullyQualifiedName~block-decl-onlystrict"
```

Run multiple specific tests with OR:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&(FullyQualifiedName~block-decl-onlystrict|FullyQualifiedName~switch-case-decl-onlystrict|FullyQualifiedName~switch-dflt-decl-onlystrict)"
```

Run all tests for a specific method:
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "Name=FunctionCode"
```

Run tests matching a pattern (e.g., all strict-mode-only tests):
```bash
dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --filter "FullyQualifiedName~FunctionCode&FullyQualifiedName~onlystrict"
```

## Demos
```bash
dotnet run --project examples/Demo
dotnet run --project examples/PromiseDemo
dotnet run --project examples/NpmPackageDemo
```

## Test262 regression session
- Source list: `tests/Asynkron.JsEngine.Tests.Test262/current-regressions.filter.txt`
- Runner: `./tools/run-test262-regressions.sh`
- This pack is intentionally Test262-only and excludes non-Test262 regressions such as `Asynkron.JsEngine.Tests.Array_indexOf_OnlyCallsHasPropertyOnPrototypeAfterLengthZeroed`.
- Smaller subsystem packs live under `tests/Asynkron.JsEngine.Tests.Test262/regression-packs/`.
- Examples: `./tools/run-test262-regressions.sh full`, `./tools/run-test262-regressions.sh temporal`, `./tools/run-test262-regressions.sh regexp`, `./tools/run-test262-regressions.sh proxy`.
