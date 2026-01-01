# Agent Guidelines for Asynkron.JsEngine

## Coding Standards

### Antipatterns

- **Avoid using `object`**: Always use `JsValue` when dealing with JavaScript values. Using `object` leads to boxing and performance issues.
- **Avoid `IDictionary<Symbol, T>`**: Using dictionaries for symbol lookups is slow due to hashing. Prefer slot-based access for identifiers.
- **Minimize allocations in hot paths**: Avoid creating new objects, arrays, or strings in frequently executed code.
- **Avoid deep recursion**: Refactor recursive algorithms to iterative ones where possible to prevent stack overflows and improve performance.
- **Avoid unnecessary environment activations**: Reuse `JsEnvironment` instances when possible to reduce overhead.
- **Avoid default culture conversions**: Always specify `InvariantCulture` for number/string conversions to ensure consistent behavior.
- **Avoid complex LINQ queries in hot paths**: LINQ can introduce overhead; use simple loops instead.

### Invariant Culture for Number/String Conversions

**CRITICAL RULE**: All floating-point and double-precision number to/from string conversions **MUST** use `InvariantCulture`.

This ensures consistent behavior across different locales and prevents issues with decimal separators, thousands separators, and number formatting.

#### Examples

**✅ CORRECT:**
```csharp
// Number to string
double value = 3.14;
string str = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

// Integer to string (when culture matters)
long intValue = 1000;
string intStr = intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

// Formatted numbers
double num = 42.123;
string formatted = num.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); // "42.12"
string exponential = num.ToString("e", System.Globalization.CultureInfo.InvariantCulture); // "4.212300e+001"
```

**❌ INCORRECT:**
```csharp
// DO NOT use default culture
double value = 3.14;
string str = value.ToString(); // BAD: Uses current culture

long intValue = 1000;
string intStr = intValue.ToString(); // BAD: Uses current culture for formatting
```

#### Where This Applies

- All Number.prototype methods (toString, toFixed, toExponential, toPrecision)
- String constructor conversions
- Any Math operations that produce string output
- JSON serialization of numbers
- Console output of numeric values
- Date/time formatting when dealing with numeric components

#### Why This Matters

Different cultures format numbers differently:
- US: `3.14` (period as decimal separator)
- Germany: `3,14` (comma as decimal separator)
- France: `3,14` with thousands separator

JavaScript expects consistent number formatting (US/Invariant style with periods), so we must always use InvariantCulture to match JavaScript behavior.

## Build and Test (Codex Web)

These are the standard commands to build and run tests in this repo:

```bash
# Restore dependencies
dotnet restore

# Build everything
dotnet build

# Run the main test suite
dotnet test tests/Asynkron.JsEngine.Tests
```

Optional: to run a narrower test subset (replace the filter as needed):

```bash
dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~SomeTestName"
```

## Profiling

### Profiler Script (Recommended)

The easiest way to profile JsEngine is using the `asynkron-profiler` CLI via `./tools/profile`:

```bash
# Profile fibonacci (CPU + memory)
./tools/profile fib

# Profile forloop (CPU + memory)
./tools/profile forloop

# Profile all benchmarks
./tools/profile all

# CPU profiling only
./tools/profile fib --cpu

# Memory profiling only
./tools/profile fib --memory

# Exception profiling only
./tools/profile fib --exception

# Heap snapshot only
./tools/profile fib --heap

# Run Jint comparison benchmarks
./tools/profile --compare
```

The script automatically:
1. Builds the ProfileRunner app
2. Runs `asynkron-profiler` for CPU/memory/heap capture
3. Converts traces to speedscope format when needed
4. Parses the JSON and outputs hot functions
5. Shows allocation call graphs (who triggered each allocation)

#### Output Example

```
=== JSENGINE HOT FUNCTIONS ===
   Time (ms)      Calls  Function
--------------------------------------------------------------------------------------------------------------
    38805.39      19533  Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateExpression...
    19769.23       9897  Asynkron.JsEngine.Ast.TypedAstEvaluator+SyncFunctionInvoker.Invoke...
    19753.25       9961  Asynkron.JsEngine.Ast.TypedAstEvaluator.EvaluateCall...

JsEngine time: 166928.10 ms (91.8% of total)
```

