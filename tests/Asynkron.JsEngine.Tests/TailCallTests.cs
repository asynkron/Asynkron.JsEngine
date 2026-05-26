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

    [Fact(Timeout = 15000)]
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
            }(100000));
            callCount;
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectCalleeExpressionDoesNotReuseLeakedCapturedActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let saved;
            (function f(n) {
                "use strict";
                if (n === 0) {
                    return saved();
                }

                return (saved = () => n, f)(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectCalleeExpressionDoesNotReuseLeakedFunctionDeclarationClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let saved;
            (function f(n) {
                "use strict";
                function getF() { return f; }
                function getN() { return n; }
                if (n === 0) {
                    return saved();
                }

                return (saved = getN, getF())(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseLeakedFunctionDeclarationClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let saved;
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() { saved = getN; return f; }
                if (n === 0) {
                    return saved();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaObjectProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = {};
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() { holder.saved = getN; return f; }
                if (n === 0) {
                    return holder.saved();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaNestedObjectPropertyAfterInitialCleanScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = { inner: {} };
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        holder.inner.saved = getN;
                    }

                    return f;
                }

                if (n === 0) {
                    return holder.inner.saved();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaNestedArrayElementAfterInitialCleanScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = { arr: [0] };
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        holder.arr[0] = getN;
                    }

                    return f;
                }

                if (n === 0) {
                    return holder.arr[0]();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaWeakMapValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let wm = new WeakMap();
            let key = {};
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        wm.set(key, getN);
                    }

                    return f;
                }

                if (n === 0) {
                    return wm.get(key)();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaWeakMapValueAfterInitialCleanHolderScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = { wm: new WeakMap(), key: {} };
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        holder.wm.set(holder.key, getN);
                    }

                    return f;
                }

                if (n === 0) {
                    return holder.wm.get(holder.key)();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaWeakMapCustomPropertyAfterInitialCleanScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let wm = new WeakMap();
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        wm.saved = getN;
                    }

                    return f;
                }

                if (n === 0) {
                    return wm.saved();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaMapValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let map = new Map();
            let key = {};
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        map.set(key, getN);
                    }

                    return f;
                }

                if (n === 0) {
                    return map.get(key)();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaMapValueAfterInitialCleanHolderScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = { map: new Map(), key: {} };
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        holder.map.set(holder.key, getN);
                    }

                    return f;
                }

                if (n === 0) {
                    return holder.map.get(holder.key)();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaMapCustomPropertyAfterInitialCleanScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let map = new Map();
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        map.saved = getN;
                    }

                    return f;
                }

                if (n === 0) {
                    return map.saved();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaSetValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let set = new Set();
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        set.add(getN);
                    }

                    return f;
                }

                if (n === 0) {
                    let leaked;
                    set.forEach(v => leaked = v);
                    return leaked();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaSetCustomPropertyAfterInitialCleanScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let set = new Set();
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        set.saved = getN;
                    }

                    return f;
                }

                if (n === 0) {
                    return set.saved();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaSetValueAfterInitialCleanHolderScan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = { set: new Set() };
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() {
                    if (n === 1) {
                        holder.set.add(getN);
                    }

                    return f;
                }

                if (n === 0) {
                    let leaked;
                    holder.set.forEach(v => leaked = v);
                    return leaked();
                }

                return getF()(n - 1);
            }(2));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaFunctionObjectProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let holder = function holder() {};
            (function f(n) {
                "use strict";
                function getN() { return n; }
                function getF() { holder.saved = getN; return f; }
                if (n === 0) {
                    return holder.saved();
                }

                return getF()(n - 1);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaCurrentActivationArgumentObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function f(n, localHolder) {
                "use strict";
                if (!localHolder) localHolder = {};
                function getN() { return n; }
                function getF() { localHolder.saved = getN; return f; }
                if (n === 0) return localHolder.saved();
                return getF()(n - 1, localHolder);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallDoesNotReuseClosureLeakedViaArgumentPrototypeChain()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function f(n, localHolder) {
                "use strict";
                if (!localHolder) localHolder = {};
                function getN() { return n; }
                function getF() {
                    Object.setPrototypeOf(localHolder, { saved: getN });
                    return f;
                }

                if (n === 0) {
                    return localHolder.saved();
                }

                return getF()(n - 1, localHolder);
            }(1));
            """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSameFunctionTailCall_IndirectHelperCallIgnoresUnrelatedProxySlotDuringEscapeCheck()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let p = new Proxy({}, { ownKeys() { throw new Error("boom"); } });
            (function f(n) {
                "use strict";
                function getF() { return f; }
                return n ? getF()(n - 1) : 1;
            }(1));
            """);

        Assert.Equal(1d, result);
    }
}
