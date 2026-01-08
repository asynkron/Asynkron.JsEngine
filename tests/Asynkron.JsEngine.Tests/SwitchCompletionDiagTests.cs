using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Diagnostics)]
[Category(TestCategories.CompletionValues)]
public sealed class SwitchCompletionDiagTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SimpleIf_ThenValue()
    {
        await using var engine = new JsEngine();
        // Just an if statement that executes its then branch
        var r = await engine.Evaluate("eval('if (true) { 7; }')");
        output.WriteLine($"if (true) 7: {r}");
        Assert.Equal(7.0, r);
    }

    [Fact]
    public async Task TwoIfs_FirstTrueSetsValue()
    {
        await using var engine = new JsEngine();
        // Two if statements, first one true with value, second true but no value
        var r = await engine.Evaluate("eval('if (true) { 7; } if (true) {}')");
        output.WriteLine($"if7, if empty: {r}");
        // Second if with empty body should set completion to undefined
    }

    [Fact]
    public async Task TwoIfs_FirstTrueSetsValue_SecondFalse()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('if (true) { 7; } if (false) {}')");
        output.WriteLine($"if7, if false: {r}");
        // Per ES spec 14.6.2, when condition is false with no else, result is undefined.
        // This DOES update the completion value (verified with Node.js/Chrome).
        Assert.Equal("undefined", r?.ToString());
    }

    [Fact]
    public async Task UndefinedThenValue()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('undefined; 7;')");
        output.WriteLine($"undefined; 7: {r}");
        Assert.Equal(7.0, r);
    }
}
