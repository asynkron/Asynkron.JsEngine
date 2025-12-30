using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SwitchCompletionDiagTests(ITestOutputHelper output)
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
        // Expected: 7 (second if is false, so completion from first if stays)
        Assert.Equal(7.0, r);
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
