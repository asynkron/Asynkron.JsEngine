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
    public async Task StrictSameFunctionTailCall_InConditionalBranchDoesNotGrowCallDepth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n, acc) {
                "use strict";
                return n === 0 ? acc : countdown(n - 1, acc + 1);
            }

            countdown(1500, 0);
            """);

        Assert.Equal(1500d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_InFinallyReturnDoesNotGrowCallDepth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let callCount = 0;
            function countdown(n) {
                "use strict";
                if (n === 0) {
                    callCount++;
                    return;
                }

                try {
                } finally {
                    return countdown(n - 1);
                }
            }

            countdown(1500);
            callCount;
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_FinallyReturnOverridesPendingRestart()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n) {
                "use strict";
                try {
                    return n === 0 ? 0 : countdown(n - 1);
                } finally {
                    return 42;
                }
            }

            countdown(2);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyFinallyReturnOverridesPendingLegacyRestart()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n) {
                "use strict";
                try {
                    if (n === 0) {
                        return 0;
                    }

                    for (var x = 0; ;) {
                        return countdown(n - 1);
                    }
                } finally {
                    if (n === 2) {
                        return 42;
                    }
                }
            }

            countdown(2);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyLegacyRestartRefreshesArgumentsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n) {
                "use strict";
                if (n === 0) {
                    return arguments[0];
                }

                for (var x = 0; ;) {
                    return countdown(n - 1);
                }
            }

            countdown(2);
            """);

        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyLegacyRestartClearsFunctionScopedVars()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function countdown(n) {
                "use strict";
                if (n === 2) {
                    var leaked = 99;
                }

                if (n === 0) {
                    return leaked === undefined;
                }

                for (var x = 0; ;) {
                    return countdown(n - 1);
                }
            }

            countdown(2);
            """);

        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyLegacyRestartClearsNewTargetForOrdinaryCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let seen;
            function F(n) {
                "use strict";
                if (n === 0) {
                    seen = new.target === undefined;
                    return {};
                }

                for (;;) {
                    return F(n - 1);
                }
            }

            new F(1);
            seen;
            """);

        Assert.Equal(true, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyLegacyRestartDoesNotReuseCapturedActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let saved;
            function f(n) {
                "use strict";
                var x = n;
                if (n === 1) {
                    saved = () => x;
                }

                if (n === 0) {
                    return saved();
                }

                for (;;) {
                    return f(n - 1);
                }
            }

            f(1);
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_ForBodyLegacyRestartDoesNotReuseActivationCapturedByArgument()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let saved;
            function f(n) {
                "use strict";
                if (n === 0) {
                    return saved();
                }

                for (;;) {
                    return f((saved = () => n, n - 1));
                }
            }

            f(1);
            """);

        Assert.Equal(1d, result);
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

    [Theory(Timeout = 10000)]
    [InlineData("for (var x = 0; ;) { return countdown(n - 1); }")]
    [InlineData("var x; for (x = 0; x < 1; ++x) { return countdown(n - 1); }")]
    public async Task StrictSameFunctionTailCall_InForBodyLegacyFallbackDoesNotGrowCallDepth(string loopBody)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            let callCount = 0;
            function countdown(n) {
                "use strict";
                if (n === 0) {
                    callCount += 1;
                    return callCount;
                }

                {{loopBody}}
            }

            countdown(100000);
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectCalleeExpressionDoesNotGrowCallDepth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let callCount = 0;
            (function f(n) {
                "use strict";
                if (n === 0) {
                    callCount += 1;
                    return;
                }

                function getF() { return f; }
                return getF()(n - 1);
            }(1500));
            callCount;
            """);

        Assert.Equal(1d, result);
    }
}
