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
    public async Task NestedLoops_NoGenericStatementFallback()
    {
        // This test verifies that nested loops are fully emitted as IR instructions,
        // with no generic EvaluateAndDiscard fallback for any level of nesting.
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

        // Now verify the execution plan has no generic statement fallback.
        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var fallbackInstructions = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .ToList();

        if (fallbackInstructions.Count > 0)
        {
            foreach (var fallback in fallbackInstructions)
            {
                output.WriteLine($"Found EvaluateAndDiscardInstruction for: {fallback.Expression?.GetType().Name ?? "<lowered>"}");
            }
        }

        Assert.Empty(fallbackInstructions);

        // Print full IR
        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== Full IR for nested loops ===");
        output.WriteLine(planOutput);
        output.WriteLine($"✓ All {cache.Plan.Instructions.Length} instructions are proper IR (no generic statement fallback)");
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
        // Verify that simple array destructuring emits specialized IR instructions
        // instead of using the generic binding declaration instruction.
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

        // Check we don't have the generic binding declaration instruction for this
        var hasBindingVarDecl = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.BindingVariableDeclaration);

        // Print IR for debugging
        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for array destructuring ===");
        output.WriteLine(planOutput);

        Assert.True(hasArrayDestructuringInit,
            "Simple array destructuring should emit ArrayDestructuringInit instruction");
        Assert.False(hasBindingVarDecl,
            "Simple array destructuring should not use the generic binding declaration instruction");
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

    [Fact(Timeout = 5000)]
    public async Task ObjectDestructuring_UsesBindingDeclarationInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testObjectDestructuring() {
                const { x, y = 2, ...rest } = { x: 1, z: 5 };
                return x + y + rest.z;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testObjectDestructuring()");
        Assert.Equal(8.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasBindingVarDecl = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.BindingVariableDeclaration);
        var bindingInstruction = Assert.Single(cache.Plan.Instructions.OfType<BindingVariableDeclarationInstruction>());

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for object destructuring ===");
        output.WriteLine(planOutput);

        Assert.True(hasBindingVarDecl,
            "Object destructuring should emit the generic binding declaration instruction");
        Assert.Null(bindingInstruction.Initializer);
        Assert.NotNull(bindingInstruction.InitializerProgram);
    }

    [Fact(Timeout = 5000)]
    public async Task MixedDeclaratorChain_UsesBindingAndArrayDestructuringInstructions()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testMixedDeclarators() {
                const { x, y = 2 } = { x: 1 }, [a, ...rest] = [3, 4, 5];
                return x + y + a + rest.length;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testMixedDeclarators()");
        Assert.Equal(8.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasBindingVarDecl = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.BindingVariableDeclaration);
        var hasArrayDestructuringInit = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.ArrayDestructuringInit);

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for mixed declarators ===");
        output.WriteLine(planOutput);

        Assert.True(hasBindingVarDecl,
            "Mixed declarators should keep object destructuring on the IR runner");
        Assert.True(hasArrayDestructuringInit,
            "Mixed declarators should preserve specialized array destructuring IR");
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleAssignmentExpression_UsesAssignmentInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testAssignmentInstruction() {
                let total = 1;
                total = total + 4;
                return total;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testAssignmentInstruction()");
        Assert.Equal(5.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);
        var hasAssignmentEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is AssignmentExpression { Target.Name: "total" });

        var planOutput = ExecutionPlanDiagnostics.PrintPlan(funcDecl.Function);
        output.WriteLine("=== IR for simple assignment expression ===");
        output.WriteLine(planOutput);

        Assert.True(hasAssignmentInstruction,
            "Simple identifier assignment should emit AssignmentSlotInstruction");
        Assert.False(hasAssignmentEvaluateAndDiscard,
            "Simple identifier assignment should no longer use EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncAssignmentExpression_UsesAssignmentInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function testAsyncAssignmentInstruction() {
                let total = 0;
                total = await Promise.resolve(7);
                return total;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.EvaluateAndAwait("""
            let asyncAssignmentResult = undefined;
            testAsyncAssignmentInstruction().then(value => asyncAssignmentResult = value);
            asyncAssignmentResult;
            """);
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);

        Assert.True(hasAssignmentInstruction,
            "Async simple assignment should stay on AssignmentSlotInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task AwaitExpressionStatement_UsesAwaitInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function testAwaitStatementInstruction() {
                await Promise.resolve(1);
                return 7;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.EvaluateAndAwait("""
            let awaitStatementResult = undefined;
            testAwaitStatementInstruction().then(value => awaitStatementResult = value);
            awaitStatementResult;
            """);
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasAwaitInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AwaitAndDiscard);
        var hasAwaitEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is AwaitExpression);

        Assert.True(hasAwaitInstruction,
            "Plain await expression statements should emit AwaitAndDiscardInstruction");
        Assert.False(hasAwaitEvaluateAndDiscard,
            "Plain await expression statements should no longer use EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task CompoundAssignmentExpression_UsesCompoundAssignmentInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testCompoundAssignmentInstruction() {
                let total = 3;
                let delta = 4;
                total += delta;
                return total;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testCompoundAssignmentInstruction()");
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasCompoundInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.CompoundAssignmentSlot);
        var hasCompoundEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is AssignmentExpression
                {
                    IsCompoundAssignment: true,
                    Value: BinaryExpression { Operator: BinaryOperator.Add },
                    Target.Name: "total"
                });

        Assert.True(hasCompoundInstruction,
            "Simple arithmetic compound assignments should emit CompoundAssignmentSlotInstruction");
        Assert.False(hasCompoundEvaluateAndDiscard,
            "Simple arithmetic compound assignments should no longer use EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalCompoundAssignment_UsesLogicalAssignmentInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testLogicalCompoundInstruction() {
                let a = 0;
                let b = 1;
                let c = 2;
                let calls = 0;

                function nextValue() {
                    calls++;
                    return 7;
                }

                a ||= nextValue();
                b &&= nextValue();
                c ??= nextValue();

                return a + b + c + calls;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testLogicalCompoundInstruction()");
        Assert.Equal(18.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var logicalInstructionCount = cache.Plan.Instructions
            .Count(i => i.Kind == InstructionKind.LogicalCompoundAssignmentSlot);
        var hasLogicalEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is AssignmentExpression
                {
                    IsCompoundAssignment: true,
                    Value: BinaryExpression
                    {
                        Operator: BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing
                    }
                });

        Assert.Equal(3, logicalInstructionCount);
        Assert.False(hasLogicalEvaluateAndDiscard,
            "Logical compound assignments should no longer use EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncLogicalCompoundAssignment_UsesLogicalAssignmentInstruction()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function testAsyncLogicalCompoundInstruction() {
                let value = 0;
                value ||= await Promise.resolve(7);
                return value;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.EvaluateAndAwait("""
            let asyncLogicalCompoundResult = undefined;
            testAsyncLogicalCompoundInstruction().then(value => asyncLogicalCompoundResult = value);
            asyncLogicalCompoundResult;
            """);
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasLogicalInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.LogicalCompoundAssignmentSlot);

        Assert.True(hasLogicalInstruction,
            "Async logical compound assignments should stay on LogicalCompoundAssignmentSlotInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task SequenceExpression_ReusesDedicatedStatementInstructions()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testSequenceInstruction() {
                let total = 0;
                let calls = 0;

                function nextValue() {
                    calls++;
                    return 7;
                }

                total = 1, total ||= nextValue();
                return total + calls;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("testSequenceInstruction()");
        Assert.Equal(1.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);
        var hasLogicalInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.LogicalCompoundAssignmentSlot);
        var hasSequenceEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is SequenceExpression);

        Assert.True(hasAssignmentInstruction,
            "Sequence-expression left legs should reuse AssignmentSlotInstruction when available");
        Assert.True(hasLogicalInstruction,
            "Sequence-expression right legs should reuse LogicalCompoundAssignmentSlotInstruction when available");
        Assert.False(hasSequenceEvaluateAndDiscard,
            "Sequence expression statements should no longer stay on EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncSequenceExpression_ReusesAwaitAndAssignmentInstructions()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function testAsyncSequenceInstruction() {
                let total = 0;
                await Promise.resolve("tick"), total = 7;
                return total;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.EvaluateAndAwait("""
            let asyncSequenceResult = undefined;
            testAsyncSequenceInstruction().then(value => asyncSequenceResult = value);
            asyncSequenceResult;
            """);
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasAwaitInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AwaitAndDiscard);
        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);
        var hasSequenceEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is SequenceExpression);

        Assert.True(hasAwaitInstruction,
            "Async sequence-expression left legs should reuse AwaitAndDiscardInstruction");
        Assert.True(hasAssignmentInstruction,
            "Async sequence-expression right legs should reuse AssignmentSlotInstruction");
        Assert.False(hasSequenceEvaluateAndDiscard,
            "Async sequence expression statements should no longer stay on EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task ConditionalExpression_ReusesBranchAndDedicatedStatementInstructions()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            function testConditionalInstruction(flag) {
                let total = 0;
                let calls = 0;

                function nextValue() {
                    calls++;
                    return 7;
                }

                flag ? total = 1 : total ||= nextValue();
                return total + calls;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.Evaluate("""
            [testConditionalInstruction(true), testConditionalInstruction(false)];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2, array.Length);
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(8.0, array.GetElement(1).AsDouble());

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasBranchInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.Branch);
        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);
        var hasLogicalInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.LogicalCompoundAssignmentSlot);
        var hasConditionalEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is ConditionalExpression);

        Assert.True(hasBranchInstruction,
            "Conditional expression statements should reuse BranchInstruction");
        Assert.True(hasAssignmentInstruction,
            "Conditional consequent legs should reuse AssignmentSlotInstruction when available");
        Assert.True(hasLogicalInstruction,
            "Conditional alternate legs should reuse LogicalCompoundAssignmentSlotInstruction when available");
        Assert.False(hasConditionalEvaluateAndDiscard,
            "Conditional expression statements should no longer stay on EvaluateAndDiscardInstruction");
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncConditionalExpression_ReusesBranchAwaitAndAssignmentInstructions()
    {
        await using var engine = CreateEngine();

        var program = engine.ParseProgram("""
            async function testAsyncConditionalInstruction(flag) {
                let total = 0;
                flag ? await Promise.resolve("tick") : total = 7;
                return total;
            }
            """);

        await engine.Evaluate(program);
        var result = await engine.EvaluateAndAwait("""
            let asyncConditionalResult = undefined;
            testAsyncConditionalInstruction(false).then(value => asyncConditionalResult = value);
            asyncConditionalResult;
            """);
        Assert.Equal(7.0, result);

        var funcDecl = program.Body[0] as FunctionDeclaration;
        Assert.NotNull(funcDecl);

        var cache = ((IAstCacheable<ExecutionPlanCache>)funcDecl.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build successfully. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);

        var hasBranchInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.Branch);
        var hasAwaitInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AwaitAndDiscard);
        var hasAssignmentInstruction = cache.Plan.Instructions
            .Any(i => i.Kind == InstructionKind.AssignmentSlot);
        var hasConditionalEvaluateAndDiscard = cache.Plan.Instructions
            .OfType<EvaluateAndDiscardInstruction>()
            .Any(i => i.Expression is ConditionalExpression);

        Assert.True(hasBranchInstruction,
            "Async conditional expression statements should reuse BranchInstruction");
        Assert.True(hasAwaitInstruction,
            "Async conditional consequent legs should reuse AwaitAndDiscardInstruction");
        Assert.True(hasAssignmentInstruction,
            "Async conditional alternate legs should reuse AssignmentSlotInstruction");
        Assert.False(hasConditionalEvaluateAndDiscard,
            "Async conditional expression statements should no longer stay on EvaluateAndDiscardInstruction");
    }
}
