using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Regression)]
public sealed class NestedFunctionScopeRegressionTests : InternalTestBase
{
    public NestedFunctionScopeRegressionTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task NestedFunctionCapturesOuterLetAfterReturn()
    {
        const string script = """
            (() => {
                function outer(seed) {
                    let baseValue = seed;
                    function inner(flag) {
                        if (flag) {
                            let baseValue = seed + 100;
                            return baseValue;
                        }
                        return baseValue;
                    }
                    return inner;
                }
                const fn = outer(10);
                return [fn(false), fn(true), fn(false)].join(",");
            })();
            """;

        await AssertInvariantResult(script, "10,110,10");
    }

    [Fact]
    public async Task ShadowingInsideInnerDoesNotCorruptOuterClosure()
    {
        const string script = """
            (() => {
                function make() {
                    const captured = 2;
                    let outer = 5;
                    return function(flag) {
                        if (flag) {
                            let outer = 99;
                            return outer + captured;
                        }
                        return outer + captured;
                    };
                }
                const fn = make();
                return [fn(false), fn(true), fn(false)].join(",");
            })();
            """;

        await AssertInvariantResult(script, "7,101,7");
    }

    private async Task AssertInvariantResult(string script, string expected)
    {
        await using var engine = CreateEngine();
        var first = await engine.Evaluate(script);
        var second = await engine.Evaluate(script);

        Assert.Equal(expected, ToJsString(first));
        Assert.Equal(expected, ToJsString(second));
    }

    private static string ToJsString(object? value)
    {
        return value switch
        {
            JsValue js => js.AsString(),
            string s => s,
            _ => value?.ToString() ?? string.Empty
        };
    }
}
