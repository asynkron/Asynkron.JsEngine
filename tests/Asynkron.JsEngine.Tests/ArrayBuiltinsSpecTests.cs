using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class ArrayBuiltinsSpecTests
{
    [Fact(Timeout = 2000)]
    public async Task Array_toLocaleString_InvokesElementMethodWithArgs()
    {
        await using var engine = new JsEngine();

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
        await using var engine = new JsEngine();

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
}
