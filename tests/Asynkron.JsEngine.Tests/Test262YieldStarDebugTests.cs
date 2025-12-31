using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Debug tests to understand why Test262 yield-star-from-try tests fail.
/// </summary>
public class Test262YieldStarDebugTests
{
    private readonly ITestOutputHelper _output;

    public Test262YieldStarDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 10000)]
    public async Task ExactTest262YieldStarFromTry()
    {
        await using var engine = new JsEngine();

        // This is the exact code from yield-star-from-try.js
        var result = await engine.Evaluate(@"
function* values() {
  yield 1;
  yield 1;
}
var dataIterator = values();
var controlIterator = (function*() {
  for (var x of dataIterator) {
    try {
      i++;
      yield * values();
      j++;
    } catch (err) {}
    k++;
  }

  l++;
})();
var i = 0;
var j = 0;
var k = 0;
var l = 0;

controlIterator.next();
[i, j, k, l];
");

        var array = Assert.IsType<JsArray>(result);
        _output.WriteLine($"After first .next(): i={array.GetElement(0)}, j={array.GetElement(1)}, k={array.GetElement(2)}, l={array.GetElement(3)}");
        
        // Expected: i=1, j=0, k=0, l=0
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(0.0, array.GetElement(1).AsDouble());
        Assert.Equal(0.0, array.GetElement(2).AsDouble());
        Assert.Equal(0.0, array.GetElement(3).AsDouble());
    }

    [Fact(Timeout = 10000)]
    public async Task ExactTest262YieldStarFromTry_SecondNext()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
function* values() {
  yield 1;
  yield 1;
}
var dataIterator = values();
var controlIterator = (function*() {
  for (var x of dataIterator) {
    try {
      i++;
      yield * values();
      j++;
    } catch (err) {}
    k++;
  }

  l++;
})();
var i = 0;
var j = 0;
var k = 0;
var l = 0;

controlIterator.next();
controlIterator.next();
[i, j, k, l];
");

        var array = Assert.IsType<JsArray>(result);
        _output.WriteLine($"After second .next(): i={array.GetElement(0)}, j={array.GetElement(1)}, k={array.GetElement(2)}, l={array.GetElement(3)}");
        
        // Expected: i=1, j=0, k=0, l=0 (still in first yield* delegation)
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(0.0, array.GetElement(1).AsDouble());
        Assert.Equal(0.0, array.GetElement(2).AsDouble());
        Assert.Equal(0.0, array.GetElement(3).AsDouble());
    }
}
