using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Tests.Helpers;

namespace Asynkron.JsEngine.Tests.Test262;

public class DebugClassFieldTests
{
    [Test]
    public async Task GlobalLetDoesNotLeakToGlobalObject()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        var result = await engine.Evaluate("""
                                           let i = 0;
                                           ({
                                             hasOwn: Object.prototype.hasOwnProperty.call(this, "i"),
                                           value: this.i
                                           });
                                           """);

        var snapshot = AssertPlainObject(result, "global let snapshot");
        Assert.That(snapshot["hasOwn"], Is.EqualTo(false));
        Assert.That(snapshot["value"], Is.EqualTo(Symbol.Undefined));
    }

    [Test]
    public async Task VerifyPropertyDoesNotClobberGlobalLets()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["propertyHelper.js"]);

        var result = await engine.Evaluate("""
                                           let i = 123;
                                           verifyProperty({ 0: 1 }, "0", {
                                             value: 1,
                                             enumerable: true,
                                             writable: true,
                                             configurable: true
                                           });
                                           ({
                                             i,
                                             hasGlobalI: Object.prototype.hasOwnProperty.call(this, "i"),
                                           globalValue: this.i
                                           });
                                           """);

        var snapshot = AssertPlainObject(result, "verifyProperty snapshot");
        TestContext.Progress.WriteLine(
            $"verifyProperty snapshot: i={snapshot["i"]}, hasGlobalI={snapshot["hasGlobalI"]}, globalValue={snapshot["globalValue"]}");
        Assert.That(snapshot["i"], Is.EqualTo(123d));
        Assert.That(snapshot["hasGlobalI"], Is.EqualTo(false));
        Assert.That(snapshot["globalValue"], Is.EqualTo(Symbol.Undefined));
    }

    [Test]
    public async Task IntercalatedComputedFieldsMatchSpecOrdering()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);
        await engine.Evaluate(State.Sources["propertyHelper.js"]);

        var outcome = await engine.Evaluate("""
                                            let i = 0;
                                            var C = class {
                                              [i++] = i++;
                                              static [i++] = i++;
                                              [i++] = i++;
                                            };
                                            let c = new C();
                                            ({
                                              i,
                                              c0: c[0],
                                              c2: c[2],
                                              s1: C[1],
                                              cHas1: c.hasOwnProperty('1'),
                                              sHas0: C.hasOwnProperty('0'),
                                            sHas2: C.hasOwnProperty('2')
                                            });
                                            """);

        var result = AssertPlainObject(outcome, "intercalated spec snapshot");
        Assert.That(result["i"], Is.EqualTo(6d));
        Assert.That(result["c0"], Is.EqualTo(4d));
        Assert.That(result["c2"], Is.EqualTo(5d));
        Assert.That(result["s1"], Is.EqualTo(3d));
        Assert.That(result["cHas1"], Is.EqualTo(false));
        Assert.That(result["sHas0"], Is.EqualTo(false));
        Assert.That(result["sHas2"], Is.EqualTo(false));
    }

    [Test]
    public async Task IntercalatedComputedFieldsMatchUnderHarnessExecution()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };
        engine.RealmState.Logger = new TestLogger(name: "DebugClassFields");

        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);
        await engine.Evaluate(State.Sources["propertyHelper.js"]);

        var testCase =
            State.Test262Stream.GetTestFile("language/expressions/class/elements/intercalated-static-non-static-computed-fields.js");
        try
        {
            await engine.Evaluate(testCase.Program);
        }
        catch (ThrowSignal signal)
        {
            // Swallow assertion failures so we can inspect the resulting bindings.
            var thrown = signal.ThrownValue;
            TestContext.Progress.WriteLine(
                $"test262 throw value: {thrown} ({thrown.ToObject()?.GetType().Name ?? "null"})");
            if (thrown.TryGetObject<JsObject>(out var errorObj) &&
                errorObj.TryGetProperty("message", out var message))
            {
                TestContext.Progress.WriteLine($"test262 throw message: {message}");
            }
        }

        // After running the harness test (which may delete properties while verifying),
        // create a fresh class instance in the same realm and validate that field
        // initializers still wire up as expected.
        var invariantSnapshot = await engine.Evaluate("""
                                                      (() => {
                                                        let i = 0;
                                                        var D = class {
                                                          [i++] = i++;
                                                          static [i++] = i++;
                                                          [i++] = i++;
                                                        };
                                                        let d = new D();
                                                        return {
                                                          i,
                                                          d0: d[0],
                                                          d2: d[2],
                                                          s1: D[1],
                                                          dHas1: d.hasOwnProperty('1'),
                                                          DHas0: D.hasOwnProperty('0'),
                                                          DHas2: D.hasOwnProperty('2'),
                                                          protoMatch: Object.getPrototypeOf(d) === D.prototype,
                                                          instanceKeys: Object.getOwnPropertyNames(d),
                                                          protoKeys: Object.getOwnPropertyNames(D.prototype),
                                                          staticKeys: Object.getOwnPropertyNames(D)
                                                        };
                                                      })();
                                                      """);

        var check = AssertPlainObject(invariantSnapshot, "intercalated harness snapshot");
        string JoinKeys(object? value) =>
            value switch
            {
                JsArray array => string.Join(",", array.Items),
                IEnumerable<object?> enumerable => string.Join(",", enumerable.Select(v => v?.ToString() ?? "null")),
                _ => value?.ToString() ?? "null"
            };
        Assert.That(check["i"], Is.EqualTo(6d));
        Assert.That(check["d0"], Is.EqualTo(4d));
        Assert.That(check["d2"], Is.EqualTo(5d));
        Assert.That(check["s1"], Is.EqualTo(3d));
        Assert.That(check["protoMatch"], Is.EqualTo(true));
        Assert.That(JsOps.ToBoolean(JsValue.FromObjectUnsafe(check["dHas1"])), Is.EqualTo(false));
        Assert.That(JsOps.ToBoolean(JsValue.FromObjectUnsafe(check["DHas0"])), Is.EqualTo(false));
        Assert.That(JsOps.ToBoolean(JsValue.FromObjectUnsafe(check["DHas2"])), Is.EqualTo(false));
        Assert.That(JoinKeys(check["instanceKeys"]), Is.EqualTo("0,2"));
        Assert.That(JoinKeys(check["protoKeys"]), Is.EqualTo("constructor"));
        Assert.That(JoinKeys(check["staticKeys"]), Is.EqualTo("1,length,name,prototype"));
    }

    [Test]
    public async Task PrivateSetterShadowedByGetterThrowsTypeError()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        try
        {
            await engine.Evaluate("""
                                  (function() {
                                    class C {
                                      set #m(v) { this._v = v; }
                                      method(v) { this.#m = v; }
                                      B = class {
                                        method(o, v) { o.#m = v; }
                                        get #m() { return 'test262'; }
                                      }
                                    }
                                    let c = new C();
                                    let innerB = new c.B();
                                    innerB.method(innerB);
                                  })();
                                  """);
            Assert.Fail("Expected TypeError to be thrown");
        }
        catch (ThrowSignal signal)
        {
            var thrown = signal.ThrownValue;
            Assert.That(thrown.TryGetObject<JsObject>(out var error), Is.True, "Thrown value should be a JS error object");
            var errorObj = error ?? throw new AssertionException("Expected thrown value to be a JS error object");
            errorObj.TryGetProperty("name", out var name);
            Assert.That(name.ToString(), Is.EqualTo("TypeError"));
        }
    }

    [Test]
    public async Task DefaultClassPrototypeShouldBeAppliedToInstances()
    {
        // Regression guard: instances produced by a simple class constructor should
        // inherit from that constructor's prototype. Currently, `new C()` yields an
        // object whose [[Prototype]] is a fresh, empty JsObject rather than the
        // populated `C.prototype` (constructor.name is "C", prototype has "constructor" and "x").
        // This breaks private field/method lookups because the prototype chain is wrong.
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        var snapshot = await engine.Evaluate("""
                                             (function () {
                                               class C {
                                                 #x = 1;
                                                 x() { return this.#x; }
                                               }
                                               const c = new C();
                                               return {
                                                 protoMatch: Object.getPrototypeOf(c) === C.prototype,
                                                 protoKeys: Object.getOwnPropertyNames(Object.getPrototypeOf(c) || {}),
                                                 ctorProtoKeys: Object.getOwnPropertyNames(C.prototype),
                                                 ctorProtoCtor: C.prototype.constructor === C,
                                                 instanceType: typeof c.x
                                               };
                                             })();
                                             """);

        var result = AssertPlainObject(snapshot, "default class prototype snapshot");
        TestContext.WriteLine(
            $"protoMatch={result["protoMatch"]}, protoKeys={result["protoKeys"]}, ctorProtoKeys={result["ctorProtoKeys"]}, ctorProtoCtor={result["ctorProtoCtor"]}, instanceType={result["instanceType"]}");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            "new C() should set [[Prototype]] to C.prototype");
        Assert.That(result["ctorProtoCtor"], Is.EqualTo(true),
            "C.prototype.constructor should point back to C");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            "c.x should resolve to the prototype method");
    }

    [Test]
    public async Task PrivateFieldAccessByProxyReceiverSustainsBrand()
    {
        await using var engine = new JsEngine { ExecutionTimeout = null };

        var snapshot = await engine.Evaluate("""
                                             (() => {
                                               const calls = [];
                                               class ProxyBase {
                                                 constructor() {
                                                   return new Proxy(this, {
                                                     get(obj, prop) {
                                                       calls.push(prop);
                                                       return obj[prop];
                                                     }
                                                   });
                                                 }
                                               }

                                               class Test extends ProxyBase {
                                               #f = 3;
                                               method() { return this.#f; }
                                               }

                                               const t = new Test();
                                               const methodValue = t.method;
                                               const proto = Object.getPrototypeOf(t);
                                               const testProto = Test.prototype;
                                               const protoOwn = proto ? Object.getOwnPropertyNames(proto) : [];
                                               const testProtoOwn = testProto ? Object.getOwnPropertyNames(testProto) : [];
                                               return {
                                                 methodType: typeof methodValue,
                                                 methodValue,
                                                 protoMatch: proto === testProto,
                                                 protoKeys: Object.getOwnPropertyNames(Test.prototype),
                                                 protoOwn,
                                                 testProtoOwn,
                                                 protoCtorIsTest: proto?.constructor === Test,
                                                 testProtoCtorIsTest: testProto?.constructor === Test,
                                                 calls
                                               };
                                             })();
                                             """);

        var result = AssertPlainObject(snapshot, "proxy receiver snapshot");
        TestContext.Progress.WriteLine(
            $"proxy-private snapshot: methodType={result["methodType"]}, protoMatch={result["protoMatch"]}, protoKeys={result["protoKeys"]}, protoOwn={result["protoOwn"]}, testProtoOwn={result["testProtoOwn"]}, protoCtorIsTest={result["protoCtorIsTest"]}, testProtoCtorIsTest={result["testProtoCtorIsTest"]}, methodClrType={result["methodValue"]?.GetType().Name ?? "null"}");

        Assert.That(result["methodType"], Is.EqualTo("function"));
        Assert.That(result["protoMatch"], Is.EqualTo(true));
        Assert.That(result["methodValue"], Is.InstanceOf<IJsCallable>());
        Assert.That(result["calls"], Is.InstanceOf<JsArray>());
        var calls = (JsArray)result["calls"]!;
        Assert.That(calls.Items, Is.EqualTo(new object?[] { "method" }));
    }

    private static async Task<IDictionary<string, object?>> CapturePrototypeSnapshot(
        JsEngine engine,
        string createExpression)
    {
        var script = $@"(() => {{
  class C {{
    #x = 1;
    x() {{ return this.#x; }}
  }}
  const c = {createExpression};
  return {{
    protoMatch: Object.getPrototypeOf(c) === C.prototype,
    protoKeys: Object.getOwnPropertyNames(Object.getPrototypeOf(c) || {{}}),
    ctorProtoKeys: Object.getOwnPropertyNames(C.prototype),
    ctorProtoCtor: C.prototype.constructor === C,
    instanceType: typeof c.x
  }};
}})();";

        var snapshot = await engine.Evaluate(script);
        return AssertPlainObject(snapshot, "capture prototype snapshot");
    }

    private static IDictionary<string, object?> AssertPlainObject(object? value, string context)
    {
        if (value is IDictionary<string, object?> dict)
        {
            return dict;
        }

        throw new AssertionException($"Expected plain object for {context}");
    }

    [Test]
    public async Task PrototypeAttachedForPlainClassInstance()
    {
        await using var engine = new JsEngine { ExecutionTimeout = null };
        var result = await CapturePrototypeSnapshot(engine, "new C()");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            "new C() should set [[Prototype]] to C.prototype");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            "c.x should resolve to the prototype method");
    }

    [Test]
    public async Task PrototypeAttachedForProxyWrappedInstance()
    {
        await using var engine = new JsEngine { ExecutionTimeout = null };
        var result = await CapturePrototypeSnapshot(engine, "new Proxy(new C(), {})");
        TestContext.WriteLine(
            $"proxy protoMatch={result["protoMatch"]}, protoKeys={result["protoKeys"]}, ctorProtoKeys={result["ctorProtoKeys"]}, instanceType={result["instanceType"]}");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            "Proxy default handler should not strip the instance [[Prototype]]");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            "c.x should resolve to the prototype method even through a Proxy");
    }

    [Test]
    public async Task PrototypeAttachedWhenUsingReflectConstruct()
    {
        await using var engine = new JsEngine { ExecutionTimeout = null };
        var result = await CapturePrototypeSnapshot(engine, "Reflect.construct(C, [])");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            "Reflect.construct should still wire [[Prototype]] to C.prototype");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            "c.x should resolve to the prototype method when constructed via Reflect");
    }

    // Document the breadth of the prototype loss: multiple construction paths currently
    // yield instances whose [[Prototype]] is not C.prototype, breaking private brand lookup.
    [Test]
    [TestCase("new C()", "plain class construction")]
    [TestCase("new (new Proxy(C, {}))()", "constructor wrapped in a Proxy with default handler")]
    [TestCase("Reflect.construct(C, [])", "Reflect.construct with default newTarget")]
    [TestCase("Reflect.construct(new Proxy(C, {}), [])", "Reflect.construct with proxied target")]
    [TestCase("Reflect.construct(C, [], new Proxy(C, {}))", "Reflect.construct with proxied newTarget")]
    [TestCase("Reflect.construct(new Proxy(C, {}), [], new Proxy(C, {}))", "proxied target and newTarget")]
    public async Task PrototypeAttachedAcrossConstructionForms(string createExpression, string scenario)
    {
        await using var engine = new JsEngine { ExecutionTimeout = null };
        var result = await CapturePrototypeSnapshot(engine, createExpression);
        TestContext.Progress.WriteLine(
            $"scenario={scenario}, expr={createExpression}, protoMatch={result["protoMatch"]}, protoKeys={result["protoKeys"]}, ctorProtoKeys={result["ctorProtoKeys"]}, ctorProtoCtor={result["ctorProtoCtor"]}, instanceType={result["instanceType"]}");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            $"{scenario}: instance [[Prototype]] should be C.prototype");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            $"{scenario}: c.x should resolve to the prototype method");
    }

}
