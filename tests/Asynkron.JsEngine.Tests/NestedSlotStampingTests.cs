using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NestedSlotStampingTests
{
    [Fact]
    public async Task NestedClosure_ProducesExpectedSequence()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            function make() {
                let x = 0;
                return function () { return x++; };
            }
            const f = make();
            [f(), f(), f()];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(0.0, array.GetElement(0).AsDouble());
        Assert.Equal(1.0, array.GetElement(1).AsDouble());
        Assert.Equal(2.0, array.GetElement(2).AsDouble());
    }

    [Fact]
    public async Task MultipleNestedClosures_DoNotCollideAcrossInstancesOrRuns()
    {
        await using var engine = new JsEngine();

        const string script = """
            (() => {
                function outer(start) {
                    let x = start;
                    function inner() { return x++; }
                    return inner;
                }
                const f = outer(1);
                const g = outer(10);
                return [f(), f(), g(), g()];
            })();
            """;

        var result = await engine.Evaluate(script);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(2.0, array.GetElement(1).AsDouble());
        Assert.Equal(10.0, array.GetElement(2).AsDouble());
        Assert.Equal(11.0, array.GetElement(3).AsDouble());

        // Run again to ensure pooled environments don’t leak state across executions.
        var second = await engine.Evaluate(script);
        var array2 = Assert.IsType<JsArray>(second);
        Assert.Equal(1.0, array2.GetElement(0).AsDouble());
        Assert.Equal(2.0, array2.GetElement(1).AsDouble());
        Assert.Equal(10.0, array2.GetElement(2).AsDouble());
        Assert.Equal(11.0, array2.GetElement(3).AsDouble());
    }

    [Fact]
    public async Task MixedContexts_GlobalStrictAndScript_DoNotLeak()
    {
        await using var engine = new JsEngine();

        const string script = """
            (function () {
                function make(label) {
                    let x = 0;
                    return function () { return label + (x++); };
                }

                const g = make("G"); // global sloppy
                const s = (function () {
                    'use strict';
                    const h = make("S");
                    return h;
                })();

                const arr = [];
                arr.push(g());
                arr.push(s());
                arr.push(g());
                arr.push(s());
                return arr;
            })();
            """;

        var result = await engine.Evaluate(script);
        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("G0", array.GetElement(0).AsString());
        Assert.Equal("S0", array.GetElement(1).AsString());
        Assert.Equal("G1", array.GetElement(2).AsString());
        Assert.Equal("S1", array.GetElement(3).AsString());

        // Re-run in a separate script execution to ensure no pooled environment leakage
        var second = await engine.Evaluate(script);
        var array2 = Assert.IsType<JsArray>(second);
        Assert.Equal("G0", array2.GetElement(0).AsString());
        Assert.Equal("S0", array2.GetElement(1).AsString());
        Assert.Equal("G1", array2.GetElement(2).AsString());
        Assert.Equal("S1", array2.GetElement(3).AsString());
    }
}
