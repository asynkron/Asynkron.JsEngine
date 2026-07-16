using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
[Collection("GeneratorIrCollection")]
public sealed class AsyncGeneratorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task AsyncGeneratorFunctionConstructor_SingleArgumentBodyYields()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let observed = [];
            var AsyncGeneratorFunction = Object.getPrototypeOf(async function* () {}).constructor;

            var g = AsyncGeneratorFunction('yield 1;');
            var iter = g();

            iter.next().then(function(result) {
              observed.push(result.value + ':' + result.done);
            });
            iter.next().then(function(result) {
              observed.push(String(result.value) + ':' + result.done);
            });
        """);

        var result = await engine.Evaluate("observed.join('|');");
        Assert.Equal("1:false|undefined:true", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ForAwaitCollectsSequence()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let output = "";
            let log = [];

            async function* numbers() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function run() {
                for await (const value of numbers()) {
                    log.push(value);
                }
            }

            run();
        """);

        var result = await engine.EvaluateAndAwait("log.join(',');");
        Assert.Equal("1,2,3", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_AwaitsBeforeYield()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let output = "";
            let log = [];

            async function* gen() {
                const first = await __delay(1, "a");
                log.push("before-yield:" + first);
                yield first;
                const second = await __delay(1, "b");
                log.push("after-first-yield:" + second);
                yield second;
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("yielded:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal("before-yield:a|yielded:a|after-first-yield:b|yielded:b", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_YieldStarAwaitedArray()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let output = "";
            let log = [];

            async function* relay(values) {
                yield* await values;
            }

            async function run() {
                for await (const value of relay([1, 2])) {
                    log.push(value);
                }
            }

            run();
        """);

        var result = await engine.EvaluateAndAwait("log.join(',');");
        Assert.Equal("1,2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ClassPrivateStaticMethod_YieldStarAwaitedArray()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let output = "";
            let log = [];

            class C {
                static async * #relay(values) {
                    yield* await values;
                }

                static get relay() {
                    return this.#relay;
                }
            }

            async function run() {
                for await (const value of C.relay([1, 2])) {
                    log.push(value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join(',');");
        Assert.Equal("1,2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ClassStaticMethod_YieldStarAbruptAsyncIteratorLookupRejectsNext()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let output = "";
            let log = [];

            class C {
                static async * relay(iterable) {
                    yield* iterable;
                }
            }

            const abrupt = {
                get [Symbol.asyncIterator]() {
                    throw "boom";
                }
            };

            async function run() {
                const iterator = C.relay(abrupt);
                try {
                    await iterator.next();
                    log.push("not-thrown");
                } catch (error) {
                    log.push("caught:" + error);
                }

                const afterThrow = await iterator.next();
                log.push("after:" + afterThrow.value + ":" + afterThrow.done);
            }

            run();
        """);

        var result = await engine.EvaluateAndAwait("log.join('|');");
        Assert.Equal("caught:boom|after:undefined:true", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ClassComputedMethodNameFailsExplicitlyAfterUnifiedDecline()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        var result = await engine.EvaluateAndAwait("""
            let output = "";
            let log = [];

            async function* gen() {
                class Box {
                    [await __delay(1, "value")]() {
                        return "ok";
                    }
                }

                yield new Box().value();
            }

            async function run() {
                for await (const value of gen()) {
                    log.push(value);
                }
            }

            run().then(function() {
                output = log.join(",");
            }, function(e) {
                output = "error:" + String(e);
            });

            output;
        """);

        var message = result?.ToString();
        Assert.NotNull(message);
        Assert.StartsWith("error:Async-generator body 'gen' is not eligible for unified bytecode execution:", message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ClassComputedFieldNameFailsExplicitlyAfterUnifiedDecline()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        var result = await engine.EvaluateAndAwait("""
            let output = "";
            let log = [];

            async function* gen() {
                class Box {
                    [(await __delay(1, "va")) + "lue"];
                }

                yield Object.prototype.hasOwnProperty.call(new Box(), "value");
            }

            async function run() {
                for await (const value of gen()) {
                    log.push(String(value));
                }
            }

            run().then(function() {
                output = log.join(",");
            }, function(e) {
                output = "error:" + String(e);
            });

            output;
        """);

        var message = result?.ToString();
        Assert.NotNull(message);
        Assert.StartsWith("error:Async-generator body 'gen' is not eligible for unified bytecode execution:", message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ForLoopWithYieldRoutesAndCompletes()
    {
        await using var engine = CreateEngine();

        var result = await engine.EvaluateAndAwait("""
            let output = "";
            let log = [];

            async function* counter(limit) {
                for (let i = 0; i < limit; i = i + 1) {
                    log.push("loop:" + i);
                    yield i;
                }
            }

            async function run() {
                for await (const value of counter(3)) {
                    log.push("value:" + value);
                }
            }

            run().then(function() {
                output = log.join("|");
            }, function(e) {
                output = "error:" + String(e);
            });

            output;
        """);

        Assert.Equal("loop:0|value:0|loop:1|value:1|loop:2|value:2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_WhileAndDoWhileWithAwaitAndYieldFailsExplicitlyAfterUnifiedDecline()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        var result = await engine.EvaluateAndAwait("""
            let output = "";
            let log = [];

            async function* gen() {
                let i = 0;
                while (i < 2) {
                    await __delay(1);
                    log.push("while:" + i);
                    yield "w" + i;
                    i = i + 1;
                }

                let j = 0;
                do {
                    await __delay(1);
                    log.push("do:" + j);
                    yield "d" + j;
                    j = j + 1;
                } while (j < 2);
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("value:" + value);
                }
            }

            run().then(function() {
                output = log.join("|");
            }, function(e) {
                output = "error:" + String(e);
            });

            output;
        """);

        var message = result?.ToString();
        Assert.NotNull(message);
        Assert.StartsWith("error:Async-generator body 'gen' is not eligible for unified bytecode execution:", message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_SwitchInBody()
    {
        await using var engine = CreateEngine(() => new JsEngineOptions()
        {
            Logger = new TestLogger(output, minLogLevel: LogLevel.Debug)
        });

        await engine.Evaluate("""
            let output = "";
            let log = [];

            async function* classify(xs) {
                for (const x of xs) {
                    switch (x) {
                        case 0:
                            log.push("zero");
                            yield "zero";
                            break;
                        case 1:
                        case 2:
                            log.push("small");
                            yield "small";
                            break;
                        default:
                            log.push("other");
                            yield "other";
                            break;
                    }
                }
            }

            async function run() {
                for await (const value of classify([0, 1, 2, 3])) {
                    log.push("value:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "zero|value:zero|small|value:small|small|value:small|other|value:other",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_TryCatchWithThrow()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let output = "";
            let log = [];

            async function* gen() {
                try {
                    log.push("before-yield");
                    yield "first";
                    log.push("after-yield");
                } catch (e) {
                    log.push("caught:" + e);
                    yield "handled";
                }
            }

            async function run() {
                const it = gen();
                const r1 = await it.next();
                log.push("r1:" + r1.value + ":" + r1.done);
                const r2 = await it.throw("boom");
                log.push("r2:" + r2.value + ":" + r2.done);
                const r3 = await it.next();
                log.push("r3:" + r3.value + ":" + r3.done);
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "before-yield|r1:first:false|caught:boom|r2:handled:false|r3:undefined:true",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_TryFinallyWithYieldInFinally()
    {
        await using var engine = CreateEngine();

        var result = await engine.EvaluateAndAwait("""
            let output = "";
            let log = [];

            async function* gen() {
                try {
                    log.push("try-start");
                    yield "body";
                    log.push("try-end");
                } finally {
                    log.push("finally-start");
                    yield "cleanup";
                    log.push("finally-end");
                }
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("value:" + value);
                }
            }

            run().then(function() {
                output = log.join("|");
            }, function(e) {
                output = "unsupported:" + String(e);
            });

            output;
            """);

        Assert.Equal(
            "try-start|value:body|try-end|finally-start|value:cleanup|finally-end",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ReturnValueVisibleViaNext()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                log.push("start");
                yield 1;
                log.push("before-return");
                return 2;
            }

            async function run() {
                const it = gen();
                const r1 = await it.next();
                log.push("r1:" + r1.value + ":" + r1.done);
                const r2 = await it.next();
                log.push("r2:" + r2.value + ":" + r2.done);
                const r3 = await it.next();
                log.push("r3:" + r3.value + ":" + r3.done);
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "start|r1:1:false|before-return|r2:2:true|r3:undefined:true",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ForAwaitOverAsyncGenerator()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                yield "a";
                yield "b";
                yield "c";
            }

            async function run() {
                for await (const value of gen()) {
                    log.push(value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join(',');");
        Assert.Equal("a,b,c", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_DelayedAwaitInsideBody_UsesHostDelay()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                log.push("start");
                const first = await __delay(10, "x");
                log.push("after-first-await:" + first);
                yield first;
                const second = await __delay(10, "y");
                log.push("after-second-await:" + second);
                yield second;
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("yielded:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "start|after-first-await:x|yielded:x|after-second-await:y|yielded:y",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_DelayedAwaitInLoopBody()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                let i = 0;
                while (i < 3) {
                    const v = await __delay(10, "v" + i);
                    log.push("loop:" + i);
                    yield v;
                    i = i + 1;
                }
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("yielded:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "loop:0|yielded:v0|loop:1|yielded:v1|loop:2|yielded:v2",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_DelayedAwaitNestedGenerators()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function* inner() {
                const a = await __delay(10, "a");
                log.push("inner:" + a);
                yield a;
            }

            async function* outer() {
                log.push("outer:start");
                for await (const value of inner()) {
                    log.push("outer:value:" + value);
                    yield value;
                }
                const b = await __delay(10, "b");
                log.push("outer:after:" + b);
                yield b;
            }

            async function run() {
                for await (const value of outer()) {
                    log.push("yielded:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "outer:start|inner:a|outer:value:a|yielded:a|outer:after:b|yielded:b",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_DelayedAwaitBetweenYields()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                log.push("before-first-yield");
                yield "first";
                const x = await __delay(10, "x");
                log.push("after-await:" + x);
                yield "second";
            }

            async function run() {
                for await (const value of gen()) {
                    log.push("yielded:" + value);
                }
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "before-first-yield|yielded:first|after-await:x|yielded:second",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_DirectNextAcrossPendingAwaits_PreserveOrdering()
    {
        await using var engine = CreateEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                log.push("gen:start");
                const first = await __delay(10, "first");
                log.push("gen:after-first-await:" + first);
                yield first;
                log.push("gen:after-yield");
                const second = await __delay(10, "second");
                log.push("gen:after-second-await:" + second);
                yield second;
            }

            async function run() {
                const it = gen();
                const r1 = await it.next();
                log.push("next1:" + r1.value + ":" + r1.done);
                const r2 = await it.next();
                log.push("next2:" + r2.value + ":" + r2.done);
                const r3 = await it.next();
                log.push("next3:" + r3.value + ":" + r3.done);
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "gen:start|gen:after-first-await:first|next1:first:false|gen:after-yield|gen:after-second-await:second|next2:second:false|next3:undefined:true",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_NextPromiseLateThen_SeesStableIteratorResult()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                yield "v1";
            }

            async function run() {
                const it = gen();
                const late = it.next();
                await Promise.resolve();
                late.then(function(result) {
                    log.push(result.value + ":" + result.done);
                }, function(error) {
                    log.push("rejected:" + String(error));
                });
                await Promise.resolve();
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal("v1:false", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_NextPromiseLateThen_AfterMultipleMicrotasks_SeesStableIteratorResult()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let log = [];

            async function* gen() {
                yield "v1";
            }

            async function run() {
                const it = gen();
                const late = it.next();
                await Promise.resolve();
                await Promise.resolve();
                await Promise.resolve();
                await Promise.resolve();
                late.then(function(result) {
                    log.push(result.value + ":" + result.done);
                }, function(error) {
                    log.push("rejected:" + String(error));
                });
                await Promise.resolve();
            }

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal("v1:false", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_NextPromiseLateThen_PreservesIteratorResultIdentity()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let sameIdentity = false;

            async function* gen() {
                yield "v1";
            }

            async function run() {
                const it = gen();
                const settled = await it.next();
                const late = Promise.resolve(settled);
                late.then(function(result) {
                    sameIdentity = result === settled;
                });
                await Promise.resolve();
            }

            run();
        """);

        var same = await engine.Evaluate("sameIdentity;");
        Assert.True(same is bool b && b);
    }
}
