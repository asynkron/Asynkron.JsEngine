# Agent Guidelines for Asynkron.JsEngine

Use these task-specific guides while working in the repo. MUST READ AND UNDERSTAND ALL OF THESE before working.

## Standards & Architecture
- [Coding standards and InvariantCulture rules](agents/how-to-coding-standards.md)
- [Architecture overview](agents/how-to-architecture.md)

## Build, Tests, and Profiling
- Use `/test` to run the internal test suite (not ECMAScript 262 tests)
- [Build/test commands and demos](agents/how-to-build-and-test.md)
- [Profiling (scripts, manual traces, hotspots)](agents/how-to-profiling.md)

## Engineering Rules & Workflow
- [Development rules (thread safety, compliance, timeouts)](agents/how-to-development-rules.md)
- [Workflow and GitHub issue logging](agents/how-to-workflow-and-issues.md) — GitHub issues are the persistent working memory; use Faktorial Source Context/API when supplied, otherwise use the `gh` CLI patterns there to view/create/comment/patch and log progress.
- [Git worktree workflow](agents/how-to-worktrees.md)
- **Pre-PR checklist**: Use `/pre-pr` skill before any PR — runs roslynator fix, tests, quickdup, format

## JsValue and Performance Patterns
- [JsValue usage and evaluator overload pattern](agents/how-to-jsvalue-usage.md)
- [Comparing to Jint (do/don't language)](agents/how-to-compare-jint.md)

## Debugging & Test Strategies
- [Debugging aids (logger assertions, slot metadata)](agents/how-to-debugging.md)
- [Test Bomb methodology](agents/how-to-test-bombs.md)
- [Layered Tests methodology](agents/how-to-layered-tests.md)

## Test262 Conformance Testing

### Testrunner
- The custom testrunner lives at `~/git/asynkron/Asynkron.TestRunner` (global tool: `testrunner`)
- Run: `testrunner run tests/Asynkron.JsEngine.Tests.Test262/Asynkron.JsEngine.Tests.Test262.csproj --workers 4`
- Baselines: `.testrunner/baseline.md` — copy `summary.md` after each run to track progress

### Crash collateral vs real failures
- The testrunner reports ~1000+ "failures" but most are **crash collateral** — innocent tests killed when an OOM test crashes the worker process
- **Always verify failures individually**: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --no-build --filter "Name=<TestMethod>" --blame-hang-timeout 15s`
- Real correctness failures are typically <20. Run Language and BuiltIns runsettings to get true counts:
  - `dotnet test ... --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings`
  - `dotnet test ... --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings`

### Checking for regressions
- Before assuming a new failure is a regression, `git stash` and test on clean main
- Compare testrunner baselines with `diff` on sorted failure lists — most churn is crash collateral noise
- Excluded features (dynamic import, import-defer, etc.) are in `Test262Harness.settings.json`

### Performance-related test failures
- RegExp property escapes, DecodeURI/EncodeURI tests are CPU-heavy — they pass individually but timeout in parallel
- Use `/profile` skill to profile before optimizing: `dnx asynkron-profiler --cpu -- <target>`
- `RegexOptions.Compiled` is enabled for all JS regex patterns — large patterns benefit from JIT compilation
- Property escape pattern expansions are cached via `ConcurrentDictionary`
