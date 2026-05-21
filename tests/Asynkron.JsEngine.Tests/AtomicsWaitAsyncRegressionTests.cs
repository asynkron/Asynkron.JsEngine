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

    [Fact]
    public async Task WaitCoercesInt32ExpectedValueBeforeComparison()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const i32a = new Int32Array(new SharedArrayBuffer(Int32Array.BYTES_PER_ELEMENT));
            i32a[0] = -1;
            Atomics.wait(i32a, 0, 0xffffffff, 0);
            """);

        Assert.Equal("timed-out", result);
    }

    [Fact]
    public async Task WaitCoercesBigIntExpectedValueBeforeComparison()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const i64a = new BigInt64Array(new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT));
            i64a[0] = -1n;
            Atomics.wait(i64a, 0, 0xffffffffffffffffn, 0);
            """);

        Assert.Equal("timed-out", result);
    }

    [Fact]
    public async Task WaitAsyncCoercesBigIntExpectedValueBeforeComparison()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const i64a = new BigInt64Array(new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT));
            i64a[0] = -1n;
            Atomics.waitAsync(i64a, 0, 0xffffffffffffffffn, 0).value;
            """);

        Assert.Equal("timed-out", result);
    }

    [Fact]
    public async Task WaitAsyncBigIntNotEqualRemainsImmediate()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const i64a = new BigInt64Array(new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT));
            i64a[0] = 1n;
            Atomics.waitAsync(i64a, 0, 2n).value;
            """);

        Assert.Equal("not-equal", result);
    }

    [Fact]
    public async Task WaitAsyncInt32GoodIndicesRemainImmediateNotEqual()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const sab = new SharedArrayBuffer(1024);
            const view = new Int32Array(sab, 32, 20);
            const goodIndices = [
              (view) => 0 / -1,
              (view) => '-0',
              (view) => view.length - 1,
              (view) => ({ valueOf: () => 0 }),
              (view) => ({ toString: () => '0', valueOf: false })
            ];

            const results = [];
            for (const indexFactory of goodIndices) {
              const index = indexFactory(view);
              view.fill(0);
              Atomics.store(view, index, 37);
              const waitResult = Atomics.waitAsync(view, index, 0);
              results.push(String(waitResult.async) + ":" + String(waitResult.value));
            }

            results.join(",");
            """);

        Assert.Equal("false:not-equal,false:not-equal,false:not-equal,false:not-equal,false:not-equal", result);
    }

    [Fact]
    public async Task WaitAsyncBigIntGoodIndicesRemainImmediateNotEqual()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const sab = new SharedArrayBuffer(2048);
            const view = new BigInt64Array(sab, 32, 20);
            const goodIndices = [
              (view) => 0 / -1,
              (view) => '-0',
              (view) => view.length - 1,
              (view) => ({ valueOf: () => 0 }),
              (view) => ({ toString: () => '0', valueOf: false })
            ];

            const results = [];
            for (const indexFactory of goodIndices) {
              const index = indexFactory(view);
              view.fill(0n);
              Atomics.store(view, index, 37n);
              const waitResult = Atomics.waitAsync(view, index, 0n);
              results.push(String(waitResult.async) + ":" + String(waitResult.value));
            }

            results.join(",");
            """);

        Assert.Equal("false:not-equal,false:not-equal,false:not-equal,false:not-equal,false:not-equal", result);
    }

    [Fact]
    public async Task WaitAsyncGoodIndicesCanBeAwaitedInAsyncFunction()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var report = "";
            (async () => {
              const sab = new SharedArrayBuffer(1024);
              const view = new Int32Array(sab, 32, 20);
              const goodIndices = [
                (view) => 0 / -1,
                (view) => '-0',
                (view) => view.length - 1,
                (view) => ({ valueOf: () => 0 }),
                (view) => ({ toString: () => '0', valueOf: false })
              ];

              const results = [];
              for (const indexFactory of goodIndices) {
                const index = indexFactory(view);
                view.fill(0);
                Atomics.store(view, index, 37);
                results.push(await Atomics.waitAsync(view, index, 0).value);
              }

              report = results.join(",");
            })();
            """);

        var result = await engine.Evaluate("report;");
        Assert.Equal("not-equal,not-equal,not-equal,not-equal,not-equal", result);
    }

    [Fact]
    public async Task AwaitInForOfExpressionStatementArgumentResumesEachIteration()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var report = "";
            (async () => {
              const values = [1, 2, 3];
              const results = [];
              for (const value of values) {
                results.push(await value);
              }
              report = results.join(",");
            })();
            """);

        var result = await engine.Evaluate("report;");
        Assert.Equal("1,2,3", result);
    }
}
