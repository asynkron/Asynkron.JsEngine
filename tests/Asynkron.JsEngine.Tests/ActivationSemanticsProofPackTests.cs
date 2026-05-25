using Asynkron.JsEngine.Tests.Helpers;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.Eval)]
[Category(TestCategories.StrictMode)]
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class ActivationSemanticsProofPackTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string SimpleIrActivationFastPathLog = "simple-ir-activation-fast-path";
    private const string SimpleIrParameterNumberBinaryFastPathLog = "simple-ir-parameter-number-binary-fast-path";
    private const string SimpleIrParameterBinaryFastPathLog = "simple-ir-parameter-binary-fast-path";
    private const string SimpleIrReturnFastPathLog = "simple-ir-return-fast-path";

    [Fact(Timeout = 5000)]
    public async Task SimpleSyncFunction_DoesNotUseCallerBinaryOrSimpleReturnFastPaths()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                var c = a;
                var d = b;
                return c + d;
            }

            add(20, 22);
            """);

        Assert.Equal(42d, result);
        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_NumberArgumentsUseCallerBinaryFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                return a + b;
            }

            add(20, 22);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_NonNumberArgumentsDoNotUseCallerOrReturnFastPaths()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let hits = 0;
            const box = {
                valueOf() {
                    hits++;
                    return 20;
                }
            };

            function add(a, b) {
                return a + b;
            }

            String(add(box, 22)) + ":" + String(hits);
            """);

        Assert.Equal("42:1", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_PropagatesThrownExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function boom() {
                throw new Error("fast path throw");
            }

            function probe() {
                return boom();
            }

            try {
                probe();
            } catch (err) {
                err.message;
            }
            """);

        Assert.Equal("fast path throw", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData("""
        function probe(a) {
            return arguments[0];
        }

        probe(42);
        """, 42d)]
    [InlineData("""
        function probe(a) {
            eval("var local = a + 1;");
            return local;
        }

        probe(41);
        """, 42d)]
    [InlineData("""
        var box = { value: 41 };
        function probe() {
            with (box) {
                return value + 1;
            }
        }

        probe();
        """, 42d)]
    [InlineData("""
        function probe(a = 42) {
            return a;
        }

        probe();
        """, 42d)]
    [InlineData("""
        function probe(...items) {
            return items[0];
        }

        probe(42);
        """, 42d)]
    [InlineData("""
        function probe({ value }) {
            return value;
        }

        probe({ value: 42 });
        """, 42d)]
    [InlineData("""
        function probe(a, a) {
            return a;
        }

        probe(1, 42);
        """, 42d)]
    [InlineData("""
        function makeReader(a) {
            return function read() {
                return a;
            };
        }

        var read = makeReader(42);
        read();
        """, 42d)]
    [InlineData("""
        function* probe(a) {
            yield a;
        }

        probe(42).next().value;
        """, 42d)]
    [InlineData("""
        class Probe {
            constructor(value) {
                this.value = value;
            }
        }

        new Probe(42).value;
        """, 42d)]
    [InlineData("""
        class Base {
            value() {
                return 41;
            }
        }

        class Probe extends Base {
            value() {
                return super.value() + 1;
            }
        }

        new Probe().value();
        """, 42d)]
    [InlineData("""
        class Probe {
            #value = 41;
            value(delta) {
                return this.#value + delta;
            }
        }

        new Probe().value(1);
        """, 42d)]
    public async Task UnsafeActivationShapes_DoNotUseIrActivationFastPath(string script, double expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(script);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncActivation_DoesNotUseIrActivationFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            async function probe(a) {
                return a + 1;
            }

            probe(41);
            "called";
            """);

        Assert.Equal("called", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SloppySimpleParameters_MapArgumentsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                a = a + 1;
                return arguments[0] + ":" + a;
            }

            probe(41);
            """);

        Assert.Equal("42:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSimpleParameters_DoNotMapArgumentsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                "use strict";
                a = a + 1;
                return arguments[0] + ":" + a;
            }

            probe(41);
            """);

        Assert.Equal("41:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DefaultParameterDisablesArgumentsMapping()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a = 1) {
                a = a + 1;
                return arguments[0] + ":" + a + ":" + arguments.length;
            }

            probe(41);
            """);

        Assert.Equal("41:42:1", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RestAndDestructuredParameters_BindFromActivationWithoutArgumentsAliasing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe({ x }, y = x + 1, ...rest) {
                rest[0] = 99;
                return [x, y, arguments[0].x, rest[0], arguments.length].join(":");
            }

            probe({ x: 41 }, undefined, 7);
            """);

        Assert.Equal("41:42:41:99:3", result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedClosures_KeepCapturedActivationStateAcrossCalls()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeCounter(start) {
                let count = start;
                return function () {
                    count = count + 1;
                    return count;
                };
            }

            const a = makeCounter(0);
            const b = makeCounter(10);

            [a(), a(), b(), a(), b()].join(",");
            """);

        Assert.Equal("1,2,11,3,12", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalInParameterDefault_SeesActivationBindings()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a, b = eval("a + 1")) {
                return b;
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalVarDeclaration_WritesIntoFunctionActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                eval("var local = a + 1;");
                return local;
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task WithAndStrictMode_StaySeparated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = { value: 41 };

            function sloppy() {
                with (box) {
                    return value + 1;
                }
            }

            var strictCaught = false;
            try {
                eval("'use strict'; with (box) { value; }");
            } catch (err) {
                strictCaught = err instanceof SyntaxError;
            }

            [sloppy(), strictCaught].join(":");
            """);

        Assert.Equal("42:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictAndSloppyCalls_KeepDistinctThisBindingRules()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sloppyThis() {
                return this === globalThis;
            }

            function strictThis() {
                "use strict";
                return this === undefined;
            }

            [sloppyThis(), strictThis()].join(":");
            """);

        Assert.Equal("true:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorActivation_PreservesCapturedParameterAcrossSuspension()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* probe(a) {
                const read = () => a;
                yield read();
                a = a + 1;
                yield read();
            }

            const iterator = probe(41);
            [iterator.next().value, iterator.next().value].join("|");
            """);

        Assert.Equal("41|42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionActivation_PreservesCapturedParameterAcrossAwait()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var observed = "";

            async function probe(a) {
                const read = () => a;
                const first = read();
                a = await Promise.resolve(a + 1);
                const second = read();
                return first + ":" + second;
            }

            probe(41).then(function(value) {
                observed = value;
            });
            """);

        var result = await engine.Evaluate("observed;");
        Assert.Equal("41:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorActivation_PreservesCapturedParameterAcrossAwaitAndYield()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var observed = [];

            async function* probe(a) {
                const read = () => a;
                observed.push("start:" + read());
                a = await Promise.resolve(a + 1);
                yield read();
            }

            async function run() {
                for await (const value of probe(41)) {
                    observed.push("yield:" + value);
                }
            }

            run();
            """);

        var result = await engine.Evaluate("observed.join('|');");
        Assert.Equal("start:41|yield:42", result);
    }
}
