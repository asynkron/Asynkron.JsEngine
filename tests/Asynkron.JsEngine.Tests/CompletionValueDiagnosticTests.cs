using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests for completion value semantics.
/// </summary>
[Category(TestCategories.Diagnostics)]
[Category(TestCategories.CompletionValues)]
public sealed class CompletionValueDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Switch_BreakEmpty_ReturnsUndefined()
    {
        await using var engine = new JsEngine();

        // Test 1: bare break should give undefined
        var r1 = await engine.Evaluate("eval('1; switch (\"a\") { case \"a\": break; default: }')");
        output.WriteLine($"Test 1: {r1}");
        output.WriteLine($"Expected: undefined");
        Assert.True(r1?.ToString() == "undefined" || r1 == null, $"Expected undefined, got {r1}");
    }

    [Fact]
    public async Task Switch_ValueThenBreak_ReturnsValue()
    {
        await using var engine = new JsEngine();

        // Test 2: value before break should give the value
        var r2 = await engine.Evaluate("eval('2; switch (\"a\") { case \"a\": { 3; break; } default: }')");
        output.WriteLine($"Test 2: {r2}");
        output.WriteLine($"Expected: 3");
        Assert.Equal(3.0, r2);
    }

    [Fact]
    public async Task Switch_SimpleBreak_ReturnsUndefined()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate("eval('switch (1) { case 1: break; }')");
        output.WriteLine($"Simple break: {r}");
        Assert.True(r?.ToString() == "undefined" || r == null, $"Expected undefined, got {r}");
    }

    [Fact]
    public async Task Switch_ValueBeforeBreak_ReturnsValue()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate("eval('switch (1) { case 1: 42; break; }')");
        output.WriteLine($"42; break: {r}");
        Assert.Equal(42.0, r);
    }

    [Fact]
    public async Task If_AbruptEmpty_ReturnsUndefined()
    {
        await using var engine = new JsEngine();

        // From test262: cptn-no-else-true-abrupt-empty
        var r = await engine.Evaluate("eval('1; do { if (true) break; } while (false)')");
        output.WriteLine($"if break: {r}");
        Assert.True(r?.ToString() == "undefined" || r == null, $"Expected undefined, got {r}");
    }

    [Fact]
    public async Task If_ValueThenBreak_ReturnsValue()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate("eval('1; do { if (true) { 2; break; } } while (false)')");
        output.WriteLine($"if (2; break): {r}");
        Assert.Equal(2.0, r);
    }

    // These are the specific failing Test262 cases
    [Fact]
    public async Task Switch_ContinueEmpty_InsideDoWhile_ReturnsUndefined()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate("eval('4; do { switch (\"a\") { case \"a\": continue; default: } } while (false)')");
        output.WriteLine($"switch continue: {r}");
        Assert.True(r?.ToString() == "undefined" || r == null, $"Expected undefined, got {r}");
    }

    [Fact]
    public async Task Switch_ValueThenContinue_InsideDoWhile_ReturnsValue()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate("eval('5; do { switch (\"a\") { case \"a\": { 6; continue; } default: } } while (false)')");
        output.WriteLine($"switch 6; continue: {r}");
        Assert.Equal(6.0, r);
    }

    // Test without eval to see if the issue is in switch or eval
    [Fact]
    public async Task Switch_ContinueEmpty_InsideDoWhile_NoEval()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate(@"
            var result;
            do {
                switch ('a') {
                    case 'a': continue;
                    default:
                }
            } while (false);
            result;
        ");
        output.WriteLine($"switch continue (no eval): {r}");
        // Result should be undefined
    }

    [Fact]
    public async Task Switch_ValueThenContinue_InsideDoWhile_NoEval()
    {
        await using var engine = new JsEngine();

        var r = await engine.Evaluate(@"
            var result;
            do {
                switch ('a') {
                    case 'a': {
                        result = 6;
                        continue;
                    }
                    default:
                }
            } while (false);
            result;
        ");
        output.WriteLine($"switch 6; continue (no eval): {r}");
        Assert.Equal(6.0, r);
    }

    [Fact(Timeout = 10000)]
    public async Task SwitchFallThrough_ValueThenBreak()
    {
        await using var engine = new JsEngine();
        // case "a": 7; falls through to case "b": break;
        // Expected: 7 (break has empty completion, UpdateEmpty fills from V=7)
        var result = await engine.Evaluate("eval('6; switch (\"a\") { case \"a\": 7; case \"b\": break; }')");
        output.WriteLine($"Result: {result}");
        Assert.Equal(7.0, result);
    }
}
