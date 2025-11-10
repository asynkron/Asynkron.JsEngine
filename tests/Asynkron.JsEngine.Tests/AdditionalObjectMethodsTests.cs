using Xunit;

namespace Asynkron.JsEngine.Tests;

public class AdditionalObjectMethodsTests
{
    [Fact]
    public void Object_GetOwnPropertyNames_ReturnsAllPropertyNames()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 1, b: 2, c: 3 };
            let names = Object.getOwnPropertyNames(obj);
            names.length;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void Object_GetOwnPropertyNames_IncludesProperties()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10, y: 20 };
            let names = Object.getOwnPropertyNames(obj);
            names.includes('x') && names.includes('y');
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_GetOwnPropertyNames_WithEmptyObject()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            let names = Object.getOwnPropertyNames(obj);
            names.length;
        ");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_ReturnsDescriptor()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.value;
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_HasWritableProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.writable;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_HasEnumerableProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.enumerable;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_HasConfigurableProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.configurable;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_ForFrozenObject()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            Object.freeze(obj);
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.writable;
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_GetOwnPropertyDescriptor_ReturnsUndefinedForNonExistent()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            Object.getOwnPropertyDescriptor(obj, 'y');
        ");
        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public void Object_DefineProperty_DefinesNewProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', { value: 42 });
            obj.x;
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void Object_DefineProperty_ReturnsObject()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            let returned = Object.defineProperty(obj, 'x', { value: 42 });
            returned === obj;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_DefineProperty_UpdatesExistingProperty()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.defineProperty(obj, 'x', { value: 99 });
            obj.x;
        ");
        Assert.Equal(99d, result);
    }

    [Fact]
    public void Object_DefineProperty_WithMultipleProperties()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1 });
            Object.defineProperty(obj, 'b', { value: 2 });
            obj.a + obj.b;
        ");
        Assert.Equal(3d, result);
    }
}
