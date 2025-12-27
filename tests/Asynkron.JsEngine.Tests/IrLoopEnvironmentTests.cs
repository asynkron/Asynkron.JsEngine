using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify per-iteration environment behavior in IR-executed loops.
/// These tests check that closures capture correct values in for loops with let bindings.
/// </summary>
public class IrLoopEnvironmentTests(ITestOutputHelper output) : FastPathTestBase(output)
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
}