#### Allocation Call Graph

The profiler also outputs allocation hotspots with call graphs showing the code paths that triggered allocations:

```
=== ALLOCATION HOTSPOTS (constructors & allocators) ===

CreateNextIterationEnvironment
  Calls: 1048
  Allocated by:
    <- EvaluateLoopPlanJsValue (1048x, 100%)
         <- EvaluateForJsValue (4x)

JsArgumentsObject..ctor
  Calls: 208
  Allocated by:
    <- CreateArgumentsObject (208x, 100%)
         <- SyncFunctionInvoker.Invoke (362x)
```

This helps identify which code paths cause the most memory allocations.

### Manual Profiling

#### Quick Start with BenchmarkDotNet

```bash
cd benchmarks/Asynkron.JsEngine.Benchmarks
dotnet run -c Release -- --filter "*Fibonacci*"
```

#### Capture Detailed Allocation Trace

```bash
# Trace what's being allocated (with call stacks)
dotnet-trace collect \
  --profile gc-verbose \
  --format NetTrace \
  -o trace.nettrace \
  -- dotnet run -c Release \
     --project benchmarks/Asynkron.JsEngine.Benchmarks \
     --filter "JintComparisonBenchmarks.Asynkron_ForLoop"

# Analyze the trace
dotnet-trace report trace.nettrace topN -n 30

# Or convert to Speedscope/Chromium format for visualization
dotnet-trace convert trace.nettrace --format Speedscope
```

> **Detailed Guide**: See [docs/memory-profiling.md](docs/memory-profiling.md) for comprehensive profiling techniques including dotnet-trace, GC dumps, and trace analysis.

### Known Allocation Hotspots

**Fibonacci Benchmark Results (as of Dec 2024):**

*Before optimizations:*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 56ms | 50.11 MB | 8,000 |
| Asynkron | 172ms | 322.37 MB | 53,000 |

*After optimizations (Round 1):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | ~55ms | 50.11 MB | 8,000 |
| Asynkron | ~150ms | **173.25 MB** | **28,000** |

*After optimizations (Round 2 - lazy init & lock-free pools):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 52.58 ms | 50.11 MB | 8,000 |
| Asynkron | 134.51 ms | 168.62 MB | 28,000 |

*After optimizations (Round 3 - NumericResult struct & fast paths):*
| Engine | Time | Allocated | Gen0 Collections |
|--------|------|-----------|------------------|
| Jint | 53.30 ms | 50.11 MB | 8,000 |
| Asynkron | **116.84 ms** | **107.49 MB** | **17,000** |

**Cumulative Improvement:**
- Allocations: 322 MB → 107.49 MB = **~67% reduction**
- Speed: ~172 ms → 116.84 ms = **~32% faster**
- Gap with Jint: 2.2x time, 2.1x allocations (down from 3.1x and 6.4x)

## Git Worktree Workflow

When making code changes, use git worktrees to isolate work from the main repository. This prevents conflicts with uncommitted changes and allows parallel development.

### Creating a Worktree

```bash
# Create a new worktree with a feature branch
git worktree add ../Asynkron.JsEngine-<feature> -b feature/<branch-name>

# Example:
git worktree add ../Asynkron.JsEngine-typing -b feature/type-narrowing
```

This creates a separate directory with its own working tree, sharing the same git history.

### Working in the Worktree

1. Make changes in the worktree directory
2. Build and test: `dotnet build && dotnet test tests/Asynkron.JsEngine.Tests`
3. Commit changes
4. Push and create PR: `git push -u origin feature/<branch-name> && gh pr create`
5. Merge: `gh pr merge <pr-number> --squash`

### Cleanup After Merge

```bash
# From the main repo directory
git pull origin main
git worktree remove ../Asynkron.JsEngine-<feature> --force
git branch -D feature/<branch-name>
```

