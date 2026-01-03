using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.CompletionValues)]
public sealed class SwitchCompletionSimpleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Test1_EmptyDefaultCase()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('1; switch (\"a\") { default: }')");
        output.WriteLine($"Empty default: {r}");
        // Expected: undefined
    }

    [Fact]
    public async Task Test2_ValueThenBreak()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('switch (1) { case 1: 7; break; }')");
        output.WriteLine($"7; break: {r}");
        Assert.Equal(7.0, r);
    }

    [Fact]
    public async Task Test3_FallThroughValueThenBreak()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('switch (\"a\") { case \"a\": 7; case \"b\": break; }')");
        output.WriteLine($"case a: 7; case b: break: {r}");
        Assert.Equal(7.0, r);
    }

    [Fact]
    public async Task Test4_FallThroughValueThenValue()
    {
        await using var engine = new JsEngine();
        var r = await engine.Evaluate("eval('switch (\"a\") { case \"a\": 7; case \"b\": 8; }')");
        output.WriteLine($"case a: 7; case b: 8: {r}");
        Assert.Equal(8.0, r);
    }
}
