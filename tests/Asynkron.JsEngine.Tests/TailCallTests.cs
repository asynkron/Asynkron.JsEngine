using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class TailCallTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_DoesNotGrowCallDepth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n, acc) {
                "use strict";
                if (n === 0) {
                    return acc;
                }

                return countdown(n - 1, acc + 1);
            }

            countdown(1500, 0);
            """);

        Assert.Equal(1500d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_PopsTryCatchFrameBeforeRestart()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n) {
                "use strict";
                try {
                    if (n === 0) {
                        return 0;
                    }

                    return countdown(n - 1);
                } catch (e) {
                    return -1;
                }
            }

            countdown(1500);
            """);

        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_InTryFinallyRunsFinallyBeforeReturning()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const log = [];
            function countdown(n) {
                "use strict";
                try {
                    if (n === 0) {
                        return "done";
                    }

                    return countdown(n - 1);
                } finally {
                    log.push(n);
                }
            }

            countdown(3) + "|" + log.join(",");
            """);

        Assert.Equal("done|0,1,2,3", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_RebindsMemberReceiverOnRestart()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            const first = { id: "first" };
            const second = { id: "second" };
            function f(n) {
                "use strict";
                try {
                    if (n === 0) {
                        return this.id;
                    }

                    return second.f(n - 1);
                } catch (e) {
                    return "catch";
                }
            }

            first.f = f;
            second.f = f;
            first.f(1);
            """);

        Assert.Equal("second", result);
    }
}
