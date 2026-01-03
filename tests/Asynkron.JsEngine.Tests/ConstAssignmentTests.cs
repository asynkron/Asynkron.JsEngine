using System.Collections.Generic;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class ConstAssignmentTests(ITestOutputHelper output) : InternalTestBase(output)
{
    public static IEnumerable<object[]> ConstLoopData => new[]
    {
        new object[] { false, "for (const x in [1, 2, 3]) { x++; }" },
        new object[] { true, "for (const x in [1, 2, 3]) { x++; }" },
        new object[] { false, "for (const x of [1, 2, 3]) { x++; }" },
        new object[] { true, "for (const x of [1, 2, 3]) { x++; }" },
        new object[] { false, "for (const i = 0; i < 1; i++) { /* update expression */ }" },
        new object[] { true, "for (const i = 0; i < 1; i++) { /* update expression */ }" }
    };

    [Theory]
    [MemberData(nameof(ConstLoopData))]
    public async Task ConstAssignments_AreCaughtAsTypeErrors(bool strict, string loop)
    {
        await using var engine = CreateEngine();
        var result = await ExecuteLoop(engine, strict, loop);
        Assert.Equal("TypeError", result);
    }

    [Theory]
    [MemberData(nameof(ConstLoopData))]
    public async Task ConstAssignments_AreCaughtAsTypeErrors_NoDebug(bool strict, string loop)
    {
        await using var engine = new JsEngine(new JsEngineOptions());
        var result = await ExecuteLoop(engine, strict, loop);
        Assert.Equal("TypeError", result);
    }

    private static async Task<string?> ExecuteLoop(JsEngine engine, bool strict, string loop)
    {
        var result = await engine.Evaluate($$"""
            (function () {
                {{(strict ? "\"use strict\";" : string.Empty)}}
                try {
                    {{loop}}
                } catch (err) {
                    return err.name;
                }
                return "no-throw";
            })();
            """);

        return result as string;
    }
}
