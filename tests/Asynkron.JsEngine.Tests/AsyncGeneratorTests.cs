using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Collection("GeneratorIrCollection")]
public class AsyncGeneratorTests(ITestOutputHelper output)
{
    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ForAwaitCollectsSequence()
    {
        await using var engine = new JsEngine();

        await engine.Run("""
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

        var result = await engine.Evaluate("log.join(',');");
        Assert.Equal("1,2,3", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_AwaitsBeforeYield()
    {
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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
    public async Task AsyncGenerator_ForLoopWithYield()
    {
        await using var engine = new JsEngine();

        await engine.Run("""
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

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal("loop:0|value:0|loop:1|value:1|loop:2|value:2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_WhileAndDoWhileWithAwaitAndYield()
    {
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "while:0|value:w0|while:1|value:w1|do:0|value:d0|do:1|value:d1",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_SwitchInBody()
    {
        await using var engine = new JsEngine();

        await engine.Run("""
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
        await using var engine = new JsEngine();

        await engine.Run("""
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
        await using var engine = new JsEngine();

        await engine.Run("""
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

            run();
        """);

        var result = await engine.Evaluate("log.join('|');");
        Assert.Equal(
            "try-start|value:body|try-end|finally-start|value:cleanup|finally-end",
            result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncGenerator_ReturnValueVisibleViaNext()
    {
        await using var engine = new JsEngine();

        await engine.Run("""
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
        await using var engine = new JsEngine();

        await engine.Run("""
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
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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
        await using var engine = new JsEngine();

        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Run("""
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
}
