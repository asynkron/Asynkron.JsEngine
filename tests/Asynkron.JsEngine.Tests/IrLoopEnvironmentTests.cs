using Asynkron.JsEngine.JsTypes;
using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify per-iteration environment behavior in IR-executed loops.
/// These tests check that closures capture correct values in for loops with let bindings.
/// </summary>
public class IrLoopEnvironmentTests(JsEngineTestFixture fixture) : JsEngineTestBase(fixture)
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
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((double)i, array.GetElement(i).AsDouble());
        }
    }
}
