using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
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

        await AssertInvariantResult(script, "10,110,10", routedFunc: "inner");
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

        await AssertInvariantResult(script, "7,101,7", routedFunc: "<anonymous>");
    }

    private async Task AssertInvariantResult(string script, string expected, string? routedFunc = null)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(script);
        var outerDecl = AstTestHelpers.FindFirst<FunctionDeclaration>(pipeline.Analyzed);
        var outerFunc = outerDecl.Function;
        var innerDecl = AstTestHelpers.Walk(outerFunc.Body, includeSelf: true)
            .OfType<FunctionDeclaration>()
            .FirstOrDefault(f => !ReferenceEquals(f.Function, outerFunc));
        var innerFunc = innerDecl?.Function ??
                        AstTestHelpers.FindFirst<FunctionExpression>(outerFunc.Body,
                            f => !ReferenceEquals(f, outerFunc));
        var ifStatement = AstTestHelpers.FindFirst<IfStatement>(innerFunc.Body);
        var thenBlock = Assert.IsType<BlockStatement>(ifStatement.Then);
        var hoistPlan = ((IAstCacheable<HoistPlan>)thenBlock).GetOrCreateCache();

        Output.WriteLine(
            $"Inner if-block ScopeId={thenBlock.ScopeId} SlotCount={thenBlock.SlotCount} NeedsEnv={hoistPlan.NeedsEnvironment}");
        Assert.True(hoistPlan.NeedsEnvironment, "Inner if-block must allocate lexical environment for shadowed let");

        await using var engine = CreateEngine();
        var first = await engine.Evaluate(script);
        var second = await engine.Evaluate(script);

        Assert.Equal(expected, ToJsString(first));
        Assert.Equal(expected, ToJsString(second));

        if (routedFunc is not null)
        {
            // Option B (Stage 5): the colliding inner closure now routes through the production
            // unified-bytecode VM (no longer forced onto the IR runner by the retired collision guard)
            // while still computing the correct result above.
            const string prodLog = "unified-bytecode-production-fast-path func=";
            Assert.Contains(
                CurrentLogger!.Collector.Snapshot(),
                x => x.Message.Contains(prodLog + routedFunc, StringComparison.Ordinal));
        }
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
