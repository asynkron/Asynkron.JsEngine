using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ArrayIteratorMethodsTests
{
    [Fact]
    public async Task Array_Entries_ReturnsIndexValuePairs()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = ['a', 'b', 'c'];
            let entries = arr.entries();
            entries[0][0] + entries[0][1];
        ");
        Assert.Equal("0a", result);
    }

    [Fact]
    public async Task Array_Entries_WithMultipleElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [10, 20, 30];
            let entries = arr.entries();
            entries[1][0] + entries[1][1];
        ");
        Assert.Equal(1d + 20d, result);
    }

    [Fact]
    public async Task Array_Entries_ReturnsCorrectLength()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3, 4, 5];
            let entries = arr.entries();
            entries.length;
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Array_Keys_ReturnsIndices()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = ['a', 'b', 'c'];
            let keys = arr.keys();
            keys[0] + keys[1] + keys[2];
        ");
        Assert.Equal(0d + 1d + 2d, result);
    }

    [Fact]
    public async Task Array_Keys_ReturnsCorrectLength()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3, 4];
            let keys = arr.keys();
            keys.length;
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Array_Values_ReturnsElementValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [10, 20, 30];
            let values = arr.values();
            values[0] + values[1] + values[2];
        ");
        Assert.Equal(10d + 20d + 30d, result);
    }

    [Fact]
    public async Task Array_Values_ReturnsCorrectLength()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3];
            let values = arr.values();
            values.length;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Array_Values_WithStringArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = ['hello', 'world'];
            let values = arr.values();
            values[0] + ' ' + values[1];
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task Array_Entries_CanBeIterated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3];
            let entries = arr.entries();
            let sum = 0;
            for (let i = 0; i < entries.length; i++) {
                sum += entries[i][1];
            }
            sum;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Array_Keys_CanBeIterated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [10, 20, 30];
            let keys = arr.keys();
            let sum = 0;
            for (let i = 0; i < keys.length; i++) {
                sum += keys[i];
            }
            sum;
        ");
        Assert.Equal(3d, result); // 0 + 1 + 2
    }

    [Fact]
    public async Task Array_Values_CanBeIterated()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [5, 10, 15];
            let values = arr.values();
            let product = 1;
            for (let i = 0; i < values.length; i++) {
                product *= values[i];
            }
            product;
        ");
        Assert.Equal(750d, result); // 5 * 10 * 15
    }
}
