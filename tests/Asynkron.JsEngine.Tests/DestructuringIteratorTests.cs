using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class DestructuringIteratorTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task ArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        // This mirrors Test262's ary-init-iter-get-err case: the iterator getter throws.
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom");
            };
        var f = ([x]) => {};
        f(iter);
        """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Fact]
    public async Task StrictArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            "use strict";
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom-strict");
            };

            var f = ([x]) => {};
            f(iter);
            """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Theory]
    [InlineData("0, [ x ] = iterable;", 1, 0)]
    [InlineData("0, [...x] = iterable;", 1, 0)]
    public async Task ArrayAssignmentDoesNotInvokeReturnOnIteratorThrow(string assignment, int expectedNext,
        int expectedReturn)
    {
        await using var engine = CreateEngine();
        var logger = new Microsoft.Extensions.Logging.Testing.FakeLogger();
        engine.RealmState.Logger = logger;
        var result = await engine.Evaluate($$"""
            var nextCount = 0;
            var returnCount = 0;
            var iterator = {
              next: function() {
                nextCount += 1;
                throw new Error("boom");
              },
              return: function() {
                returnCount += 1;
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            try {
              {{assignment}}
            } catch (e) {
              // Swallow so we can observe the counters.
            }

            ({ next: nextCount, ret: returnCount });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.True(obj.TryGetProperty("next", out var next), "Missing next property");
        Assert.True(obj.TryGetProperty("ret", out var ret), "Missing ret property");
        Assert.Equal(expectedNext, JsOps.ToNumber(next));
        var iteratorCloseLogs = logger.Collector.Snapshot()
            .Where(r => r.Message.Contains("IteratorClose", StringComparison.Ordinal)).ToArray();
        var logSummary = string.Join(" | ", iteratorCloseLogs.Select(r => r.Message));
        Assert.True(JsOps.ToNumber(ret) == expectedReturn,
            $"returnCount={JsOps.ToNumber(ret)}; logs={logSummary}");
        Assert.True(
            iteratorCloseLogs.Length == expectedReturn,
            logSummary);
    }
}

public class FastPathDestructuringIteratorTests(ITestOutputHelper output) : DestructuringIteratorTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}