### Why Use Worktrees

- **Isolation**: Changes don't affect the main working directory
- **Parallel work**: Can have multiple features in progress simultaneously
- **Clean merges**: Each worktree has its own index and working tree
- **Easy cleanup**: Remove worktree after PR is merged

### Naming Convention

Use descriptive suffixes for worktree directories:
- `Asynkron.JsEngine-typing` - Type narrowing work
- `Asynkron.JsEngine-perf` - Performance optimizations
- `Asynkron.JsEngine-fix-123` - Bug fix for issue #123

## Other Guidelines

- Rider MCP is available for refactoring/renaming and other IDE-aware operations; prefer it when a change benefits from symbol-aware edits.

## System.Object to JsValue

Many bugs are a result of untyped `object` values being passed around instead of `JsValue`.
Always ensure proper conversion when interfacing with JavaScript values.

If a method receives `object`, do not add guards casting or checking for JsObject, update the method to accept `JsValue` directly.

## JsValue Overload Pattern for Evaluators

When optimizing evaluator methods to avoid boxing, follow this pattern:

### Problem
Methods like `EvaluateBlock`, `EvaluateStatement`, `EvaluateIf` return `object?` which causes boxing when the result is a primitive (double, bool, etc.). In hot loops, this creates massive memory allocations.

### Solution
Add `JsValue`-returning overloads to evaluator methods:

1. **Keep the original method** for compatibility:
```csharp
private object? EvaluateBlock(JsEnvironment environment, EvaluationContext context)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(block, environment, context);
    return hasJsResult ? jsResult.ToObject() : objResult;
}
```

2. **Add a JsValue overload** for hot paths:
```csharp
private JsValue EvaluateBlockJsValue(JsEnvironment environment, EvaluationContext context)
{
    var (jsResult, hasJsResult, objResult) = EvaluateBlockCore(block, environment, context);
    return hasJsResult ? jsResult : JsValue.FromObject(objResult);
}
```

3. **Extract core logic** that returns both forms without boxing:
```csharp
private (JsValue jsResult, bool hasJsResult, object? objResult) EvaluateBlockCore(
    JsEnvironment environment, EvaluationContext context)
{
    // Implementation that tracks JsValue separately from object results
}
```

### Where to Apply

Apply this pattern to evaluators called in hot loops:

| Method | File | Priority |
|--------|------|----------|
| `EvaluateStatement` | StatementNodeExtensions.cs | High |
| `EvaluateBlock` | BlockStatementExtensions.cs | High |
| `EvaluateIf` | IfStatementExtensions.cs | High |
| `EvaluateExpression` | Already returns JsValue | Done |

### Usage in Loops

In `LoopPlanExtensions.cs`, use the JsValue versions:

```csharp
// Track loop result as JsValue to avoid boxing on each iteration
var lastValueJs = JsValue.Undefined;

while (true)
{
    // Use JsValue version - no boxing per iteration
    lastValueJs = EvaluateStatementJsValue(plan.Body, iterationEnvironment, context, loopLabel);
    // ...
}

// Only box at the final return
return lastValueJs.ToObject();
```

### Fast Path in EvaluateStatementJsValue

Handle the common cases without boxing:

```csharp
private JsValue EvaluateStatementJsValue(JsEnvironment environment, EvaluationContext context, Symbol? activeLabel = null)
{
    // Fast path for hot loop cases - avoid boxing
    switch (statement)
    {
        case BlockStatement block:
            return EvaluateBlockJsValue(block, environment, context);
        case ExpressionStatement expr:
            return EvaluateExpression(expr.Expression, environment, context);
        case IfStatement ifStmt:
            return EvaluateIfJsValue(ifStmt, environment, context);
    }

    // Slow path for other statements - box the result
    var result = EvaluateStatement(statement, environment, context, activeLabel);
    return JsValue.FromObject(result);
}
```

### Results

