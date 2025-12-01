using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var script = @"
function compareArray(a, b) {
  if (b.length !== a.length) {
    return false;
  }

  for (var i = 0; i < a.length; i++) {
    if (!compareArray.isSameValue(b[i], a[i])) {
      return false;
    }
  }
  return true;
}

compareArray.isSameValue = function(a, b) {
  if (a === 0 && b === 0) return 1 / a === 1 / b;
  if (a !== a && b !== b) return true;

  return a === b;
};

var actual = Reflect.ownKeys(new Intl.Locale('en').getWeekInfo());
var expected = ['firstDay','weekend','minimalDays'];
var matches = [];
for (var i = 0; i < actual.length; i++) {
  matches.push(actual[i] === expected[i]);
}
JSON.stringify({
  result: compareArray(actual, expected),
  lengthEqual: actual.length === expected.length,
  matches,
  actual: Array.from(actual),
  expected: expected.slice(),
  actualCtor: actual.constructor && actual.constructor.name,
  expectedCtor: expected.constructor && expected.constructor.name,
  actualProto: Object.getPrototypeOf(actual) === Array.prototype,
  expectedProto: Object.getPrototypeOf(expected) === Array.prototype,
  actualTypes: actual.map(v => typeof v),
  expectedTypes: expected.map(v => typeof v)
});
";
        var result = await engine.Evaluate(script);
        Console.WriteLine(result);
    }
}
