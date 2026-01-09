using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class JsOpsPropertyCallTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task GetProperty_WithStringKey_ReturnsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = { testProp: 42 };
            obj.testProp;
        ");
        
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetProperty_WithJsValueKey_ReturnsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = { dynamicKey: 'hello' };
            obj['dynamicKey'];
        ");
        
        Assert.Equal("hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetProperty_MissingProperty_ReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {};
            obj.nonexistent;
        ");
        
        Assert.True(result is null || JsValue.FromObjectUnsafe(result).IsUndefined);
    }

    [Fact(Timeout = 2000)]
    public async Task SetProperty_SetsValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {};
            obj.newProp = 123;
            obj.newProp;
        ");
        
        Assert.Equal(123.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DeleteProperty_DeletesProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = { toDelete: 'test' };
            const deleted = delete obj.toDelete;
            [deleted, obj.toDelete];
        ");
        
        var arr = result as JsArray;
        Assert.NotNull(arr);
        Assert.Equal(true, arr.Items[0].AsBoolean());
        Assert.True(arr.Items[1].IsUndefined);
    }

    [Fact(Timeout = 2000)]
    public async Task Call_InvokesFunction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function add(a, b) { return a + b; }
            add(5, 3);
        ");
        
        Assert.Equal(8.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Call_WithThisValue_UsesCorrectThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = { 
                value: 10,
                getValue: function() { return this.value; }
            };
            obj.getValue();
        ");
        
        Assert.Equal(10.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task CallMethod_InvokesMethodOnTarget()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const calculator = { 
                value: 5,
                multiply: function(factor) { return this.value * factor; }
            };
            calculator.multiply(3);
        ");
        
        Assert.Equal(15.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task New_ConstructsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function Person(name) { 
                this.name = name; 
            }
            const person = new Person('John');
            person.name;
        ");
        
        Assert.Equal("John", result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetProperty_OnArray_ReturnsElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [10, 20, 30];
            arr[1];
        ");
        
        Assert.Equal(20.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SetProperty_OnArray_SetsElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3];
            arr[1] = 99;
            arr[1];
        ");
        
        Assert.Equal(99.0, result);
    }
}
