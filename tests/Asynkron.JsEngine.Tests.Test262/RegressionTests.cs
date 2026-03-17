using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests.Test262;

[TestFixture]
public class RegressionTests
{
    [Test]
    public async Task ForInMemberLhsInvokesArrayPrototypeSetter()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var obj = Object.create(null);
            var let, value;
            obj.key = 1;

            for (let in obj) ;

            Object.defineProperty(Array.prototype, "1", {
              set: function(param) {
                value = param;
              }
            });

            for ([let][1] in obj) ;
            [
              typeof Object.getOwnPropertyDescriptor(Array.prototype, "1").set,
              value
            ];
            """);

        var resultArray = result as JsArray ?? throw new AssertionException("Expected array result");
        TestContext.WriteLine($"SetterType={resultArray.Items[0]}, Value={resultArray.Items[1]}");
        Assert.That(resultArray.Items[0].IsString, Is.True);
        Assert.That(resultArray.Items[0].AsString(), Is.EqualTo("function"));
        Assert.That(resultArray.Items[1].IsString, Is.True);
        Assert.That(resultArray.Items[1].AsString(), Is.EqualTo("key"));
    }

    [Test]
    public async Task IntlCollatorDoesNotModifyLegacyRegExpStatics()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var regExpProperties = ["$1", "$2", "$3", "$4", "$5", "$6", "$7", "$8", "$9",
              "$_", "$*", "$&", "$+", "$`", "$'",
              "input", "lastMatch", "lastParen", "leftContext", "rightContext"
            ];

            var defaults = Object.create(null);
            (/(?:)/).test("");
            regExpProperties.forEach(function (property) {
              defaults[property] = RegExp[property];
            });

            (/(?:)/).test("");
            new Intl.Collator("de-DE-u-co-phonebk");

            var diffs = [];
            regExpProperties.forEach(function (property) {
              if (RegExp[property] !== defaults[property]) {
                diffs.push([property, defaults[property], RegExp[property]]);
              }
            });

            diffs;
            """);

        var diffs = result as JsArray ?? throw new AssertionException("Expected array result");
        if (diffs.Items.Count > 0)
        {
            foreach (var entry in diffs.Items)
            {
                if (!entry.TryGetObject<JsArray>(out var diff) || diff.Items.Count < 3)
                {
                    continue;
                }

                TestContext.WriteLine($"Diff: {diff.Items[0]} default={diff.Items[1]} actual={diff.Items[2]}");
            }
        }

        Assert.That(diffs.Items, Is.Empty);
    }

    [Test]
    public async Task ProxyReceiverSetRechecksOwnDescriptorOnEveryWrite()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var getOwnPropertyKeys = [];
            var definePropertyKeys = [];

            var target = { foo: 1 };
            var p = new Proxy(target, {
              getOwnPropertyDescriptor: function(target, key) {
                getOwnPropertyKeys.push(key);
                return Reflect.getOwnPropertyDescriptor(target, key);
              },
              defineProperty: function(target, key, desc) {
                definePropertyKeys.push(key);
                return Reflect.defineProperty(target, key, desc);
              }
            });

            p["foo"] = 2;
            p.foo = 2;
            p.foo = 2;
            var finalDescriptor = Object.getOwnPropertyDescriptor(target, "foo");

            [getOwnPropertyKeys, definePropertyKeys, p.foo, finalDescriptor.writable, finalDescriptor.enumerable, finalDescriptor.configurable];
            """);

        var snapshot = result as JsArray ?? throw new AssertionException("Expected array result");
        Assert.That(snapshot.Items[0].TryGetObject<JsArray>(out var getOwnKeys), Is.True);
        Assert.That(snapshot.Items[1].TryGetObject<JsArray>(out var defineKeys), Is.True);
        Assert.That(getOwnKeys!.Items.Select(static item => item.AsString()).ToArray(),
            Is.EqualTo(new[] { "foo", "foo", "foo" }));
        Assert.That(defineKeys!.Items.Select(static item => item.AsString()).ToArray(),
            Is.EqualTo(new[] { "foo", "foo", "foo" }));
        Assert.That(snapshot.Items[2].AsDouble(), Is.EqualTo(2d));
        Assert.That(snapshot.Items[3].AsBoolean(), Is.True);
        Assert.That(snapshot.Items[4].AsBoolean(), Is.True);
        Assert.That(snapshot.Items[5].AsBoolean(), Is.True);
    }
}
