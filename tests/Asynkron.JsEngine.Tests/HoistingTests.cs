using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class HoistingTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task FunctionDeclaration_ShouldOverride_Parameter()
    {
        // S10.2.1_A4_T1 - Function declaration should override parameter with same name
        var testLogger = new TestLogger(output);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = testLogger });
        var result = await engine.Evaluate(@"
            function f(x) {
                return typeof x;
                function x() {
                    return 7;
                }
            }
            f()
        ");
        // Log debug output
        foreach (var log in testLogger.Collector.Snapshot())
        {
            output.WriteLine(log.Message);
        }
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task FunctionDeclaration_ShouldOverride_Parameter_WithArg()
    {
        // S10.2.1_A4_T1 - Function declaration should override parameter even when arg passed
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function f(x) {
                return typeof x;
                function x() {
                    return 7;
                }
            }
            f(1)
        ");
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task VarDeclaration_ShouldNotOverride_Parameter()
    {
        // S10.2.1_A5.2_T1 - var x; should NOT override parameter value
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            (function f(x) {
                var x;
                return typeof x;
            })(1)
        ");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task FunctionDeclaration_ShouldOverride_Var()
    {
        // S10.2.1_A4_T2 - Function declaration should override var
        var testLogger = new TestLogger(output);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = testLogger });
        var result = await engine.Evaluate(@"
            function f() {
                var x;
                return typeof x;
                function x() {}
            }
            f()
        ");
        // Log debug output
        foreach (var log in testLogger.Collector.Snapshot())
        {
            output.WriteLine(log.Message);
        }
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task FunctionDeclaration_ShouldOverride_Arguments()
    {
        // S10.2.1_A4_T1 - function arguments() should override built-in arguments
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function f() {
                return typeof arguments;
                function arguments() {
                    return 7;
                }
            }
            f()
        ");
        Assert.Equal("function", result);
    }
}
