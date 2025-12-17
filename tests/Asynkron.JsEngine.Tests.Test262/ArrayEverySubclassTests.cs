using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests.Test262;

public class ArrayEverySubclassTests
{
    [Test]
    public async Task SubclassedArrayPrototypeExposesEvery()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);

        var script = """
                     "use strict";
                     function foo() {}
                     foo.prototype = new Array(1, 2, 3);
                     var f = new foo();
                     f.length = [];
                     """
                     ;

        await engine.Evaluate(script);

        var typeofEvery = await engine.Evaluate("typeof f.every;");
        var callResult = await engine.Evaluate("f.every(function () { return true; });");

        Assert.That(typeofEvery, Is.EqualTo("function"));
        Assert.That(callResult, Is.EqualTo(true));
    }

    [Test]
    public async Task SubclassedArrayPrototypeExposesEvery_NonStrict()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);

        var script = """
                     // Intentionally non-strict to mirror legacy Test262 coverage
                     function foo() {}
                     foo.prototype = new Array(1, 2, 3);
                     var f = new foo();
                     f.length = 2;
                     """;

        await engine.Evaluate(script);

        var typeofEvery = await engine.Evaluate("typeof f.every;");
        var callResult = await engine.Evaluate("f.every(function () { return true; });");

        Assert.That(typeofEvery, Is.EqualTo("function"));
        Assert.That(callResult, Is.EqualTo(true));
    }

    [Test]
    public async Task TypedArrayEveryUsesGlobalThisByDefault()
    {
        await using var engine = new JsEngine();

        var constructors = new[] { "Uint8Array", "BigInt64Array" };

        foreach (var ctor in constructors)
        {
            var script = $@"
var C = {ctor};
var record;
if (typeof C === 'undefined') {{
  record = {{ capturedType: 'unavailable', isGlobal: false }};
}} else {{
  var captured;
  new C(3).every(function() {{
    captured = this;
    return true;
  }});
  record = {{ capturedType: typeof captured, isGlobal: captured === globalThis }};
}}
record;";

            var result = await engine.Evaluate(script);
            var record = result as IDictionary<string, object?>;
            Assert.That(record, Is.Not.Null, $"Expected an object result for {ctor}");

            Assert.That(record!.TryGetValue("capturedType", out var typeVal));
            if (Equals(typeVal, "unavailable"))
            {
                continue;
            }

            Assert.That(typeVal, Is.EqualTo("object"), $"captured type for {ctor}");
            Assert.That(record.TryGetValue("isGlobal", out var isGlobalVal));
            Assert.That(isGlobalVal, Is.EqualTo(true), $"global this for {ctor}");
        }
    }

    [Test]
    public async Task TypedArrayEveryIgnoresDetachMidIteration()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("detach", args =>
        {
            if (args.Count > 0 && args[0].TryGetObject<JsArrayBuffer>(out var buffer))
            {
                buffer.Detach();
            }

            return JsValue.Undefined;
        });

        var script = """
                     var loops = 0;
                     var sample = new Uint8Array(2);
                     sample.every(function() {
                       if (loops === 0) {
                         detach(sample.buffer);
                       }
                       loops++;
                       return true;
                     });
                     loops;
                     """;

        var result = await engine.Evaluate(script);
        Assert.That(result, Is.EqualTo(2d));
    }

    [Test]
    public async Task TypedArrayEveryStrictCallbackGetsUndefinedThis()
    {
        await using var engine = new JsEngine();
        var fakeLogger = new Microsoft.Extensions.Logging.Testing.FakeLogger();
        engine.RealmState.Logger = fakeLogger;

        var script = """
                     "use strict";
                     var seen = [];
                     new Uint8Array(2).every(function() {
                       seen.push(this);
                       return true;
                     });
                     seen;
                     """;

        var result = await engine.Evaluate(script);
        var seen = result switch
        {
            JsArray arr => (IReadOnlyList<object?>)arr.Items,
            IReadOnlyList<object?> list => list,
            _ => Array.Empty<object?>()
        };

        Assert.That(seen.Count, Is.EqualTo(2), "entry count");
        Assert.That(seen[0], Is.SameAs(Symbol.Undefined), "first this binding");
        Assert.That(seen[1], Is.SameAs(Symbol.Undefined), "second this binding");

        // Ensure we actually observed prototype logs when arrays were allocated.
        Assert.That(fakeLogger.Collector.LatestRecord.Message, Does.Contain("Prototype reassigned"));
    }
}
