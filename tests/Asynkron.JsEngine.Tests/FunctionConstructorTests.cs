using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibFunction)]
public sealed class FunctionConstructorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task HostFunctionApplyExpandsArgumentsObject()
    {
        await using var engine = CreateEngine();
        engine.SetGlobalFunction("__captureApplyArgs", args =>
        {
            var parts = new string[args.Count];
            for (var i = 0; i < args.Count; i++)
            {
                parts[i] = args[i].ToObject()?.ToString() ?? "";
            }

            return string.Join(",", parts);
        });

        var result = await engine.Evaluate("""
            function forward() {
                return __captureApplyArgs.apply(null, arguments);
            }

            forward('port', 9615);
        """);

        Assert.Equal("port,9615", result);
    }

    [Fact]
    public async Task BuiltInNativeSourceUsesCreationTimeDisplayName()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const push = Array.prototype.push;
            const globalGetter = Object.getOwnPropertyDescriptor(RegExp.prototype, 'global').get;

            Object.defineProperty(push, 'name', { value: 'forged', configurable: true });
            Object.defineProperty(globalGetter, 'name', { value: '[a]]', configurable: true });

            [String(push), String(globalGetter)];
        """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function push() { [native code] }", array.Items[0].AsString());
        Assert.Equal("function get global() { [native code] }", array.Items[1].AsString());
    }

    [Fact]
    public void NativeSourceRejectsMalformedBracketedDisplayName()
    {
        var function = new HostFunction(_ => JsValue.Undefined, isConstructor: false);

        function.SetNativeSourceDisplayName("[a]]");

        Assert.Equal("function () { [native code] }", function.GetNativeFunctionSource());
    }

    [Fact]
    public async Task NewFunctionCreatesCallableBody()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("(new Function('a', 'b', 'return a + b;'))(2, 3);");

        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task NewFunctionCanBuildTypedArraySubclass()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function() {
              const Ctor = new Function('return class MyUint8Array extends Uint8Array {}')();
              const view = new Ctor(4);
              return {
                isFn: typeof Ctor,
                length: view.length,
                isView: ArrayBuffer.isView(view)
              };
            })();
        """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal("function", obj["isFn"]);
        Assert.Equal(4d, obj["length"]);
        Assert.True(obj["isView"] as bool?);
    }

    [Fact]
    public async Task NewFunctionCanReturnDistinctPrivateBrandedClasses()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
              let step = 0;

              try {
                const source = "return class C { #m() { return 'test262'; } access(o) { return o.#m(); } }";
                function createAndInstantiateClass() {
                  step = 1;
                  const factory = new Function(source);
                  step = 2;
                  const C = factory();
                  step = 3;
                  return new C();
                }

                const c1 = createAndInstantiateClass();
                const c2 = createAndInstantiateClass();

                return [
                  step,
                  c1.access(c1),
                  c2.access(c2),
                  (() => { try { c1.access(c2); return "no-throw"; } catch (e) { return e.name; } })(),
                  (() => { try { c2.access(c1); return "no-throw"; } catch (e) { return e.name; } })()
                ];
              } catch (e) {
                return ["threw", step, e && e.name, String(e && e.message ? e.message : e)];
              }
            })();
        """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(3d, array.Items[0].NumberValue);
        Assert.Equal("test262", array.Items[1].AsString());
        Assert.Equal("test262", array.Items[2].AsString());
        Assert.Equal("TypeError", array.Items[3].AsString());
        Assert.Equal("TypeError", array.Items[4].AsString());
    }

    [Fact]
    public async Task NewFunctionReturnedClassStaysConstructableAcrossFreshFactoryCreation()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (function () {
              const source = "return class C { #m() { return 'test262'; } access(o) { return o.#m(); } }";

              function inspect() {
                const factory = new Function(source);
                const Class = factory();
                const details = [
                  typeof factory,
                  typeof Class,
                  Object.prototype.hasOwnProperty.call(Class, "prototype"),
                  typeof Class.prototype
                ];

                try {
                  const instance = new Class();
                  details.push("constructed");
                  details.push(instance.access(instance));
                } catch (e) {
                  details.push(e.name);
                  details.push(String(e && e.message ? e.message : e));
                }

                return details;
              }

              return [inspect(), inspect()];
            })();
        """);

        var runs = Assert.IsType<JsArray>(result);
        Assert.Equal(2, runs.Items.Count);

        foreach (var run in runs.Items)
        {
            Assert.True(run.TryGetObject<JsArray>(out var details));
            Assert.Equal("function", details.Items[0].AsString());
            Assert.Equal("function", details.Items[1].AsString());
            Assert.True(details.Items[2].AsBoolean());
            Assert.Equal("object", details.Items[3].AsString());
            Assert.Equal("constructed", details.Items[4].AsString());
            Assert.Equal("test262", details.Items[5].AsString());
        }
    }
}
