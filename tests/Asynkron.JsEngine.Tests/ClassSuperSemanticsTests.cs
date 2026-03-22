using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class ClassSuperSemanticsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task DerivedConstructor_ThisAccessBeforeSuper_ThrowsReferenceErrorInAllShapes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
              method() { return 1; }
            }

            function probe(fn) {
              try {
                fn();
                return "ok";
              } catch (e) {
                return e && e.name ? e.name : String(e);
              }
            }

            [
              probe(() => new class extends Base { constructor() { super(this.x); } }()),
              probe(() => new class extends Base { constructor() { super(this); } }()),
              probe(() => new class extends Base { constructor() { super.method; } }()),
              probe(() => new class extends Base { constructor() { super.method(); } }()),
              probe(() => new class extends Base { constructor() { super.method(); super(this); } }()),
              probe(() => new class extends Base { constructor() { super(super.method()); } }()),
              probe(() => new class extends Base { constructor() { super(super()); } }()),
              probe(() => new class extends Base { constructor() { super(1, 2, Object.getPrototypeOf(this)); } }())
            ];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        var actual = array.Items
            .Select(item => item.TryGetString(out var text) ? text : item.ToString())
            .ToArray();
        Output.WriteLine(string.Join(", ", actual));
        if (CurrentLogger is not null)
        {
            foreach (var record in CurrentLogger.Collector.Snapshot()
                         .Where(r => r.Message.Contains("Super", StringComparison.Ordinal) ||
                                     r.Message.Contains("ThisInitialized", StringComparison.Ordinal) ||
                                     r.Message.Contains("thisInit", StringComparison.Ordinal)))
            {
                Output.WriteLine(record.Message);
            }
        }

        Assert.Equal(
            [
                "ReferenceError", "ReferenceError", "ReferenceError", "ReferenceError", "ReferenceError",
                "ReferenceError", "ReferenceError", "ReferenceError"
            ],
            actual);
    }

    [Fact(Timeout = 2000)]
    public async Task DerivedConstructor_SuperArgumentsEvaluateBeforeConstructorCheck()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var evaluatedArg = false;
            var caught;

            class C extends Object {
              constructor() {
                try {
                  super(evaluatedArg = true);
                } catch (err) {
                  caught = err;
                }
              }
            }

            Object.setPrototypeOf(C, parseInt);

            try {
              new C();
            } catch (_) {}

            [evaluatedArg, typeof caught, caught && caught.constructor === TypeError];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.Equal("object", array.Items[1].AsString());
        Assert.True(array.Items[2].AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task DerivedDefaultConstructor_InitializesInstanceFieldsAfterSuper()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {}

            class Derived extends Base {
              x = 1;
              y = this.x + 1;
            }

            var instance = new Derived();
            [instance.x, instance.y];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(1d, array.Items[0].NumberValue);
        Assert.Equal(2d, array.Items[1].NumberValue);
    }

    [Fact(Timeout = 2000)]
    public async Task DerivedConstructor_SuperCall_ForwardsNewTarget()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var expectedNewTarget = function() {};
            var thisValue, args, actualNewTarget;

            function Parent() {
              thisValue = this;
              args = arguments;
              actualNewTarget = new.target;
            }

            class Child extends Parent {
              constructor() {
                super(1, 2, 3);
              }
            }

            var instance = Reflect.construct(Child, [4, 5, 6], expectedNewTarget);
            [thisValue === instance, args.length, args[0], args[1], args[2], actualNewTarget === expectedNewTarget];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.Equal(3d, array.Items[1].NumberValue);
        Assert.Equal(1d, array.Items[2].NumberValue);
        Assert.Equal(2d, array.Items[3].NumberValue);
        Assert.Equal(3d, array.Items[4].NumberValue);
        Assert.True(array.Items[5].AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralSuperAssignment_NonStrict_FailedSet_DoesNotThrow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var obj = {
              method() {
                super.x = 8;
                Object.freeze(obj);
                super.y = 9;
              }
            };

            obj.method();
            [Object.prototype.hasOwnProperty.call(obj, "x"), Object.prototype.hasOwnProperty.call(obj, "y")];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.False(array.Items[1].AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralSuperAssignment_Strict_FailedSet_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";

            var obj = {
              method() {
                super.x = 8;
                Object.freeze(obj);
                try {
                  super.y = 9;
                  return "ok";
                } catch (e) {
                  return e.name;
                }
              }
            };

            obj.method();
            """);

        Assert.Equal("TypeError", result);
    }
}
