using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class AtomicsWaitAsyncRegressionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task WaitAsyncZeroTimeoutCanBeAwaited()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let report = "";
            const i32a = new Int32Array(new SharedArrayBuffer(Int32Array.BYTES_PER_ELEMENT));
            (async () => {
                report = await Atomics.waitAsync(i32a, 0, 0, false).value;
            })();
            """);

        var result = await engine.Evaluate("report;");
        Assert.Equal("timed-out", result);
    }

    [Fact]
    public async Task WaitAsyncZeroTimeoutCanBeAwaitedRepeatedly()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let report = "";
            const valueOf = { valueOf() { return false; } };
            const toPrimitive = { [Symbol.toPrimitive]() { return false; } };
            const i32a = new Int32Array(new SharedArrayBuffer(Int32Array.BYTES_PER_ELEMENT));
            (async () => {
                report += await Atomics.waitAsync(i32a, 0, 0, false).value;
                report += "," + await Atomics.waitAsync(i32a, 0, 0, valueOf).value;
                report += "," + await Atomics.waitAsync(i32a, 0, 0, toPrimitive).value;
                report += "," + Atomics.waitAsync(i32a, 0, 0, false).value;
                report += "," + Atomics.waitAsync(i32a, 0, 0, valueOf).value;
                report += "," + Atomics.waitAsync(i32a, 0, 0, toPrimitive).value;
            })();
            """);

        var result = await engine.Evaluate("report;");
        Assert.Equal("timed-out,timed-out,timed-out,timed-out,timed-out,timed-out", result);
    }
}
