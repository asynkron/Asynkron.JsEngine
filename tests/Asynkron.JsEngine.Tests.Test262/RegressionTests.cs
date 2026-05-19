using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Tests.Test262;

[TestFixture]
public class RegressionTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task CreateTest262Engine_AlwaysAppliesExecutionTimeout(bool useSnapshot)
    {
        await using var engine = Test262Test.CreateTest262Engine(logger: null, debugMode: false, useSnapshot);
        Assert.That(engine.ExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [TestCase("built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js")]
    [TestCase("test/built-ins/decodeURIComponent/S15.1.3.2_A2.5_T1.js")]
    public void DecodeURIComponentFourByteFixture_UsesExtendedExecutionTimeout(string fileName)
    {
        var timeout = Test262Test.GetTest262ExecutionTimeout(fileName);

        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
    }

    [Test]
    public void OrdinaryTest262Fixtures_UseDefaultExecutionTimeout()
    {
        var timeout = Test262Test.GetTest262ExecutionTimeout(
            "built-ins/decodeURIComponent/S15.1.3.1_A1.1_T1.js");

        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

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

    [Test]
    public async Task ProxyPrototypeTrapsBindHandlerAsThis()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var target = {};
            var hasThis;
            var setThis;

            var handler = {
              has: function(t, prop) {
                hasThis = this;
                return prop in t;
              },
              set: function(t, prop, value, receiver) {
                setThis = this;
                return true;
              }
            };

            var proxy = new Proxy(target, handler);
            var array = new Array(1);
            Object.setPrototypeOf(array, proxy);

            0 in array;
            array[0] = 1;

            [
              hasThis === handler,
              hasThis === undefined,
              hasThis === proxy,
              hasThis === target,
              typeof hasThis,
              setThis === handler,
              setThis === undefined,
              setThis === proxy,
              setThis === target,
              typeof setThis
            ];
            """);

        var values = result as JsArray ?? throw new AssertionException("Expected array result");
        TestContext.WriteLine(
            $"has=[handler:{values.Items[0]}, undefined:{values.Items[1]}, proxy:{values.Items[2]}, target:{values.Items[3]}, type:{values.Items[4]}] " +
            $"set=[handler:{values.Items[5]}, undefined:{values.Items[6]}, proxy:{values.Items[7]}, target:{values.Items[8]}, type:{values.Items[9]}]");
        Assert.That(values.Items[0].AsBoolean(), Is.True);
        Assert.That(values.Items[5].AsBoolean(), Is.True);
    }

    [Test]
    public async Task InternalSyncFunctionInvokerInvokeWithContext_PreservesExplicitReceiver()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var handler = {
              trap: function() {
                return this;
              }
            };

            [handler, handler.trap];
            """);

        var values = result as JsArray ?? throw new AssertionException("Expected array result");
        var handlerValue = values.Items[0];
        var trapValue = values.Items[1];
        var invoker = trapValue.ObjectValue as Asynkron.JsEngine.Ast.TypedAstEvaluator.SyncFunctionInvoker
                      ?? throw new AssertionException("Expected SyncFunctionInvoker");

        var invoked = invoker.InvokeWithContext([], handlerValue, null);
        Assert.That(invoked.Kind, Is.EqualTo(JsValueKind.Object));
        Assert.That(ReferenceEquals(invoked.ObjectValue, handlerValue.ObjectValue), Is.True);

        var rebuiltHandlerValue = JsValue.FromObjectUnsafe(handlerValue.ObjectValue);
        var rebuiltInvoked = invoker.InvokeWithContext([], rebuiltHandlerValue, null);
        Assert.That(rebuiltInvoked.Kind, Is.EqualTo(JsValueKind.Object));
        Assert.That(ReferenceEquals(rebuiltInvoked.ObjectValue, handlerValue.ObjectValue), Is.True);
    }

    [Test]
    public async Task ProxyPrototypeTrapsAreActuallyInvoked()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var hasCalled = false;
            var setCalled = false;

            var handler = {
              has: function() {
                hasCalled = true;
                return true;
              },
              set: function() {
                setCalled = true;
                return true;
              }
            };

            var proxy = new Proxy({}, handler);
            var array = new Array(1);
            Object.setPrototypeOf(array, proxy);

            var hasResult = 0 in array;
            array[0] = 1;

            [hasCalled, hasResult, setCalled];
            """);

        var values = result as JsArray ?? throw new AssertionException("Expected array result");
        Assert.That(values.Items[0].AsBoolean(), Is.True);
        Assert.That(values.Items[1].AsBoolean(), Is.True);
        Assert.That(values.Items[2].AsBoolean(), Is.True);
    }

    [Test]
    public async Task OrdinaryFunctionCall_BindsExplicitReceiver()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var handler = {};
            var seen;

            (function() {
              seen = this;
            }).call(handler);

            seen === handler;
            """);

        Assert.That(result is bool boolean && boolean, Is.True);
    }

    [Test]
    public async Task ObjectPropertyFunctionCall_BindsExplicitReceiver()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var handler = {
              trap: function() {
                return this;
              }
            };

            var trap = handler.trap;
            trap.call(handler) === handler;
            """);

        Assert.That(result is bool boolean && boolean, Is.True);
    }

    [Test]
    public async Task DirectObjectPropertyFunctionCall_PreservesReceiverInScriptState()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(
            """
            var seen;
            var handler = {
              trap: function() {
                seen = this;
              }
            };

            handler.trap();
            seen === handler;
            """);

        Assert.That(result is bool boolean && boolean, Is.True);
    }

    [Test]
    public async Task HostSideInvokeOfObjectPropertyFunction_BindsExplicitReceiver()
    {
        var engine = new JsEngine();
        var handler = await engine.Evaluate(
            """
            var handler = {
              trap: function() {
                return this === handler;
              }
            };

            handler;
            """) as JsObject ?? throw new AssertionException("Expected handler object");

        Assert.That(handler.TryGetProperty("trap", out var trapValue), Is.True);
        Assert.That(trapValue.TryGetObject<IJsCallable>(out var trap), Is.True);

        var result = trap.Invoke(Array.Empty<JsValue>(), JsValue.FromObjectUnsafe((object)handler));
        Assert.That(result.AsBoolean(), Is.True);
    }

    [Test]
    public async Task HostSideInvokeOfObjectPropertyFunction_PreservesThisInScriptState()
    {
        var engine = new JsEngine();
        var handler = await engine.Evaluate(
            """
            var seen;
            var handler = {
              trap: function() {
                seen = this;
              }
            };

            handler;
            """) as JsObject ?? throw new AssertionException("Expected handler object");

        Assert.That(handler.TryGetProperty("trap", out var trapValue), Is.True);
        Assert.That(trapValue.TryGetObject<IJsCallable>(out var trap), Is.True);

        _ = trap.Invoke(Array.Empty<JsValue>(), JsValue.FromObjectUnsafe((object)handler));

        var result = await engine.Evaluate("seen === handler;");
        Assert.That(result is bool boolean && boolean, Is.True);
    }

    [Test]
    public async Task HostSideInvokeOfProxyHandlerTrap_BindsExplicitReceiver()
    {
        var engine = new JsEngine();
        var proxy = await engine.Evaluate(
            """
            var target = {};
            var handler = {
              has: function(target, prop) {
                return this === handler;
              }
            };

            new Proxy(target, handler);
            """) as JsProxy ?? throw new AssertionException("Expected proxy object");

        var handler = proxy.Handler as JsObject ?? throw new AssertionException("Expected proxy handler object");
        Assert.That(handler.TryGetProperty("has", JsValue.FromObjectUnsafe((object)handler), out var trapValue), Is.True);
        Assert.That(trapValue.TryGetObject<IJsCallable>(out var trap), Is.True);

        var result = trap.Invoke(
            [JsValue.FromObjectUnsafe((object)proxy.Target), new JsValue("0")],
            JsValue.FromObjectUnsafe((object)handler));

        Assert.That(result.AsBoolean(), Is.True);
    }

    [Test]
    public async Task ReflectConstruct_ProxiedNewTargetUsesTargetRealm()
    {
        await using var engine = Test262Test.CreateTest262Engine(logger: null, debugMode: false, useSnapshot: false);
        var realmEngines = new List<JsEngine>();
        var obj262 = new JsObject
        {
            ["createRealm"] = new HostFunction(_ =>
            {
                var realmEngine = Test262Test.CreateTest262Engine(logger: null, debugMode: false, useSnapshot: false);
                realmEngines.Add(realmEngine);
                var realmGlobal = realmEngine.GlobalObject;
                realmGlobal["global"] = realmGlobal;
                return (JsValue)realmGlobal;
            })
        };
        engine.SetGlobalValue("$262", obj262);

        var result = await engine.Evaluate(
            """
            var realm1 = $262.createRealm().global;
            var realm2 = $262.createRealm().global;
            var realm3 = $262.createRealm().global;

            var newTarget = new realm1.Function();
            newTarget.prototype = false;

            var newTargetProxy = new realm2.Proxy(newTarget, {});
            var array = Reflect.construct(realm3.Array, [], newTargetProxy);

            [
              array instanceof realm1.Array,
              Object.getPrototypeOf(array) === realm1.Array.prototype
            ];
            """);

        var values = result as JsArray ?? throw new AssertionException("Expected array result");
        Assert.That(values.Items[0].AsBoolean(), Is.True);
        Assert.That(values.Items[1].AsBoolean(), Is.True);

        foreach (var realmEngine in realmEngines)
        {
            await realmEngine.DisposeAsync();
        }
    }

    [Test]
    public async Task ReflectConstruct_ArrayNewTargetDoesNotChangeNonArrayTargetObjectKind()
    {
        await using var engine = Test262Test.CreateTest262Engine(logger: null, debugMode: false, useSnapshot: false);
        var realmEngines = new List<JsEngine>();
        var obj262 = new JsObject
        {
            ["createRealm"] = new HostFunction(_ =>
            {
                var realmEngine = Test262Test.CreateTest262Engine(logger: null, debugMode: false, useSnapshot: false);
                realmEngines.Add(realmEngine);
                var realmGlobal = realmEngine.GlobalObject;
                realmGlobal["global"] = realmGlobal;
                return (JsValue)realmGlobal;
            })
        };
        engine.SetGlobalValue("$262", obj262);

        var result = await engine.Evaluate(
            """
            var realm = $262.createRealm().global;
            function F() { this.x = 1; }

            var value = Reflect.construct(F, [], realm.Array);

            [
              Array.isArray(value),
              value.x,
              Object.getPrototypeOf(value) === realm.Array.prototype
            ];
            """);

        var values = result as JsArray ?? throw new AssertionException("Expected array result");
        Assert.That(values.Items[0].AsBoolean(), Is.False);
        Assert.That(values.Items[1].AsDouble(), Is.EqualTo(1d));
        Assert.That(values.Items[2].AsBoolean(), Is.True);

        foreach (var realmEngine in realmEngines)
        {
            await realmEngine.DisposeAsync();
        }
    }

    [Test]
    public void ReflectConstruct_ArrayNewTargetWithNonObjectPrototypeUsesObjectFallbackForNonArrayTarget()
    {
        var realm = new RealmState
        {
            ObjectPrototype = new JsObject(),
            ArrayPrototype = new JsObject()
        };
        var arrayNewTarget = new HostFunction(_ => JsValue.Undefined, realm);
        arrayNewTarget.PropertiesObject.ForceDeleteOwnProperty("prototype");
        arrayNewTarget.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = JsValue.False,
                Writable = true,
                Enumerable = false,
                Configurable = false
            });
        realm.ArrayConstructor = arrayNewTarget;

        var target = new HostFunction(_ => JsValue.Undefined, realm);

        var prototype = ReflectHelper.ResolveConstructPrototype(arrayNewTarget, target, realm);

        Assert.That(prototype, Is.SameAs(realm.ObjectPrototype));
        Assert.That(prototype, Is.Not.SameAs(realm.ArrayPrototype));
    }
}