This optimization reduced memory allocation in the ForLoop benchmark:
- `let` loops (50k iterations): 4.99 MB → 3.84 MB (23% reduction)
- `var` loops (100k iterations): 29.52 MB → 27.24 MB (8% reduction)
- Execution time also improved ~19% for `let` loops

## Comparing to Jint

When discussing performance comparisons with Jint:

### Do NOT Say
- "The performance gap with Jint is likely due to deeper architectural differences"
- "Bytecode compilation (like Jint's interpreter)" - **Jint is NOT a bytecode interpreter**, it's an AST-walking interpreter like we are

### Do Say
- "We need to investigate what specific optimizations we can apply"
- "Let's profile to find the actual bottlenecks"
- "What are the remaining allocation hotspots?"

### Key Facts About Jint
- Jint is an **AST-walking interpreter**, not a bytecode interpreter
- Both Jint and Asynkron.JsEngine evaluate JavaScript by walking the AST
- Jint uses `readonly struct ExecutionContext` while we use class `EvaluationContext` (required for async/await support)
- When comparing, always profile and identify specific differences rather than hand-waving about "architecture"

### Investigation Approach
When we're slower than Jint, the proper approach is:
1. Profile both engines on the same workload
2. Compare hot function call counts and time distribution
3. Identify specific allocations/operations that differ
4. Create targeted optimizations for each bottleneck
5. Repeat until parity is achieved or specific trade-offs are understood

## Debugging

- **Realm logger assertions**: In tests, inject `new FakeLogger()` (from `Microsoft.Extensions.Logging.Testing`) via `new JsEngine(new JsEngineOptions { DebugMode = true, Logger = fakeLogger })`. Execute the script, then assert on `fakeLogger.Collector.Snapshot()` strings. Example: ensure no slot misses with `Assert.DoesNotContain(messages, m => m.Contains("Identifier slot read miss name=s", StringComparison.Ordinal))` and confirm hits for `i`/`s` to prove the fast path is exercised.
- **AST slot metadata**: Scope analysis now stamps `FunctionExpression` and `BlockStatement` with `ScopeId`, `SlotCount`, and `SlotMap` (symbol → slot index). You can parse and inspect the analyzed AST (e.g., `var parsed = engine.ParseProgram(script); var runDecl = (FunctionDeclaration)parsed.Body[0]; var slotMap = runDecl.Function.SlotMap; Assert.True(slotMap.ContainsKey(Symbol.Create("i")));`) to verify that specific identifiers received slots in the expected scope before running code.

## Test Bomb Methodology

When debugging a complex issue where the root cause is unclear, use the "Test Bomb" approach to systematically eliminate hypotheses. This is a FAANG-style debugging technique.

### What is a Test Bomb?

A Test Bomb is a collection of targeted tests, each testing **ONE specific hypothesis** about what might be wrong. By running all tests together, you can quickly identify which component is broken by observing the pattern of pass/fail results.

### When to Use Test Bombs

- Root cause is unclear despite initial investigation
- Multiple potential failure points
- Bug could be in one of several components
- Need to prove the bug ISN'T in a specific area
- Suspecting the test itself might be wrong

### How to Build a Test Bomb

1. **List all suspected causes** - Brainstorm every possible reason the bug could occur
2. **Write ONE test per hypothesis** - Each test should:
   - Test exactly ONE thing in isolation
   - Have a clear, predictable expected outcome
   - Be named `H1_DescriptiveHypothesis`, `H2_AnotherHypothesis`, etc.
   - Include a doc comment explaining the hypothesis
3. **Run all tests together** - The pattern of pass/fail reveals the problem:
   - All pass → Your hypothesis list is incomplete, or the test itself is wrong
   - One fails → That's likely your bug
   - Multiple fail → Related issues or root cause affects multiple areas
4. **Add edge case tests** - As you learn more, add `H13`, `H14`, etc.

### Example: Strict Mode Eval Investigation

We suspected `eval()` wasn't inheriting strict mode. We wrote 14 tests:

