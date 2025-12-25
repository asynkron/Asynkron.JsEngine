# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See @AGENTS.md for additional coding standards, profiling guidelines, and optimization patterns.

## Build and Test Commands

```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run unit tests only
dotnet test tests/Asynkron.JsEngine.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests by category
dotnet test --filter Category=AsyncForOfGlobalKnownFailure

# Run demos
dotnet run --project examples/Demo
dotnet run --project examples/PromiseDemo
dotnet run --project examples/NpmPackageDemo

# Profiling
./tools/profile forofiteration --cpu
./tools/profile forofiteration --memory
./tools/profile forofiteration --exception
Where "forofiteration" is the name of one of the /tools/Scripts examples

```

**Important**: Never use `--no-build` - always ensure you are working with the latest compiled code.

## Architecture Overview

This is a JavaScript interpreter written in C# targeting .NET 10. The execution pipeline is:

**JavaScript Source → Lexer → TypedAstParser → Typed AST → TypedAstEvaluator → Result**

### Core Components (in `src/Asynkron.JsEngine/`)

- **Parser/** - `Lexer` tokenizes source, `TypedAstParser` produces typed AST nodes (`ProgramNode`, `StatementNode`, `ExpressionNode`)
- **Ast/** - AST node definitions and the `TypedAstEvaluator` which executes the AST. Many `*Extensions.cs` files contain evaluation logic for specific node types
- **JsTypes/** - JavaScript value types: `JsObject`, `JsArray`, `JsFunction`, `JsPromise`, `JsBigInt`, typed arrays, etc.
- **JsEnvironment.cs** - Lexical environment/scope chain management
- **JsEngine.cs** - Public API façade, registers globals (Object, Array, Promise, Symbol, Map, Set, etc.), integrates event queue
- **Execution/** - Generator IR interpreter for `yield`/`yield*`, async iteration support
- **StdLib/** - Standard library implementations (Math, Date, JSON, RegExp, console, etc.)

### Key Design Patterns

- **Generator IR**: Synchronous generators compile to `GeneratorPlan` and execute via IR interpreter (not AST replay)
- **CPS Transformation**: Async/await lowered to Promise/continuation-passing style before evaluation
- **Prototype Chains**: `JsObject` tracks prototype chain for property lookup traversal

## Development Rules

### Thread Safety
- **Never** use `Task.Wait()`, `Task.Result`, or `Thread.Sleep()` - these block threads
- **Never** use `ThreadStatic`, `AsyncLocal<T>`, or shared state between async calls
- Pass all context explicitly via `JsEnvironment` or similar parameters

### ECMAScript Compliance
- Follow ECMAScript specification behavior as closely as practical
- Do not introduce non-standard language extensions
- Support both strict and sloppy mode with spec-defined differences

### Error Handling
- Throw `NotSupportedException` with clear reason for unsupported features - never silently degrade
- Use `realm.Logger?.LogInformation(...)` for diagnostics, never `Console.WriteLine`

### Code Generation
- Never edit files with `.generated.` in their names - they are produced by tooling
- Edit non-generated partials/helpers instead

### Debugging

**IMPORTANT**: Do NOT resort to `Console.WriteLine`, writing special debug programs, or ad-hoc logging. Use the established debugging infrastructure described below.

#### Realm Logger Assertions

Use `FakeLogger` from `Microsoft.Extensions.Logging.Testing` to capture and assert on log output:

```csharp
var fakeLogger = new FakeLogger();
await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = fakeLogger });

await engine.Evaluate("your test script");

var messages = fakeLogger.Collector.GetSnapshot().Select(r => r.Message).ToList();

// Positive assertion - this log fragment SHOULD exist
Assert.Contains(messages, m => m.Contains("expected log fragment", StringComparison.Ordinal));

// Negative assertion - this log fragment should NOT exist
Assert.DoesNotContain(messages, m => m.Contains("unwanted behavior", StringComparison.Ordinal));
```

#### JavaScript `__debug()` Method

Use the built-in `__debug()` function in JavaScript to capture execution state. Requires `DebugMode = true`:

```csharp
await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

await engine.Evaluate(@"
    var x = 42;
    __debug();  // Captures all variables and call stack at this point
    x = x + 1;
    __debug();  // Captures updated state
");

// Read debug messages synchronously after evaluation
var messages = new List<DebugMessage>();
while (engine.DebugMessages().TryRead(out var msg))
{
    messages.Add(msg);
}

// Assert on captured state
Assert.Equal(2, messages.Count);
Assert.Equal(42d, messages[0].Variables["x"]);  // First checkpoint
Assert.Equal(43d, messages[1].Variables["x"]);  // Second checkpoint

// For async code, use ReadAsync to wait for messages
var msg = await engine.DebugMessages().ReadAsync();
```

`DebugMessage` contains:
- `Variables` - All variables in scope at the `__debug()` call
- `CallStack` - The current call stack
- `EnvironmentChain` - The scope chain

#### Layered Test Approach

When debugging issues, work **top-down through layers**:

1. **Start at the top** - Write a high-level test that reproduces the issue
2. **Narrow down** - Add more specific tests to isolate which component fails
3. **Drill into the layer** - Once identified, add unit tests for that specific layer
4. **Never skip layers** - Don't jump to conclusions; systematically verify each layer

```csharp
// Layer 1: Full evaluation test
[Fact]
public async Task Issue_FullScript_Fails() { /* high-level repro */ }

