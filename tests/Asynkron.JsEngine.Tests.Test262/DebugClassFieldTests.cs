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
}
