using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class DestructuringTests
{
    // Basic Array Destructuring Tests
    [Fact]
    public void BasicArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ArrayDestructuringWithMoreElements()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, b] = [1, 2, 3, 4]; a * b;");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void ArrayDestructuringWithFewerElements()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, b, c] = [1, 2]; c;");
        Assert.Null(result);
    }

    [Fact]
    public void ArrayDestructuringWithSkippedElements()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, , c] = [1, 2, 3]; a + c;");
        Assert.Equal(4d, result);
    }

    [Fact]
    public void ArrayDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a = 10, b = 20] = [5]; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public void ArrayDestructuringWithAllDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a = 1, b = 2, c = 3] = []; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ArrayDestructuringWithRestElement()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest.length;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ArrayDestructuringRestElementValues()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest[0] + rest[1] + rest[2];");
        Assert.Equal(9d, result);
    }

    [Fact]
    public void ArrayDestructuringWithOnlyRestElement()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [...all] = [1, 2, 3]; all.length;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void NestedArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, [b, c]] = [1, [2, 3]]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void DeepNestedArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, [b, [c, d]]] = [1, [2, [3, 4]]]; a + b + c + d;");
        Assert.Equal(10d, result);
    }

    // Basic Object Destructuring Tests
    [Fact]
    public void BasicObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ObjectDestructuringWithRenaming()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x: a, y: b} = {x: 1, y: 2}; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ObjectDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x = 10, y = 20} = {x: 5}; x + y;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public void ObjectDestructuringWithRenamingAndDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x: a = 10, y: b = 20} = {x: 5}; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public void ObjectDestructuringWithRestProperties()
    {
        var engine = new JsEngine();
        engine.Evaluate("let {x, ...rest} = {x: 1, y: 2, z: 3};");
        var result = engine.Evaluate("rest.y + rest.z;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void ObjectDestructuringMissingProperties()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x, y, z} = {x: 1, y: 2}; z;");
        Assert.Null(result);
    }

    [Fact]
    public void NestedObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {a, b: {c}} = {a: 1, b: {c: 2}}; a + c;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void DeepNestedObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {a, b: {c, d: {e}}} = {a: 1, b: {c: 2, d: {e: 3}}}; a + c + e;");
        Assert.Equal(6d, result);
    }

    // Mixed Array and Object Destructuring
    [Fact]
    public void ArrayDestructuringWithNestedObject()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, {b, c}] = [1, {b: 2, c: 3}]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ObjectDestructuringWithNestedArray()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {a, b: [c, d]} = {a: 1, b: [2, 3]}; a + c + d;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ComplexMixedDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let {x, y: [a, b], z: {m, n}} = {x: 1, y: [2, 3], z: {m: 4, n: 5}};
            x + a + b + m + n;
        ");
        Assert.Equal(15d, result);
    }

    // Const and Var Destructuring
    [Fact]
    public void ConstArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("const [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void ConstObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("const {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void VarArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("var [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void VarObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("var {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    // Edge Cases
    [Fact]
    public void ArrayDestructuringEmptyArray()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a, b] = []; a;");
        Assert.Null(result);
    }

    [Fact]
    public void ObjectDestructuringEmptyObject()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {x, y} = {}; x;");
        Assert.Null(result);
    }

    [Fact]
    public void ArrayDestructuringWithDefaultAndValue()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a = 10, b = 20] = [5, 15]; a + b;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void ObjectDestructuringMultiplePropertiesWithSomeDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let {a = 1, b, c = 3} = {b: 2}; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public void ArrayDestructuringWithRestAndDefaults()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let [a = 1, b = 2, ...rest] = [10]; a + b + rest.length;");
        Assert.Equal(12d, result);
    }

    // Real-world patterns
    [Fact]
    public void FunctionReturnDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            function getCoords() {
                return [10, 20];
            }
            let [x, y] = getCoords();
            x + y;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public void ObjectReturnDestructuring()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            function getUser() {
                return {name: ""Alice"", age: 30};
            }
            let {age} = getUser();
            age;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public void ArrayDestructuringInExpression()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [1, 2, 3];
            let [first, second] = arr;
            first + second;
        ");
        Assert.Equal(3d, result);
    }
}
