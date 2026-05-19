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
    public async Task StrictIdentifierAssignment_ResolvesReferenceBeforeRhsCreatesGlobalProperty()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                "use strict";
                undeclared = (this.undeclared = 5);
                """);
        });

        Assert.Contains("ReferenceError", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalUndefinedAssignment_SloppyDoesNotMutateAndStrictThrows()
    {
        await using var engine = CreateEngine();

        var sloppyResult = await engine.Evaluate("""
            undefined = 1;
            undefined;
            """);

        Assert.Equal(Symbol.Undefined, sloppyResult);

        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate("""
                "use strict";
                undefined = 1;
                """);
        });

        Assert.Contains("TypeError", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostfixDecrement_GlobalAccessor_CallsSetterWithNewValueAndReturnsOldValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
var observed = 'unset';
Object.defineProperty(globalThis, 'x', {
  get() { return 1; },
  set(value) { observed = value; },
  configurable: true
});
var oldValue = x--;
({ oldValue, observed });
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("oldValue", out var oldValue));
        Assert.Equal(1d, oldValue.AsDouble());
        Assert.True(obj.TryGetProperty("observed", out var observed));
        Assert.Equal(0d, observed.AsDouble());
    }

    [Fact]
    public async Task PrefixIncrement_GlobalAccessor_CallsSetterWithNewValueAndReturnsNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
var observed = 'unset';
Object.defineProperty(globalThis, 'x', {
  get() { return 1; },
  set(value) { observed = value; },
  configurable: true
});
var newValue = ++x;
({ newValue, observed });
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("newValue", out var newValue));
        Assert.Equal(2d, newValue.AsDouble());
        Assert.True(obj.TryGetProperty("observed", out var observed));
        Assert.Equal(2d, observed.AsDouble());
    }

    [Fact]
    public async Task PostfixDecrement_GlobalAccessor_PreservesOldNegativeZero()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
var stored = 'unset';
Object.defineProperty(globalThis, 'x', {
  get() { return -0; },
  set(value) { stored = value; },
  configurable: true
});
var oldValue = x--;
({ oldIsNegativeZero: Object.is(oldValue, -0), stored });
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("oldIsNegativeZero", out var oldIsNegativeZero));
        Assert.True(oldIsNegativeZero.AsBoolean());
        Assert.True(obj.TryGetProperty("stored", out var stored));
        Assert.Equal(-1d, stored.AsDouble());
    }

    [Fact]
    public async Task PostfixDecrement_WithAccessorDeletedDuringGet_StrictWriteThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
var count = 0;
var scope = {
  get x() {
    delete this.x;
    return 2;
  }
};

with (scope) {
  (function() {
    'use strict';
    try {
      count++;
      x--;
      count++;
    } catch (error) {
      return { name: error.name, count, exists: 'x' in scope };
    }
  })();
}
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("name", out var name));
        Assert.Equal("ReferenceError", name.AsString());
        Assert.True(obj.TryGetProperty("count", out var count));
        Assert.Equal(1d, count.AsDouble());
        Assert.True(obj.TryGetProperty("exists", out var exists));
        Assert.False(exists.AsBoolean());
    }

    [Fact]
    public void WithBindingReference_PreservesObjectTargetAcrossDelete()
    {
        using var engine = CreateEngine();
        var context = engine.RealmState.CreateContext(mode: ScopeMode.Sloppy);
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

    [Fact]
    public async Task StrictWithAssignment_PreservesReferenceAndThrowsAfterRhsDeletesProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var count = 0;
            var scope = { x: 0 };
            var thrownName = "none";

            with (scope) {
                (function() {
                    "use strict";
                    try {
                        count++;
                        x = (delete scope.x, count++, 1);
                        count++;
                    } catch (error) {
                        thrownName = error.name;
                    }
                })();
            }

            ({ thrownName, count, exists: "x" in scope });
            """);

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("thrownName", out var thrownName));
        Assert.Equal("ReferenceError", thrownName.AsString());
        Assert.True(obj.TryGetProperty("count", out var count));
        Assert.Equal(2d, count.AsDouble());
        Assert.True(obj.TryGetProperty("exists", out var exists));
        Assert.False(exists.AsBoolean());
    }
}