// Layer 2: Narrowed to specific construct
[Fact]
public async Task Issue_NestedForOf_Fails() { /* isolated construct */ }

// Layer 3: Specific component
[Fact]
public async Task Issue_IteratorEnvironment_WrongScope() { /* specific mechanism */ }
```

#### Permutation Testing

When a bug appears in complex scenarios, test **all relevant permutations** to pinpoint the exact combination:

```csharp
// Example: Bug in nested loops - test all loop type combinations
[Theory]
[InlineData("for", "for")]
[InlineData("for", "while")]
[InlineData("for", "for-of")]
[InlineData("while", "for")]
[InlineData("while", "while")]
[InlineData("while", "for-of")]
[InlineData("for-of", "for")]
[InlineData("for-of", "while")]
[InlineData("for-of", "for-of")]
public async Task NestedLoop_Combinations(string outer, string inner)
{
    var script = GenerateNestedLoopScript(outer, inner);
    // Test each permutation
}
```

This approach reveals:
- Which specific combination triggers the bug
- Whether the bug is in outer loop, inner loop, or their interaction
- Edge cases that might be missed with single tests

#### Profiler Usage

Use the profiler for performance-related bugs or to understand execution flow:

```bash
# CPU profiling - find hot paths and unexpected call patterns
./tools/profile <script> --cpu

# Memory profiling - find allocation hotspots
./tools/profile <script> --memory

# Exception profiling - find hidden exceptions
./tools/profile <script> --exception
```

#### Activity Tracing

Use `System.Diagnostics.Activity` for detailed execution tracing:
- See `ActivityTracingTests.EvaluatorActivitiesAttachToTestRoot` for examples

## Project Structure

```
src/
  Asynkron.JsEngine/           # Main engine library
  Asynkron.JsEngine.Generators/ # Source generators
tests/
  Asynkron.JsEngine.Tests/     # Unit tests (xUnit)
  Asynkron.JsEngine.Tests.Test262/ # ECMAScript Test262 conformance tests
examples/                      # Demo console applications
docs/                          # Detailed documentation
```

## Workflow

The `continue.md` file at repo root contains rolling next steps. When completing a task, remove it from `continue.md` and update with new steps.

## Git Worktree Workflow for Refactoring and Bugfixing

All refactoring or bugfixing work MUST be performed using git worktrees for isolation. Follow this workflow:

1. **Create git worktree for the feature/bug**
   ```bash
   git worktree add ../Asynkron.JsEngine-<short-name> -b feature/<feature-name>
   # or for bugs:
   git worktree add ../Asynkron.JsEngine-<short-name> -b fix/<bug-name>
   ```

2. **Make a plan** - Analyze the issue, identify affected files, and plan the implementation

3. **Implement the fix/refactoring** - Make the necessary code changes

4. **Run filtered tests** - Verify the specific fix works
   ```bash
   dotnet test --filter "FullyQualifiedName~RelevantTestName"
   ```

5. **Run full internal test suite**
   ```bash
   dotnet test tests/Asynkron.JsEngine.Tests
   ```

6. **Run CPU and memory profiler** - If a relevant profiling script exists for the feature
   ```bash
   ./tools/profile <script-name> --cpu
   ./tools/profile <script-name> --memory
   ```

7. **Any problems?** - If tests fail or performance regresses, return to step 2 and iterate

8. **Commit and push**
   ```bash
   git add -A && git commit -m "Description of changes"
   git push -u origin <branch-name>
   ```

9. **Create GitHub PR using gh CLI**
   ```bash
   gh pr create --title "PR Title" --body "Description"
   ```

10. **Merge the PR using gh CLI**
    ```bash
    gh pr merge <pr-number> --squash
    ```

11. **Sync main and delete the worktree**
    ```bash
    # In main repo:
    git fetch origin && git reset --hard origin/main
    git worktree remove ../Asynkron.JsEngine-<short-name>
    git branch -D <branch-name>
    ```
