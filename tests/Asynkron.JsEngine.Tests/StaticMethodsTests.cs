using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class StaticMethodsTests
{
    // Object static methods tests
    [Fact]
    public void ObjectKeys()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 1, b: 2, c: 3 };
            let keys = Object.keys(obj);
            keys[0] + keys[1] + keys[2];
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public void ObjectValues()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { a: 10, b: 20, c: 30 };
            let values = Object.values(obj);
            values[0] + values[1] + values[2];
        ");
        Assert.Equal(60d, result);
    }

    [Fact]
    public void ObjectEntries()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = { x: 1, y: 2 };
            let entries = Object.entries(obj);
            entries[0][0] + entries[0][1] + entries[1][0] + entries[1][1];
        ");
        Assert.Equal("x1y2", result);
    }

    [Fact]
    public void ObjectAssign()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let target = { a: 1 };
            let source1 = { b: 2 };
            let source2 = { c: 3 };
            Object.assign(target, source1, source2);
            target.a + target.b + target.c;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ObjectAssignOverwrites()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let target = { a: 1, b: 2 };
            let source = { b: 20, c: 30 };
            Object.assign(target, source);
            target.a + target.b + target.c;
        ");
        Assert.Equal(51d, result);
    }

    // Array static methods tests
    [Fact]
    public void ArrayIsArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = [1, 2, 3];
            let obj = { a: 1 };
            Array.isArray(arr) && !Array.isArray(obj);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public void ArrayIsArrayString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let str = 'hello';
            Array.isArray(str);
        ");
        Assert.False((bool)result!);
    }

    [Fact]
    public void ArrayFrom()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let str = 'abc';
            let arr = Array.from(str);
            arr[0] + arr[1] + arr[2];
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public void ArrayFromArray()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let original = [1, 2, 3];
            let copy = Array.from(original);
            copy[0] + copy[1] + copy[2];
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ArrayOf()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = Array.of(1, 2, 3, 4);
            arr[0] + arr[1] + arr[2] + arr[3];
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void ArrayOfSingle()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let arr = Array.of(5);
            arr[0];
        ");
        Assert.Equal(5d, result);
    }
}
