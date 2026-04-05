using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class AssignmentReferenceTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task InheritedNonWritableDataProperty_Sloppy_IgnoresWrite()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
function Foo() {}
Object.defineProperty(Foo.prototype, 'bar', { value: 1, writable: false });
var o = new Foo();
o.bar = 2;
({ own: o.hasOwnProperty('bar'), value: o.bar });
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("own", out var own));
        Assert.False(own.AsBoolean());
        Assert.True(obj.TryGetProperty("value", out var value));
        Assert.Equal(1d, value.AsDouble());
    }

    [Fact]
    public async Task InheritedNonWritableDataProperty_Strict_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate(@"
""use strict"";
function Foo() {}
Object.defineProperty(Foo.prototype, 'bar', { value: 1, writable: false });
var o = new Foo();
o.bar = 2;
");
        });

        Assert.IsType<JsTypes.JsObject>(ex.ThrownValue.ToObject());
    }

    [Fact]
    public void WithBindingReference_PreservesObjectTargetAcrossDelete()
    {
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext();
        context.AllowIdentifierCache = false;

        var x = Symbol.Intern("x");
        var outer = JsEnvironment.CreateInstance(engine.GlobalEnvironment, isFunctionScope: true, description: "outer");
        outer.DefineSlot(x, JsValue.FromDouble(0), SlotFlags.None);

        var withObject = new JsObject { RealmState = engine.RealmState };
        withObject.SetProperty("x", JsValue.FromDouble(1));
        var withEnv = JsEnvironment.CreateInstance(outer, description: "with", withObject: withObject);

        var reference = withEnv.ResolveIdentifierAssignmentReference(x, context);
        Assert.True(JsOps.DeletePropertyValueJsValue(JsValue.FromObjectUnsafe(withObject), new JsValue("x"), context));

        reference.SetValue(JsValue.FromDouble(2));

        Assert.True(withObject.TryGetProperty("x", out var objectValue));
        Assert.Equal(2d, objectValue.AsDouble());
        Assert.Equal(0d, outer.GetBindingValueDirect(x).AsDouble());
    }
}