```
H1_UseStrictWorksAtAll             ✅ PASS
H2_UseStrictWorksInFunction        ✅ PASS
H3_UseStrictInsideTryBlock         ✅ PASS
H4_EvalWorksAtAll                  ✅ PASS
H5_DirectEvalInheritsStrictMode    ✅ PASS
H6_UseStrictInsideEvalWorks        ✅ PASS
H7_ArgumentsAssignmentIsSyntaxError ✅ PASS
H8_EvalAssignmentIsSyntaxError     ✅ PASS
H9_CallerStrictEvalInheritsIt      ✅ PASS
H10_OtherReservedWordAssignment    ✅ PASS
H11_IndirectEvalDoesNotInheritStrict ✅ PASS
H12_EvalFromStrictFunction         ✅ PASS
H13_UseStrictInsideTryIsNotDirective ✅ PASS
H14_UseStrictAtTopWithTry          ✅ PASS
```

All passed! This revealed the **original test was wrong** - it had `'use strict'` inside a try block, which per ECMAScript spec is just a string literal, not a directive.

### Test Bomb Template

```csharp
/// <summary>
/// TEST BOMB: Systematic elimination of suspected causes for [BUG DESCRIPTION].
/// Each test targets ONE specific hypothesis.
/// </summary>
public class MyBugTestBomb
{
    private readonly ITestOutputHelper _output;

    public MyBugTestBomb(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// H1: [First hypothesis description]
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H1_FirstHypothesis()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("/* test code */");
        _output.WriteLine($"H1 Result: {result}");
        Assert.Equal("expected", result?.ToString());
    }

    /// <summary>
    /// H2: [Second hypothesis description]
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H2_SecondHypothesis()
    {
        // ...
    }
}
```

### Benefits

- **Systematic** - No guessing, methodical elimination of possibilities
- **Documented** - Each test explains what it's checking and why
- **Reusable** - Tests become regression tests for the future
- **Fast** - Run all hypotheses in parallel
- **Educational** - Reveals how the system actually works
- **Proof** - Can prove a bug does NOT exist in a component

## Layered Tests Methodology

When debugging complex issues, use "Layered Tests" to verify each component in the execution pipeline works correctly in isolation. This technique tests individual layers (parser, analyzer, evaluator) separately before testing the full end-to-end behavior.

### What are Layered Tests?

Layered Tests progressively verify each stage of the execution pipeline:

```
JavaScript Source → Lexer → Parser → AST → Analyzers → Evaluator → Result
     Layer 0        Layer 1   Layer 2   Layer 3   Layer 4      Layer 5
```

Each test targets ONE layer, verifying its output before the next layer runs. This isolates exactly where a bug occurs in the pipeline.

### When to Use Layered Tests

- Bug manifests at runtime but root cause is unclear
- Need to verify AST structure before evaluation
- Testing analyzer transformations (scope analysis, CPS, loop normalization)
- Verifying metadata (SlotMap, ScopeId, PerIterationBindings) is generated correctly
- Debugging closure capture, environment chains, or slot-based lookups
- Want to ensure an optimization doesn't break intermediate state

### How to Build Layered Tests

1. **Start at Layer 0** - Verify the source code is valid
2. **Test parser output** - Parse without evaluating, inspect AST nodes
3. **Test analyzer output** - Verify transformations and metadata
4. **Test with logging** - Enable Realm logger, assert on internal operations
5. **Test full evaluation** - Finally run end-to-end and verify result

### The Five Layer Pattern

| Layer | What to Test | How to Test |
|-------|-------------|-------------|
| **L1: Parser** | AST structure is correct | `AstTestHelpers.ParseAndAnalyze()`, inspect node types |
| **L2: Analyzers** | Metadata generated correctly | Check `.SlotMap`, `.ScopeId`, `.PerIterationBindings` |
| **L3: Plans** | Loop/function plans built correctly | `((IAstCacheable<LoopPlan>)node).GetOrCreateCache()` |
| **L4: Runtime** | Internal operations work | `TestLogger` + assert on log messages |
| **L5: Result** | Final output is correct | `engine.Evaluate()` + assert on value |

