using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class ConstructorThisTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Constructor_SetsProperty_WithSimpleValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function MyClass(value) {
                this.value = value;
            }

            var obj = new MyClass(42);
            obj.value;
        ");

        output.WriteLine($"Result: {result}");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task Constructor_SetsProperty_WithArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function MyClass(arr) {
                this.arr = arr;
            }

            var arr = [1, 2, 3];
            var obj = new MyClass(arr);
            obj.arr;
        ");

        output.WriteLine($"Result: {result}");
        output.WriteLine($"Result type: {result?.GetType()}");
        Assert.IsType<JsArray>(result);
    }

    [Fact]
    public async Task Constructor_SetsProperty_WithArrayFromArrayConstructor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function MyClass(arr) {
                console.log('Constructor called with:', typeof arr, arr);
                this.arr = arr;
                console.log('this.arr set to:', this.arr);
            }

            var arr = Array(1, 2, 3);
            console.log('Array created:', arr);
            var obj = new MyClass(arr);
            console.log('obj.arr:', obj.arr);
            obj.arr;
        ");

        output.WriteLine($"Result: {result}");
        output.WriteLine($"Result type: {result?.GetType()}");
        Assert.IsType<JsArray>(result);
    }

    [Fact]
    public async Task Constructor_AccessesPropertyLength()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function MyClass(arr) {
                this.arr = arr;
            }

            var obj = new MyClass([1, 2, 3]);
            obj.arr.length;
        ");

        output.WriteLine($"Result: {result}");
        Assert.Equal(3.0, result);
    }
}
