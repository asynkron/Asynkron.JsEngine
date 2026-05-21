using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class IteratorHelpersDiagTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task Take_ReturnForwardsToUnderlyingIteratorOnce()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            let returnCount = 0;

            class TestIterator extends Iterator {
              next() {
                return { done: false, value: 1 };
              }
              return() {
                ++returnCount;
                return {};
              }
            }

            let iterator = new TestIterator().take(1).take(1).take(1);
            iterator.return();
            iterator.return();
            returnCount;
        """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Take_ReturnValidatesUnderlyingReturnObject()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("""
            class TestIterator extends Iterator {
              next() {
                return { done: false, value: 1 };
              }
              return() {
                return null;
              }
            }

            new TestIterator().take(1).return();
        """));

        Assert.Contains("Iterator.return() must return an object", ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_MapForOf()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function* g() {
                yield 'a';
                yield 'b';
                yield 'c';
            }

            let iter = g();
            let mapped = iter.map(x => x + '!');
            log("mapped type: " + typeof mapped);
            log("mapped is object: " + (typeof mapped === 'object'));

            // Check if Symbol.iterator exists
            log("has Symbol.iterator: " + (mapped[Symbol.iterator] !== undefined));
            let selfIter = mapped[Symbol.iterator]();
            log("selfIter === mapped: " + (selfIter === mapped));

            // Manual iteration
            let r1 = mapped.next();
            log("r1.value: " + r1.value + ", done: " + r1.done);
            let r2 = mapped.next();
            log("r2.value: " + r2.value + ", done: " + r2.done);
            let r3 = mapped.next();
            log("r3.value: " + r3.value + ", done: " + r3.done);
            let r4 = mapped.next();
            log("r4.value: " + r4.value + ", done: " + r4.done);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_MapForOfLoop()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function* g() {
                yield 'a';
                yield 'b';
                yield 'c';
            }

            let count = 0;
            for (let v of g().map(x => x + '!')) {
                log("for-of value: " + v);
                count++;
            }
            log("count: " + count);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_InstanceofIterator()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function* g() { yield 1; }
            let genIter = g();
            log("genIter instanceof Iterator: " + (genIter instanceof Iterator));

            let mapped = genIter.map(x => x);
            log("typeof mapped: " + typeof mapped);
            log("mapped instanceof Iterator: " + (mapped instanceof Iterator));

            // Check prototype chain
            let proto = Object.getPrototypeOf(mapped);
            log("proto: " + proto);
            log("proto === Iterator.prototype: " + (proto === Iterator.prototype));

            let proto2 = Object.getPrototypeOf(proto);
            log("proto2: " + proto2);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_TypeofBooleanReturn()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function* g() { yield 1; yield 2; }

            let everyResult = g().every(x => true);
            log("every result: " + everyResult);
            log("typeof every result: " + typeof everyResult);

            let someResult = g().some(x => true);
            log("some result: " + someResult);
            log("typeof some result: " + typeof someResult);

            let findResult = g().find(x => x === 1);
            log("find result: " + findResult);
            log("typeof find result: " + typeof findResult);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_UnderlyingIteratorClosed()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            let iterator = (function* () {
                for (let i = 0; i < 5; ++i) {
                    yield i;
                }
            })();

            // Test that calling return() closes the generator
            let retResult = iterator.return();
            log("return result: " + JSON.stringify(retResult));

            let nextResult = iterator.next();
            log("after return, next result: " + JSON.stringify(nextResult));
            log("after return, done: " + nextResult.done);
            log("after return, value: " + nextResult.value);

            // Now try with map
            let iter2 = (function* () {
                for (let i = 0; i < 5; ++i) {
                    yield i;
                }
            })();

            iter2.return();
            let mapped = iter2.map(() => 0);
            log("mapped created after return");

            let { value, done } = mapped.next();
            log("mapped.next() after return - value: " + value + ", done: " + done);
        """);
    }

    [Fact(Timeout = 5000)]
    public async Task DiagnosticTest_ClassExtendsIterator()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        await engine.Evaluate("""
            class MyIter extends Iterator {
                constructor() {
                    super();
                    this._count = 0;
                }
                next() {
                    this._count++;
                    if (this._count > 3) return { value: undefined, done: true };
                    return { value: this._count, done: false };
                }
            }

            let iter = new MyIter();
            log("iter.next(): " + JSON.stringify(iter.next()));
            log("iter.next(): " + JSON.stringify(iter.next()));
            log("iter.next(): " + JSON.stringify(iter.next()));
            log("iter.next(): " + JSON.stringify(iter.next()));

            // Check if map works on it
            let iter2 = new MyIter();
            let mapped = iter2.map(x => x * 10);
            log("typeof mapped: " + typeof mapped);
            log("mapped.next(): " + JSON.stringify(mapped.next()));
            log("mapped.next(): " + JSON.stringify(mapped.next()));
            log("mapped.next(): " + JSON.stringify(mapped.next()));
            log("mapped.next(): " + JSON.stringify(mapped.next()));
        """);
    }

    [Fact(Timeout = 10000)]
    public async Task DiagnosticTest_GetterNextProperty()
    {
        await using var engine = CreateEngine();

        engine.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "null" : "null";
            Output.WriteLine($"LOG: {msg}");
            return JsValue.Null;
        });

        // Simplified test -- just check if getter is invoked during GetIteratorDirect
        await engine.Evaluate("""
            let nextGets = 0;

            let obj = {
                get next() {
                    ++nextGets;
                    return function() {
                        return { value: 1, done: true };
                    };
                }
            };

            // Make it inherit from Iterator.prototype
            Object.setPrototypeOf(obj, Iterator.prototype);

            log("nextGets before: " + nextGets);
            let nextFn = obj.next;
            log("nextGets after direct read: " + nextGets);
            log("typeof nextFn: " + typeof nextFn);
        """);
    }
}