### Example: For Loop Closure Capture Bug

We suspected closures in `for (let i...)` loops weren't capturing per-iteration values:

```csharp
/// <summary>
/// LAYERED TESTS: For loop closure capture verification.
/// Tests each pipeline stage to isolate where capture fails.
/// </summary>
public class ForLoopClosureLayeredTests : InternalTestBase
{
    // ==================== LAYER 1: Parser ====================
    /// <summary>
    /// L1: Verify ForStatement parses correctly with let initializer.
    /// </summary>
    [Fact]
    public void L1_ForLoopWithLet_ParsesCorrectly()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            for (let i = 0; i < 3; i++) { funcs.push(() => i); }
            """);

        var forStmt = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);
        Assert.NotNull(forStmt);
        Assert.IsType<VariableDeclaration>(forStmt.Init);

        var decl = (VariableDeclaration)forStmt.Init;
        Assert.Equal(VariableDeclarationKind.Let, decl.Kind);
    }

    // ==================== LAYER 2: Scope Analysis ====================
    /// <summary>
    /// L2: Verify scope analyzer marks 'let' variables for per-iteration binding.
    /// </summary>
    [Fact]
    public void L2_LetVariable_MarkedForPerIterationBinding()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            for (let i = 0; i < 3; i++) { funcs.push(() => i); }
            """);

        var forStmt = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);

        // Check the ScopeId was assigned
        Assert.True(forStmt.ScopeId > 0, "ForStatement should have a ScopeId");
    }

    // ==================== LAYER 3: Loop Plan ====================
    /// <summary>
    /// L3: Verify LoopPlan includes 'i' in PerIterationBindings.
    /// </summary>
    [Fact]
    public void L3_LoopPlan_ContainsPerIterationBinding()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("""
            for (let i = 0; i < 3; i++) { funcs.push(() => i); }
            """);

        var forStmt = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);
        var plan = ((IAstCacheable<LoopPlan>)forStmt).GetOrCreateCache();

        // THE KEY ASSERTION: 'i' must be in per-iteration bindings
        var bindingNames = plan.PerIterationBindings.Select(b => b.Name).ToArray();
        Assert.Contains("i", bindingNames);
    }

    // ==================== LAYER 4: Runtime Logging ====================
    /// <summary>
    /// L4: Verify environment creation per iteration via logs.
    /// </summary>
    [Fact]
    public async Task L4_PerIterationEnvironments_Created()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            const funcs = [];
            for (let i = 0; i < 3; i++) { funcs.push(() => i); }
            """);

        // Check that per-iteration environments were created
        var activateCount = logger.Collector.Snapshot()
            .Count(r => r.Message.Contains("JsEnvironment.Activate"));

        Output.WriteLine($"Environment activations: {activateCount}");
        Assert.True(activateCount >= 3, "Should activate at least 3 environments (one per iteration)");
    }

    // ==================== LAYER 5: Full Result ====================
    /// <summary>
    /// L5: Verify closures capture correct per-iteration values.
    /// </summary>
    [Fact]
    public async Task L5_Closures_CaptureCorrectValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const funcs = [];
            for (let i = 0; i < 3; i++) { funcs.push(() => i); }
            funcs.map(f => f());
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0.0, array.GetElement(0).AsDouble()); // First closure sees i=0
        Assert.Equal(1.0, array.GetElement(1).AsDouble()); // Second closure sees i=1
        Assert.Equal(2.0, array.GetElement(2).AsDouble()); // Third closure sees i=2
    }
}
```

**Result**: L3 failed! The `LoopPlan.PerIterationBindings` was empty, revealing the bug was in `LoopNormalizer` not detecting the closure.

### Helper Infrastructure

