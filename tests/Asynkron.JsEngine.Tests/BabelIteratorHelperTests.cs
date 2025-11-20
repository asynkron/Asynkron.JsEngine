using System.Threading.Tasks;
using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class BabelIteratorHelperTests
{
    [Fact(Timeout = 2000)]
    public async Task CreateForOfIteratorHelperLoose_WorksForArrays()
    {
        const string script = """
            let debugTypeOfIt = null;
            let debugIsSymbolIterator = null;

            function _createForOfIteratorHelperLoose(o, allowArrayLike) {
              var it = typeof Symbol !== "undefined" && o[Symbol.iterator] || o["@@iterator"];
              debugTypeOfIt = typeof it;
              debugIsSymbolIterator = (typeof Symbol !== "undefined" && it === Symbol.iterator);
              if (it) return (it = it.call(o)).next.bind(it);
              if (Array.isArray(o) || (it = _unsupportedIterableToArray(o)) ||
                  allowArrayLike && o && typeof o.length === "number") {
                if (it) o = it;
                var i = 0;
                return function () {
                  if (i >= o.length) return { done: true };
                  return { done: false, value: o[i++] };
                };
              }
              throw new TypeError("Invalid attempt to iterate non-iterable instance.");
            }

            function _unsupportedIterableToArray(o) {
              if (!o) return;
              if (typeof o === "string") return Array.from(o);
              return null;
            }

            const iter = _createForOfIteratorHelperLoose([1, 2, 3], false);
            const values = [];
            values.push(iter().value);
            values.push(iter().value);
            values.push(iter().value);
        """;

        await using var engine = new JsEngine();
        await engine.Evaluate(script);

        var typeOfIt = await engine.Evaluate("debugTypeOfIt;");
        var isSymbolIterator = await engine.Evaluate("debugIsSymbolIterator;");

        Assert.Equal("function", typeOfIt);
        Assert.Equal(false, isSymbolIterator);

        var first = await engine.Evaluate("values[0];");
        var second = await engine.Evaluate("values[1];");
        var third = await engine.Evaluate("values[2];");

        Assert.Equal(1.0, first);
        Assert.Equal(2.0, second);
        Assert.Equal(3.0, third);
    }
}
