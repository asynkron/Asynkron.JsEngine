using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify per-iteration environment behavior in IR-executed loops.
/// These tests check that closures capture correct values in for loops with let bindings.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
public sealed class IrLoopEnvironmentTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task SyncForLoop_ClosuresCaptureCorrectValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function foo() {
                const funcs = [];
                for (let i = 0; i < 3; i++) {
                    funcs.push(() => i);
                    let x = () => {};
                }
                return funcs.map(f => f());
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(3, array.Length);
        Assert.Equal(0.0, array.GetElement(0).AsDouble());
        Assert.Equal(1.0, array.GetElement(1).AsDouble());
        Assert.Equal(2.0, array.GetElement(2).AsDouble());
    }

    [Fact(Timeout = 5000)]
    public async Task ForLoop_VarDoesNotCreatePerIterationBindings()
    {
        // With 'var', all closures should capture the same binding
        // and see the final value (3)
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function foo() {
                const funcs = [];
                for (var i = 0; i < 3; i++) {
                    funcs.push(() => i);
                }
                return funcs.map(f => f());
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(3, array.Length);
        // All closures see the final value
        Assert.Equal(3.0, array.GetElement(0).AsDouble());
        Assert.Equal(3.0, array.GetElement(1).AsDouble());
        Assert.Equal(3.0, array.GetElement(2).AsDouble());
    }

    [Fact(Timeout = 5000)]
    public async Task SyncForLoop_SimpleIteration()
    {
        // Simpler test without closures - just verify loop iteration works
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function foo() {
                const values = [];
                for (let x = 0; x < 5; x++) {
                    values.push(x);
                }
                return values;
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(5, array.Length);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(i, array.GetElement(i).AsDouble());
        }
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunction_ForLoop_ClosuresCaptureCorrectValues()
    {
        // This tests the IR path - async functions use ExecutionPlanRunner
        // Closures should capture 0, 1, 2 - sum should be 3
        await using var engine = CreateEngine();
        await engine.Evaluate("""
            let result = 0;
            async function foo() {
                const funcs = [];
                for (let i = 0; i < 3; i++) {
                    funcs.push(() => i);
                    await Promise.resolve();
                }
                result = funcs[0]() + funcs[1]() + funcs[2]();
            }
            foo();
            """);

        // 0 + 1 + 2 = 3
        var result = await engine.Evaluate("result;");
        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunction_ForLoop_SimpleIteration()
    {
        // Simpler test - just verify values are summed correctly during iteration
        await using var engine = CreateEngine();
        await engine.Evaluate("""
            let result = 0;
            async function foo() {
                let sum = 0;
                for (let x = 0; x < 5; x++) {
                    sum += x;
                    await Promise.resolve();
                }
                result = sum;
            }
            foo();
            """);

        // 0 + 1 + 2 + 3 + 4 = 10
        var result = await engine.Evaluate("result;");
        Assert.Equal(10.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task PrintIrExecutionPlan_ForLoopWithClosures()
    {
        // This test demonstrates the IR pretty printer for debugging
        await using var engine = CreateEngine();

        // Parse and evaluate to trigger execution plan building
        var program = engine.ParseProgram("""
            async function testLoop() {
                const funcs = [];
                for (let i = 0; i < 3; i++) {
                    funcs.push(() => i);
                    await Promise.resolve();
                }
                return funcs;
            }
            """);

        // Evaluate to trigger scope analysis and plan building
        await engine.Evaluate(program);

        // Get the function declaration and print its execution plan
        var funcDecl = program.Body[0] as Asynkron.JsEngine.Ast.FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        Assert.NotNull(planOutput);

        // Output to test log for inspection
        output.WriteLine("=== IR Execution Plan for async for-loop with closures ===");
        output.WriteLine(planOutput);

        // Verify plan was created (not a fallback)
        Assert.DoesNotContain("No execution plan available", planOutput);
    }

    [Fact(Timeout = 5000)]
    public async Task PrintIrExecutionPlan_NestedLoops()
    {
        // This test prints the IR for nested loops to help debug issues
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function nestedLoops() {
                const arr = [1, 2];
                let sum = 0;
                for (let i = 0; i < 2; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
                return sum;
            }
            """);

        await engine.Evaluate(program);

        var funcDecl = program.Body[0] as Asynkron.JsEngine.Ast.FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        Assert.NotNull(planOutput);

        output.WriteLine("=== IR Execution Plan for nested for + for-await-of loops ===");
        output.WriteLine(planOutput);

        Assert.DoesNotContain("No execution plan available", planOutput);
    }

    [Fact(Timeout = 5000)]
    public async Task TraceIrExecution_ForLoopWithClosures()
    {
        // This test demonstrates IR execution tracing with a logger
        // The logger outputs each instruction as it executes
        var logger = new TestLogger(output, "IR-Trace");

        await using var engine = new JsEngine(new JsEngineOptions
        {
            Logger = logger
        });

        output.WriteLine("=== Executing async for-loop with closures (trace below) ===");
        output.WriteLine("");

        // Define and call the async function, then get the result
        await engine.EvaluateAndAwait("""
            let result;
            (async function testLoop() {
                const funcs = [];
                for (let i = 0; i < 2; i++) {
                    funcs.push(() => i);
                    await Promise.resolve();
                }
                result = funcs[0]() + funcs[1]();
            })();
            """);

        var result = await engine.Evaluate("result");

        output.WriteLine("");
        output.WriteLine($"=== Result: {result} (expected: 1 = 0 + 1) ===");

        // 0 + 1 = 1
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TraceIrExecution_PASSING_SingleForWithForAwaitOf()
    {
        // PASSING CASE: 1 for loop + 1 for-await-of (sum = 6)
        var logger = new TestLogger(output, "PASS");

        await using var engine = new JsEngine(new JsEngineOptions { Logger = logger });

        output.WriteLine("=== PASSING: 1 for + 1 for-await-of (expected: 6) ===");

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        output.WriteLine($"=== Result: {result} (expected: 6) ===");
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TraceIrExecution_FAILING_DoubleForWithForAwaitOf()
    {
        // FAILING CASE: 2 for loops + 1 for-await-of (expected: 12, actual: 6)
        var logger = new TestLogger(output, "FAIL");

        await using var engine = new JsEngine(new JsEngineOptions { Logger = logger });

        output.WriteLine("=== FAILING: 2 for + 1 for-await-of (expected: 12) ===");

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        output.WriteLine($"=== Result: {result} (expected: 12, getting half = 6) ===");
        // Don't assert - we know it fails, we just want to see the trace
    }

    [Fact(Timeout = 5000)]
    public async Task PrintIrPlan_DoubleForWithForAwaitOf()
    {
        // Print the IR plan for the failing case to analyze structure
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function doubleNestedBug() {
                const arr = [1, 2];
                let sum = 0;
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
                return sum;
            }
            """);

        await engine.Evaluate(program);

        var funcDecl = program.Body[0] as Asynkron.JsEngine.Ast.FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        Assert.NotNull(planOutput);

        output.WriteLine("=== IR Plan for 2 for + 1 for-await-of (FAILING CASE) ===");
        output.WriteLine(planOutput);
    }

    [Fact(Timeout = 10000)]
    public async Task SingleForLoop_WithForAwaitOf_ReturnsCorrectSum()
    {
        // Simpler test: just j + for-await-of (no outer i loop)
        // No logger to avoid initialization noise
        await using var engine = CreateEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let j = 0; j < 2; j++) {
                    for await (const n of arr) {
                        sum += n;
                    }
                }
            })();
            sum;
            """);

        Output.WriteLine($"Result: {result} (expected: 6)");
        // 2 j-iterations × (1+2) = 2 × 3 = 6
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task NestedForLoops_WithForAwaitOf_ReturnsCorrectSum()
    {
        // No logger to avoid initialization noise and hangs
        await using var engine = CreateEngine();

        var result = await engine.EvaluateAndAwait("""
            let sum = 0;
            (async function() {
                const arr = [1, 2];
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        for await (const n of arr) {
                            sum += n;
                        }
                    }
                }
            })();
            sum;
            """);

        output.WriteLine($"Result: {result} (expected: 12)");
        // 2 i-iterations × 2 j-iterations × (1+2) = 4 × 3 = 12
        Assert.Equal(12.0, result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedLoops_NoStatementInstructionFallback()
    {
        // This test verifies that nested loops are fully emitted as IR instructions,
        // with no StatementInstruction fallback for any level of nesting.
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testNestedLoops() {
                let sum = 0;
                for (let i = 0; i < 2; i++) {
                    while (sum < 10) {
                        for (const x of [1, 2]) {
                            for (let k in {a: 1, b: 2}) {
                                sum += x;
                            }
                        }
                        break;
                    }
                }
                return sum;
            }
            """);

        // Execute to ensure it works correctly
        await engine.Evaluate(program);
        var result = await engine.Evaluate("testNestedLoops()");
        Assert.Equal(12.0, result); // 2 outer × 2 for-of × 2 for-in = 2×(1+2+1+2) = 12

        // Now verify the execution plan has no StatementInstruction
        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var statementInstructions = cache.Plan.Instructions
            .Where(i => i is StatementInstruction)
            .ToList();

        if (statementInstructions.Count > 0)
        {
            foreach (var si in statementInstructions.Cast<StatementInstruction>())
            {
                output.WriteLine($"Found StatementInstruction for: {si.Statement.GetType().Name}");
            }
        }

        Assert.Empty(statementInstructions);

        // Print full IR
        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== Full IR for nested loops ===");
        output.WriteLine(planOutput);
        output.WriteLine($"✓ All {cache.Plan.Instructions.Length} instructions are proper IR (no StatementInstruction fallback)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Array Destructuring IR Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_SimpleBinding_ReturnsCorrectValues()
    {
        // Test simple array destructuring produces correct values
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                const [a, b, c] = [1, 2, 3];
                return [a, b, c];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(3, array.Length);
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(2.0, array.GetElement(1).AsDouble());
        Assert.Equal(3.0, array.GetElement(2).AsDouble());
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_WithRest_ReturnsCorrectValues()
    {
        // Test array destructuring with rest element
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                const [first, ...rest] = [1, 2, 3, 4, 5];
                return [first, rest.length, rest[0], rest[1]];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(4, array.Length);
        Assert.Equal(1.0, array.GetElement(0).AsDouble()); // first
        Assert.Equal(4.0, array.GetElement(1).AsDouble()); // rest.length
        Assert.Equal(2.0, array.GetElement(2).AsDouble()); // rest[0]
        Assert.Equal(3.0, array.GetElement(3).AsDouble()); // rest[1]
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_WithHoles_ReturnsCorrectValues()
    {
        // Test array destructuring with holes (skipped elements)
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                const [a, , c] = [1, 2, 3];
                return [a, c];
            })();
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2, array.Length);
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(3.0, array.GetElement(1).AsDouble());
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_InAsyncFunction_ReturnsCorrectValues()
    {
        // Test array destructuring in async function context (uses IR execution)
        await using var engine = CreateEngine();
        await engine.Evaluate("""
            let result;
            async function test() {
                const arr = [10, 20, 30];
                await Promise.resolve();
                const [x, y, z] = arr;
                result = x + y + z;
            }
            test();
            """);

        var result = await engine.Evaluate("result");
        Assert.Equal(60.0, result); // 10 + 20 + 30
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_UsesIrInstructions()
    {
        // Verify that simple array destructuring emits IR instructions
        // instead of falling back to ComplexVariableDeclarationInstruction
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testArrayDestructuring() {
                const [a, b, c] = [1, 2, 3];
                return a + b + c;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testArrayDestructuring()");
        Assert.Equal(6.0, result); // 1 + 2 + 3

        // Verify the execution plan uses ArrayDestructuring instructions
        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        // Check for ArrayDestructuringInit instruction (indicates IR emission was used)
        var hasArrayDestructuringInit = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.ArrayDestructuringInit);

        // Check we don't have ComplexVariableDeclarationInstruction for this
        var hasComplexVarDecl = cache.Plan.Instructions
            .Any(i => i is ComplexVariableDeclarationInstruction);

        // Print IR for debugging
        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for array destructuring ===");
        output.WriteLine(planOutput);

        Assert.True(hasArrayDestructuringInit,
            "Simple array destructuring should emit ArrayDestructuringInit instruction");
        Assert.False(hasComplexVarDecl,
            "Simple array destructuring should not use ComplexVariableDeclarationInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayDestructuring_WithRest_UsesIrInstructions()
    {
        // Verify that array destructuring with rest emits IR instructions
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testRestDestructuring() {
                const [first, ...rest] = [1, 2, 3, 4];
                return first + rest.length;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testRestDestructuring()");
        Assert.Equal(4.0, result); // 1 + 3 (rest.length)

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        // Check for ArrayDestructuringRest instruction
        var hasArrayDestructuringRest = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.ArrayDestructuringRest);

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for array destructuring with rest ===");
        output.WriteLine(planOutput);

        Assert.True(hasArrayDestructuringRest,
            "Array destructuring with rest should emit ArrayDestructuringRest instruction");
    }
}
