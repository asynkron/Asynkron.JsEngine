using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using NUnit.Framework;

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

        Assert.That(result, Is.InstanceOf<IDictionary<string, object?>>());
        var snapshot = (IDictionary<string, object?>)result;
        Assert.That(snapshot?["hasOwn"], Is.EqualTo(false));
        Assert.That(snapshot?["value"], Is.EqualTo(Symbol.Undefined));
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

        Assert.That(result, Is.InstanceOf<IDictionary<string, object?>>());
        var snapshot = (IDictionary<string, object?>)result;
        TestContext.Progress.WriteLine(
            $"verifyProperty snapshot: i={snapshot?["i"]}, hasGlobalI={snapshot?["hasGlobalI"]}, globalValue={snapshot?["globalValue"]}");
        Assert.That(snapshot?["i"], Is.EqualTo(123d));
        Assert.That(snapshot?["hasGlobalI"], Is.EqualTo(false));
        Assert.That(snapshot?["globalValue"], Is.EqualTo(Symbol.Undefined));
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

        Assert.That(outcome, Is.InstanceOf<IDictionary<string, object?>>());
        var result = (IDictionary<string, object?>)outcome;
        Assert.That(result?["i"], Is.EqualTo(6d));
        Assert.That(result?["c0"], Is.EqualTo(4d));
        Assert.That(result?["c2"], Is.EqualTo(5d));
        Assert.That(result?["s1"], Is.EqualTo(3d));
        Assert.That(result?["cHas1"], Is.EqualTo(false));
        Assert.That(result?["sHas0"], Is.EqualTo(false));
        Assert.That(result?["sHas2"], Is.EqualTo(false));
    }

    [Test]
    public async Task IntercalatedComputedFieldsMatchUnderHarnessExecution()
    {
        await using var engine = new JsEngine
        {
            ExecutionTimeout = null
        };
        engine.RealmState.Logger = new ConsoleLogger("DebugClassFields");

        await engine.Evaluate(State.Sources["assert.js"]);
        await engine.Evaluate(State.Sources["sta.js"]);
        await engine.Evaluate(State.Sources["propertyHelper.js"]);

        var testCase =
            State.Test262Stream.GetTestFile("language/expressions/class/elements/intercalated-static-non-static-computed-fields.js");
        try
        {
            await engine.Evaluate(testCase.Program);
        }
        catch (ThrowSignal)
        {
            // Swallow assertion failures so we can inspect the resulting bindings.
        }

        var snapshot = await engine.Evaluate("""
                                             ({
                                               i,
                                               c0: c[0],
                                               c2: c[2],
                                               s1: C[1],
                                             cHas1: c.hasOwnProperty('1'),
                                             sHas0: C.hasOwnProperty('0'),
                                             sHas2: C.hasOwnProperty('2'),
                                             staticKeys: Object.getOwnPropertyNames(C),
                                             protoKeys: Object.getOwnPropertyNames(C.prototype),
                                             hasGlobalI: Object.prototype.hasOwnProperty.call(this, "i"),
                                             iDescriptor: Object.getOwnPropertyDescriptor(this, "i"),
                                             iDescriptorValue: Object.getOwnPropertyDescriptor(this, "i")?.value,
                                             iDescriptorWritable: Object.getOwnPropertyDescriptor(this, "i")?.writable,
                                             iDescriptorEnumerable: Object.getOwnPropertyDescriptor(this, "i")?.enumerable,
                                             iDescriptorConfigurable: Object.getOwnPropertyDescriptor(this, "i")?.configurable
                                             });
                                             """);

        Assert.That(snapshot, Is.InstanceOf<IDictionary<string, object?>>());
        var result = (IDictionary<string, object?>)snapshot;
        string JoinKeys(object? value) =>
            value switch
            {
                JsArray array => string.Join(",", array.Items),
                IEnumerable<object?> enumerable => string.Join(",", enumerable.Select(v => v?.ToString() ?? "null")),
                _ => value?.ToString() ?? "null"
            };
        if (!Equals(result["i"], 6d) ||
            !Equals(result["c0"], 4d) ||
            !Equals(result["c2"], 5d) ||
            !Equals(result["s1"], 3d) ||
            JsOps.ToBoolean(result["cHas1"]) ||
            JsOps.ToBoolean(result["sHas0"]) ||
            JsOps.ToBoolean(result["sHas2"]))
        {
            Assert.Fail(
                $"Harness run produced i={result["i"]}, c0={result["c0"]}, c2={result["c2"]}, s1={result["s1"]}, cHas1={result["cHas1"]}, sHas0={result["sHas0"]}, sHas2={result["sHas2"]}, staticKeys={JoinKeys(result["staticKeys"])}, protoKeys={JoinKeys(result["protoKeys"])}, hasGlobalI={result["hasGlobalI"]}, iDescriptorValue={result["iDescriptorValue"]}, iDescriptorWritable={result["iDescriptorWritable"]}, iDescriptorEnumerable={result["iDescriptorEnumerable"]}, iDescriptorConfigurable={result["iDescriptorConfigurable"]}");
        }
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
            Assert.That(thrown, Is.InstanceOf<JsObject>(), "Thrown value should be a JS error object");
            var error = (JsObject)thrown;
            error.TryGetProperty("name", out var name);
            Assert.That(name?.ToString(), Is.EqualTo("TypeError"));
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

        Assert.That(snapshot, Is.InstanceOf<IDictionary<string, object?>>());
        var result = (IDictionary<string, object?>)snapshot;
        TestContext.WriteLine(
            $"protoMatch={result["protoMatch"]}, protoKeys={result["protoKeys"]}, ctorProtoKeys={result["ctorProtoKeys"]}, ctorProtoCtor={result["ctorProtoCtor"]}, instanceType={result["instanceType"]}");
        Assert.That(result["protoMatch"], Is.EqualTo(true),
            "new C() should set [[Prototype]] to C.prototype");
        Assert.That(result["ctorProtoCtor"], Is.EqualTo(true),
            "C.prototype.constructor should point back to C");
        Assert.That(result["instanceType"], Is.EqualTo("function"),
            "c.x should resolve to the prototype method");
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
        Assert.That(snapshot, Is.InstanceOf<IDictionary<string, object?>>(),
            "Expected the snapshot to be a plain object");
        return (IDictionary<string, object?>)snapshot;
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
