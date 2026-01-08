using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.Regression)]
public sealed class FunctionDeclarationDupTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task GlobalDuplicateDeclarations_UseLastDefinition()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            function f() { return 1; }
            function f() { return 2; }
            f();
            """);

        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task DuplicateDeclarations_AreHoistedAndOverrideEarlierCalls()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var first = f();
            function f() { return "first"; }
            function f() { return "second"; }
            var second = f();
            [first, second];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("second", array.GetElement(0).AsString());
        Assert.Equal("second", array.GetElement(1).AsString());
    }
}
