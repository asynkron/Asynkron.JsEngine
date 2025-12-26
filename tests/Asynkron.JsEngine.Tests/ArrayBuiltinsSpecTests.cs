using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class ArrayBuiltinsSpecTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Array_toLocaleString_InvokesElementMethodWithArgs()
    {
        await using var engine = CreateEngine();

        var result = Assert.IsType<JsObject>(await engine.Evaluate("""
            var callCount = 0;
            var lastArgs;
            const element = {
                toLocaleString(...args) {
                    callCount++;
                    lastArgs = args;
                    return "ok";
                }
            };
            const output = [element].toLocaleString("th-u-nu-thai", { minimumFractionDigits: 3 });
            ({ output, callCount, arg0: lastArgs[0], arg1: lastArgs[1] });
        """));

        Assert.Equal("ok", result["output"]);
        Assert.Equal(1d, result["callCount"]);
        Assert.Equal("th-u-nu-thai", result["arg0"]);
        Assert.IsType<JsObject>(result["arg1"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_indexOf_ObservesPropertiesAddedDuringIteration()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var arr = {
              length: 2
            };

            Object.defineProperty(arr, "0", {
              get: function() {
                Object.defineProperty(arr, "1", {
                  get: function() {
                    return 1;
                  },
                  configurable: true
                });
                return 0;
              },
              configurable: true
            });

            Array.prototype.indexOf.call(arr, 1);
        """);

        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_at_SymbolIndexThrowsTypeError()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            "use strict";
            var a = [0, 1, 2, 3];
            var outcome = { kind: "unset" };
            try {
              a.at(Symbol());
              outcome = { kind: "no-throw" };
            } catch (err) {
              outcome = {
                kind: "throw",
                type: typeof err,
                ctor: err && err.constructor && err.constructor.name,
                name: err && err.name,
                message: err && err.message
              };
            }
            outcome;
        """);

        var record = Assert.IsType<JsObject>(result);
        var kind = record["kind"];
        var type = record["type"];
        var ctor = record["ctor"];
        var name = record["name"];
        var message = record["message"];

        Assert.Equal("throw", kind);
        Assert.Equal("object", type);
        Assert.Equal("TypeError", ctor);
        Assert.Equal("TypeError", name);
        Assert.NotNull(message);
    }
}

public class FastPathArrayBuiltinsSpecTests(ITestOutputHelper output) : ArrayBuiltinsSpecTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceArrayBuiltinsSpecTests(ITestOutputHelper output) : ArrayBuiltinsSpecTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
