using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ObjectMethodsTests
{
    [Fact]
    public void Object_Freeze_Prevents_Property_Modification()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1, y: 2 };
            Object.freeze(obj);
            obj.x = 999;  // Should be ignored
            obj.x;
        ");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Object_Freeze_Prevents_Property_Addition()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.freeze(obj);
            obj.newProp = 999;  // Should be ignored
            Object.hasOwn(obj, 'newProp');
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_Freeze_Returns_Same_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            let frozen = Object.freeze(obj);
            frozen === obj;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_IsFrozen_Returns_True_For_Frozen_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.freeze(obj);
            Object.isFrozen(obj);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_IsFrozen_Returns_False_For_Normal_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.isFrozen(obj);
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_Seal_Prevents_Property_Addition()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.seal(obj);
            obj.newProp = 999;  // Should be ignored
            Object.hasOwn(obj, 'newProp');
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_Seal_Allows_Property_Modification()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.seal(obj);
            obj.x = 999;  // Should work
            obj.x;
        ");
        Assert.Equal(999.0, result);
    }

    [Fact]
    public void Object_Seal_Returns_Same_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            let sealed = Object.seal(obj);
            sealed === obj;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_IsSealed_Returns_True_For_Sealed_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.seal(obj);
            Object.isSealed(obj);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_IsSealed_Returns_False_For_Normal_Object()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.isSealed(obj);
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_Frozen_Is_Also_Sealed()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1 };
            Object.freeze(obj);
            Object.isSealed(obj);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Object_Create_Creates_Object_With_Null_Prototype()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null);
            typeof obj;
        ");
        Assert.Equal("object", result);
    }

    [Fact]
    public void Object_Create_Creates_Object_With_Specified_Prototype()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let proto = { x: 10 };
            let obj = Object.create(proto);
            obj.x;  // Should inherit from prototype
        ");
        Assert.Equal(10.0, result);
    }

    [Fact]
    public void Object_Create_New_Properties_Dont_Affect_Prototype()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let proto = { x: 10 };
            let obj = Object.create(proto);
            obj.y = 20;
            Object.hasOwn(proto, 'y');  // Should be false
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Object_Create_Can_Override_Inherited_Properties()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let proto = { x: 10 };
            let obj = Object.create(proto);
            obj.x = 999;
            obj.x;  // Should be 999, not 10
        ");
        Assert.Equal(999.0, result);
    }

    [Fact]
    public void Object_Create_Prototype_Chain_Works()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let grandparent = { a: 1 };
            let parent = Object.create(grandparent);
            parent.b = 2;
            let child = Object.create(parent);
            child.c = 3;
            child.a + child.b + child.c;  // Should access all levels
        ");
        Assert.Equal(6.0, result);
    }
}
