using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class AdditionalMethodsTests
{
    // String methods
    [Fact]
    public void StringReplaceAll()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = 'hello world hello';
            str.replaceAll('hello', 'hi');
        ");
        Assert.Equal("hi world hi", result);
    }

    [Fact]
    public void StringAt()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = 'hello';
            str.at(1);
        ");
        Assert.Equal("e", result);
    }

    [Fact]
    public void StringAtNegative()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = 'hello';
            str.at(-1);
        ");
        Assert.Equal("o", result);
    }

    [Fact]
    public void StringTrimStart()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = '  hello  ';
            str.trimStart();
        ");
        Assert.Equal("hello  ", result);
    }

    [Fact]
    public void StringTrimEnd()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let str = '  hello  ';
            str.trimEnd();
        ");
        Assert.Equal("  hello", result);
    }

    // Array methods
    [Fact]
    public void ArrayAt()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [10, 20, 30];
            arr.at(1);
        ");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void ArrayAtNegative()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [10, 20, 30];
            arr.at(-1);
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public void ArrayFlat()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, [2, 3], [4, [5, 6]]];
            let flat = arr.flat();
            flat[0] + flat[1] + flat[2] + flat[3];
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void ArrayFlatDepth()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, [2, [3, [4]]]];
            let flat = arr.flat(2);
            flat.length;
        ");
        Assert.Equal(4d, result); // 1, 2, 3, [4]
    }

    [Fact]
    public void ArrayFlatMap()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, 2, 3];
            let result = arr.flatMap(function(x) { return [x, x * 2]; });
            result[0] + result[1] + result[2] + result[3] + result[4] + result[5];
        ");
        Assert.Equal(18d, result);
    }

    [Fact]
    public void ArrayFindLast()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, 2, 3, 4, 5];
            arr.findLast(function(x) { return x > 2; });
        ");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void ArrayFindLastIndex()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, 2, 3, 4, 5];
            arr.findLastIndex(function(x) { return x > 2; });
        ");
        Assert.Equal(4d, result);
    }

    // Object methods
    [Fact]
    public void ObjectFromEntries()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let entries = [['a', 1], ['b', 2]];
            let obj = Object.fromEntries(entries);
            obj.a + obj.b;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ObjectHasOwn()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = { a: 1 };
            Object.hasOwn(obj, 'a');
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void ObjectHasOwnFalse()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = { a: 1 };
            Object.hasOwn(obj, 'b');
        ");
        Assert.False((bool)result!);
    }
}
