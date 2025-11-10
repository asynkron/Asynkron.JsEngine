using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ObjectDescriptorTests
{
    // Tests for Object.defineProperty with writable descriptor
    
    [Fact]
    public async Task DefineProperty_Writable_False_Prevents_Modification()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'readonly', {
                value: 42,
                writable: false
            });
            obj.readonly = 100;  // Should be ignored
            obj.readonly;
        ");
        Assert.Equal(42d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Writable_True_Allows_Modification()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'prop', {
                value: 10,
                writable: true
            });
            obj.prop = 20;
            obj.prop;
        ");
        Assert.Equal(20d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Default_Writable_Is_True()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'prop', { value: 10 });
            obj.prop = 20;
            obj.prop;
        ");
        Assert.Equal(20d, result);
    }
    
    // Tests for Object.defineProperty with enumerable descriptor
    
    [Fact]
    public async Task DefineProperty_Enumerable_False_Hides_From_Keys()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'visible', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'hidden', { value: 2, enumerable: false });
            Object.keys(obj).length;
        ");
        Assert.Equal(1d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Enumerable_False_Visible_In_GetOwnPropertyNames()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'hidden', { value: 2, enumerable: false });
            Object.getOwnPropertyNames(obj).length;
        ");
        Assert.Equal(1d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Enumerable_True_Shows_In_Keys()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'visible', { value: 1, enumerable: true });
            Object.keys(obj)[0];
        ");
        Assert.Equal("visible", result);
    }
    
    [Fact]
    public async Task DefineProperty_Multiple_Properties_Different_Enumerable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'b', { value: 2, enumerable: false });
            Object.defineProperty(obj, 'c', { value: 3, enumerable: true });
            Object.keys(obj).join(',');
        ");
        Assert.Equal("a,c", result);
    }
    
    // Tests for Object.defineProperty with configurable descriptor
    
    [Fact]
    public async Task DefineProperty_Configurable_False_Prevents_Redefinition()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'prop', {
                value: 42,
                configurable: false
            });
            Object.defineProperty(obj, 'prop', {
                value: 100
            });
            obj.prop;
        ");
        Assert.Equal(42d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Configurable_True_Allows_Redefinition()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'prop', {
                value: 42,
                configurable: true
            });
            Object.defineProperty(obj, 'prop', {
                value: 100
            });
            obj.prop;
        ");
        Assert.Equal(100d, result);
    }
    
    // Tests for Object.defineProperty with getter/setter
    
    [Fact]
    public async Task DefineProperty_Getter_Works()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { _value: 5 };
            Object.defineProperty(obj, 'computed', {
                ['get']: function() { return this._value * 2; }
            });
            obj.computed;
        ");
        Assert.Equal(10d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Setter_Works()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'computed', {
                ['set']: function(v) { this._value = v * 2; }
            });
            obj.computed = 5;
            obj._value;
        ");
        Assert.Equal(10d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Getter_And_Setter_Work_Together()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'computed', {
                ['get']: function() { return this._value || 0; },
                ['set']: function(v) { this._value = v * 2; }
            });
            obj.computed = 5;
            obj.computed;
        ");
        Assert.Equal(10d, result);
    }
    
    [Fact]
    public async Task DefineProperty_Getter_Only_Property_Cannot_Be_Set()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'readonly', {
                ['get']: function() { return 42; }
            });
            obj.readonly = 100;  // Should be ignored
            obj.readonly;
        ");
        Assert.Equal(42d, result);
    }
    
    // Tests for Object.getOwnPropertyDescriptor
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Value()
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
    public async Task GetOwnPropertyDescriptor_Returns_Writable_True_For_Normal_Property()
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
    public async Task GetOwnPropertyDescriptor_Returns_Writable_False_For_Readonly()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                value: 42,
                writable: false
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.writable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Enumerable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                value: 42,
                enumerable: false
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.enumerable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Configurable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                value: 42,
                configurable: false
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.configurable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Undefined_For_Nonexistent()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 42 };
            Object.getOwnPropertyDescriptor(obj, 'y');
        ");
        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Getter()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                ['get']: function() { return 42; }
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            typeof desc.get;
        ");
        Assert.Equal("function", result);
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Returns_Setter()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                ['set']: function(v) { this._v = v; }
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            typeof desc.set;
        ");
        Assert.Equal("function", result);
    }
    
    [Fact]
    public async Task GetOwnPropertyDescriptor_Accessor_Has_No_Value_Or_Writable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'x', {
                ['get']: function() { return 42; }
            });
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            !Object.hasOwn(desc, 'value') && !Object.hasOwn(desc, 'writable');
        ");
        Assert.Equal(true, result);
    }
    
    // Tests for Object.getOwnPropertyNames
    
    [Fact]
    public async Task GetOwnPropertyNames_Returns_All_Properties()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'b', { value: 2, enumerable: false });
            Object.getOwnPropertyNames(obj).length;
        ");
        Assert.Equal(2d, result);
    }
    
    [Fact]
    public async Task GetOwnPropertyNames_Includes_Non_Enumerable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'hidden', { value: 1, enumerable: false });
            Object.getOwnPropertyNames(obj).includes('hidden');
        ");
        Assert.Equal(true, result);
    }
    
    // Tests for Object.create with property descriptors
    
    [Fact]
    public async Task Object_Create_With_Property_Descriptors()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null, {
                x: { value: 10, writable: true }
            });
            obj.x;
        ");
        Assert.Equal(10d, result);
    }
    
    [Fact]
    public async Task Object_Create_Property_Descriptors_Default_Enumerable_False()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null, {
                x: { value: 10 }
            });
            Object.keys(obj).length;
        ");
        Assert.Equal(0d, result);
    }
    
    [Fact]
    public async Task Object_Create_Property_Descriptors_Can_Be_Enumerable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null, {
                x: { value: 10, enumerable: true }
            });
            Object.keys(obj).length;
        ");
        Assert.Equal(1d, result);
    }
    
    [Fact]
    public async Task Object_Create_Multiple_Properties()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null, {
                x: { value: 10, enumerable: true },
                y: { value: 20, enumerable: true }
            });
            obj.x + obj.y;
        ");
        Assert.Equal(30d, result);
    }
    
    [Fact]
    public async Task Object_Create_With_Accessor_Descriptor()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = Object.create(null, {
                computed: {
                    ['get']: function() { return 42; },
                    enumerable: true
                }
            });
            obj.computed;
        ");
        Assert.Equal(42d, result);
    }
    
    // Tests for interaction with freeze/seal
    
    [Fact]
    public async Task Frozen_Object_Properties_Become_Non_Writable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.freeze(obj);
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.writable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task Frozen_Object_Properties_Become_Non_Configurable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.freeze(obj);
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.configurable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task Sealed_Object_Properties_Become_Non_Configurable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.seal(obj);
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.configurable;
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task Sealed_Object_Properties_Remain_Writable()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.seal(obj);
            let desc = Object.getOwnPropertyDescriptor(obj, 'x');
            desc.writable;
        ");
        Assert.Equal(true, result);
    }
    
    // Edge cases and error handling
    
    [Fact]
    public async Task DefineProperty_Returns_The_Object()
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
    public async Task DefineProperty_On_Frozen_Object_Is_Ignored()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.freeze(obj);
            Object.defineProperty(obj, 'y', { value: 20 });
            Object.hasOwn(obj, 'y');
        ");
        Assert.Equal(false, result);
    }
    
    [Fact]
    public async Task DefineProperty_Modify_Frozen_Property_Is_Ignored()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 10 };
            Object.freeze(obj);
            Object.defineProperty(obj, 'x', { value: 20 });
            obj.x;
        ");
        Assert.Equal(10d, result);
    }
    
    [Fact]
    public async Task Object_Keys_Respects_Enumerable_Flag()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'b', { value: 2, enumerable: false });
            Object.defineProperty(obj, 'c', { value: 3, enumerable: true });
            let keys = Object.keys(obj);
            keys.length === 2 && keys[0] === 'a' && keys[1] === 'c';
        ");
        Assert.Equal(true, result);
    }
    
    [Fact]
    public async Task Object_Values_Respects_Enumerable_Flag()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'b', { value: 2, enumerable: false });
            Object.defineProperty(obj, 'c', { value: 3, enumerable: true });
            let values = Object.values(obj);
            values.length === 2 && values[0] === 1 && values[1] === 3;
        ");
        Assert.Equal(true, result);
    }
    
    [Fact]
    public async Task Object_Entries_Respects_Enumerable_Flag()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = {};
            Object.defineProperty(obj, 'a', { value: 1, enumerable: true });
            Object.defineProperty(obj, 'b', { value: 2, enumerable: false });
            let entries = Object.entries(obj);
            entries.length === 1 && entries[0][0] === 'a' && entries[0][1] === 1;
        ");
        Assert.Equal(true, result);
    }
}
