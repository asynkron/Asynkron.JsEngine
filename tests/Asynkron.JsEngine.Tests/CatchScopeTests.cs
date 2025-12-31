using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class CatchScopeTests
{
    private readonly ITestOutputHelper _output;

    public CatchScopeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 10000)]
    public async Task SimpleCatchLetScopingTest()
    {
        // Simplified test: catch block should not leak let bindings
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
let x = 'outside';

try {
    throw new Error();
} catch (e) {
    let x = 'inside';
}

x
");

        _output.WriteLine($"x = {result}");

        // x should still be 'outside' - the let inside catch shouldn't affect outer x
        Assert.Equal("outside", result?.ToString());
    }

    [Fact(Timeout = 10000)]
    public async Task CatchBlockShouldHaveSeparateLexicalScope()
    {
        // This is the exact test case from scope-catch-block-lex-open.js
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
let x = 'outside';
let probeParam, probeBlock;

try {
    throw [];
} catch ([_ = probeParam = function() { return x; }]) {
    probeBlock = function() { return x; };
    let x = 'inside';
}

[probeParam(), probeBlock()]
");

        var array = Assert.IsType<JsArray>(result);
        var paramResult = array.GetElement(0);
        var blockResult = array.GetElement(1);

        _output.WriteLine($"probeParam() = {paramResult}");
        _output.WriteLine($"probeBlock() = {blockResult}");

        Assert.Equal("outside", paramResult.AsString());
        Assert.Equal("inside", blockResult.AsString());
    }

    [Fact(Timeout = 10000)]
    public async Task OptionalCatchBindingShouldHaveSeparateLexicalScope()
    {
        // This is the test case from optional-catch-binding-lexical.js
        await using var engine = new JsEngine();

        // Simpler test first - does catch {} work at all?
        var result = await engine.Evaluate(@"
let x = 1;

try {
    x = 2;
    throw new Error();
} catch {
    let x = 3;  // This should NOT affect the outer x
}

x
");

        _output.WriteLine($"x = {result}");

        // x should still be 2, not 3
        Assert.Equal(2.0, result);
    }
}
