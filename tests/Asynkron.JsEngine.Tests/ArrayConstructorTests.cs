using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ArrayConstructorTests
{
    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_WithLength_CreatesArrayWithLength()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(5); arr.length;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_WithLength_ElementsAreNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(3); arr[0] === null && arr[1] === null && arr[2] === null;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_WithMultipleElements_CreatesArrayWithElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(1, 2, 3); arr.length;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_WithMultipleElements_HasCorrectValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array('a', 'b', 'c'); arr[0] + arr[1] + arr[2];");
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_WithZero_CreatesEmptyArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(0); arr.length;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_NoArguments_CreatesEmptyArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var arr = Array(); arr.length;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_Constructor_CanBeUsedInFunctions()
    {
        var engine = new JsEngine();
        var code = @"
            function fannkuch(n) {
                var perm = Array(n);
                var count = Array(n);
                return perm.length + count.length;
            }
            fannkuch(5);
        ";
        var result = await engine.Evaluate(code);
        Assert.Equal(10d, result);
    }
}
