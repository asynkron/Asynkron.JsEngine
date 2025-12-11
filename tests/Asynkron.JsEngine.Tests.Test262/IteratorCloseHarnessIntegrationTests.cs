using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests.Test262;

public class IteratorCloseHarnessIntegrationTests
{
    [Test]
    public async Task ForOfDestructuring_ReturnNull_CaughtByHarness_Sloppy()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);

        var result = await engine.Evaluate(BuildForOfScript(useStrict: false));
        var obj = result as JsObject;
        Assert.That(obj, Is.Not.Null);
        TestContext.WriteLine($"sloppy result: caught={obj!["caught"]}, errName={obj["errName"]}, next={obj["nextCount"]}, return={obj["returnCount"]}, unreachable={obj["unreachable"]}");
        Assert.That(obj!["caught"], Is.EqualTo(true));
        Assert.That(obj["errName"], Is.EqualTo("TypeError"));
        Assert.That(obj["returnCount"], Is.EqualTo(1d));
        Assert.That(obj["unreachable"], Is.EqualTo(0d));
    }

    [Test]
    public async Task ForOfDestructuring_ReturnNull_CaughtByHarness_Strict()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);

        var result = await engine.Evaluate(BuildForOfScript(useStrict: true));
        var obj = result as JsObject;
        Assert.That(obj, Is.Not.Null);
        TestContext.WriteLine($"strict result: caught={obj!["caught"]}, errName={obj["errName"]}, next={obj["nextCount"]}, return={obj["returnCount"]}, unreachable={obj["unreachable"]}");
        Assert.That(obj!["caught"], Is.EqualTo(true));
        Assert.That(obj["errName"], Is.EqualTo("TypeError"));
        Assert.That(obj["returnCount"], Is.EqualTo(1d));
        Assert.That(obj["unreachable"], Is.EqualTo(0d));
    }

    private static string BuildForOfScript(bool useStrict)
    {
        var strictPrefix = useStrict ? "\"use strict\";\n" : string.Empty;
        return $@"
{strictPrefix}
var nextCount = 0;
var returnCount = 0;
var unreachable = 0;
var iterator = {{
  next: function() {{
    nextCount += 1;
    return {{ done: false, value: undefined }};
  }},
  return: function() {{
    returnCount += 1;
    return null;
  }}
}};
var iterable = {{ }};
iterable[Symbol.iterator] = function() {{
  return iterator;
}};

function* g() {{
  for ([ {{}} = yield ] of [iterable]) {{
    unreachable += 1;
  }}
}}

var caught = false;
var errName = null;
var resultRecord;
try {{
  var iter = g();
  iter.next();
  assert.throws(TypeError, function() {{
    iter.return();
  }});
  caught = true;
  errName = 'TypeError';
  resultRecord = iter;
}} catch (err) {{
  caught = err instanceof TypeError;
  errName = err && err.constructor ? err.constructor.name : null;
}}

({{ caught, errName, nextCount, returnCount, unreachable }});
";
    }
}
