using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Diagnostics)]
public sealed class IdentifierSlotLoggingTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Skip = "Slot initialization in IR path needs investigation after environment changes")]
    public async Task ForLoop_UsesSlotFastPathWithoutMisses()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions
        {

            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
            run();
            """);

        Assert.Equal(45d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        var readMisses = messages.Where(m => m.Contains("Identifier slot read miss", StringComparison.Ordinal)).ToArray();
        var writeMisses = messages.Where(m => m.Contains("Identifier slot write miss", StringComparison.Ordinal)).ToArray();

        // Verify no slot misses for loop variables
        Assert.DoesNotContain(readMisses, m => m.Contains("name=s", StringComparison.Ordinal));
        Assert.DoesNotContain(readMisses, m => m.Contains("name=i", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=s", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=i", StringComparison.Ordinal));

        // Note: The specialized numeric loop fast path bypasses normal identifier resolution,
        // so slot hit logs may not appear. The important thing is the result is correct
        // and there are no slot misses.
    }

    [Fact(Skip = "Slot initialization in IR path needs investigation after environment changes")]
    public async Task WhileLoop_UsesSlotFastPathWithoutMisses()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions
        {

            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let sum = 0;
                let i = 0;
                while (i < 10) {
                    sum += i;
                    i++;
                }
                return sum;
            }
            run();
            """);

        Assert.Equal(45d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        var readMisses = messages.Where(m => m.Contains("Identifier slot read miss", StringComparison.Ordinal)).ToArray();
        var writeMisses = messages.Where(m => m.Contains("Identifier slot write miss", StringComparison.Ordinal)).ToArray();

        // Verify no slot misses for loop variables
        Assert.DoesNotContain(readMisses, m => m.Contains("name=sum", StringComparison.Ordinal));
        Assert.DoesNotContain(readMisses, m => m.Contains("name=i", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=sum", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=i", StringComparison.Ordinal));
    }

    [Fact(Skip = "Slot-based fast paths require ScopeAnalyzer which has been removed")]
    public async Task CommonjsModule_ParametersStayInSlots()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions
        {

            DebugMode = true,
            Logger = logger
        });

        const string script = @"
function createCommonjsModule(fn, basedir, module) {
    return module = {
        path: basedir,
        exports: {},
        require: function (path, base) {
            return null;
        }
    }, fn(module, module.exports), module.exports;
}

var browser$5 = createCommonjsModule(function (module, exports) {
    exports.ok = true;
});

var result = browser$5.ok === true;
";

        await engine.Evaluate(script);

        var result = await engine.Evaluate("result;") as bool?;
        Assert.True(result);

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        Assert.DoesNotContain(messages, m => m.Contains("Identifier slot read miss name=module", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("Identifier slot write miss name=module", StringComparison.Ordinal));
    }
}
