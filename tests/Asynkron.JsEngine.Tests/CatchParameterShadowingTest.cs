using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class CatchParameterShadowingTest(ITestOutputHelper output)
{
    [Fact(Skip = "Bug #479: Catch parameter shadowing causes duplicate declaration errors")]
    public async Task CatchParameterShouldShadowVarVariable()
    {
        var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

        // First verify var a = 1 works
        var code1 = @"
function fn() {
    var a = 1;
    return a;
}
fn();
";
        var result1 = await engine.Evaluate(code1);
        output.WriteLine($"Without catch: a = {result1}");
        Assert.Equal(1.0, result1);

        // Now test with catch that doesn't use same name
        var code2 = @"
function fn() {
    var a = 1;
    try {
        throw 'stuff3';
    } catch (b) {
        // different parameter name
    }
    return a;
}
fn();
";
        var result2 = await engine.Evaluate(code2);
        output.WriteLine($"With catch(b): a = {result2}");
        Assert.Equal(1.0, result2);

        // Now test the actual failing case
        var code3 = @"
function fn() {
    var a = 1;
    try {
        throw 'stuff3';
    } catch (a) {
        // catch parameter shadowing var variable
    }
    return a;
}
fn();
";
        var result3 = await engine.Evaluate(code3);
        output.WriteLine($"With catch(a): a = {result3}");
        Assert.Equal(1.0, result3);
    }
}