**AstTestHelpers.cs** - Parse without evaluation:
```csharp
public static class AstTestHelpers
{
    /// <summary>
    /// Runs lexer → parser → analyzers, returns AST without evaluation.
    /// </summary>
    public static AstPipelineResult ParseAndAnalyze(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new TypedAstParser(tokens, source);
        var parsed = parser.ParseProgram();
        var analyzed = new TypedConstantExpressionTransformer().Transform(parsed);
        return new AstPipelineResult(parsed, analyzed, analyzed);
    }

    /// <summary>
    /// Find first node of type T in AST.
    /// </summary>
    public static T? FindFirst<T>(AstNode root) where T : AstNode
    {
        return Walk(root, includeSelf: true).OfType<T>().FirstOrDefault();
    }
}
```

**TestLogger.cs** - Capture internal operations:
```csharp
public sealed class TestLogger : ILogger
{
    public LogCollector Collector { get; } = new();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Collector.Add(new LogRecord(logLevel, eventId, exception, formatter(state, exception)));
    }

    public sealed class LogCollector
    {
        private readonly ConcurrentQueue<LogRecord> _records = new();
        public void Add(LogRecord record) => _records.Enqueue(record);
        public LogRecord[] Snapshot() => _records.ToArray();
    }
}
```

### Layered Test Template

```csharp
/// <summary>
/// LAYERED TESTS: [Description of what's being tested].
/// Tests each pipeline stage to isolate where [problem] occurs.
/// </summary>
public class MyFeatureLayeredTests : InternalTestBase
{
    public MyFeatureLayeredTests(ITestOutputHelper output) : base(output) { }

    // ==================== LAYER 1: Parser ====================
    [Fact]
    public void L1_Parser_ProducesCorrectAst()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("/* source */");
        var node = AstTestHelpers.FindFirst<ExpectedNodeType>(pipeline.Analyzed);
        Assert.NotNull(node);
        // Assert on AST structure
    }

    // ==================== LAYER 2: Analyzers ====================
    [Fact]
    public void L2_Analyzer_GeneratesCorrectMetadata()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("/* source */");
        var node = AstTestHelpers.FindFirst<ExpectedNodeType>(pipeline.Analyzed);
        // Assert on SlotMap, ScopeId, etc.
        Assert.True(node.SlotMap.ContainsKey(Symbol.Create("x")));
    }

    // ==================== LAYER 3: Plans ====================
    [Fact]
    public void L3_Plan_BuiltCorrectly()
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze("/* source */");
        var node = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);
        var plan = ((IAstCacheable<LoopPlan>)node).GetOrCreateCache();
        // Assert on plan structure
    }

    // ==================== LAYER 4: Runtime Logging ====================
    [Fact]
    public async Task L4_Runtime_InternalOperationsCorrect()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("/* source */");

        var messages = logger.Collector.Snapshot();
        // Assert on log messages
        Assert.Contains(messages, m => m.Message.Contains("Expected operation"));
    }

    // ==================== LAYER 5: Full Result ====================
    [Fact]
    public async Task L5_FullExecution_ProducesCorrectResult()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("/* source */");
        Assert.Equal(expected, result);
    }
}
```

### Benefits

- **Isolation** - Pinpoint exactly which layer is broken
- **No guessing** - Each layer either works or doesn't
- **Fast debugging** - Parser tests run instantly (no evaluation overhead)
- **Regression protection** - Each layer test catches bugs at that level
- **Documentation** - Tests explain what each layer should produce
- **Incremental fixes** - Fix one layer at a time, verify before moving on

### Layered Tests vs Test Bombs

| Aspect | Layered Tests | Test Bombs |
|--------|--------------|------------|
| **Purpose** | Isolate which *pipeline stage* fails | Eliminate *hypotheses* about root cause |
| **Structure** | Sequential: L1 → L2 → L3 → L4 → L5 | Parallel: H1, H2, H3 run independently |
| **Naming** | `L1_`, `L2_`, `L3_`... | `H1_`, `H2_`, `H3_`... |
| **When to use** | Know the pipeline, unsure which stage | Unclear what's wrong at all |
| **Insight** | "The bug is in the analyzer" | "The test itself was wrong" |

**Use both together**: Start with a Test Bomb to identify the component, then use Layered Tests to find the exact stage within that component
