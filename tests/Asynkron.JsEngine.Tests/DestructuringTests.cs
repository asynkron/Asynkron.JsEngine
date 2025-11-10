using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class DestructuringTests
{
    // Basic Array Destructuring Tests
    [Fact]
    public async Task BasicArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithMoreElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = [1, 2, 3, 4]; a * b;");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithFewerElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b, c] = [1, 2]; c;");
        Assert.Null(result);
    }

    [Fact]
    public async Task ArrayDestructuringWithSkippedElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, , c] = [1, 2, 3]; a + c;");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 10, b = 20] = [5]; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithAllDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 1, b = 2, c = 3] = []; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithRestElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest.length;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ArrayDestructuringRestElementValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest[0] + rest[1] + rest[2];");
        Assert.Equal(9d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithOnlyRestElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [...all] = [1, 2, 3]; all.length;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task NestedArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, [b, c]] = [1, [2, 3]]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task DeepNestedArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, [b, [c, d]]] = [1, [2, [3, 4]]]; a + b + c + d;");
        Assert.Equal(10d, result);
    }

    // Basic Object Destructuring Tests
    [Fact]
    public async Task BasicObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithRenaming()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x: a, y: b} = {x: 1, y: 2}; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x = 10, y = 20} = {x: 5}; x + y;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithRenamingAndDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x: a = 10, y: b = 20} = {x: 5}; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithRestProperties()
    {
        var engine = new JsEngine();
        engine.EvaluateSync("let {x, ...rest} = {x: 1, y: 2, z: 3};");
        var result = await engine.Evaluate("rest.y + rest.z;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task ObjectDestructuringMissingProperties()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y, z} = {x: 1, y: 2}; z;");
        Assert.Null(result);
    }

    [Fact]
    public async Task NestedObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: {c}} = {a: 1, b: {c: 2}}; a + c;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task DeepNestedObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: {c, d: {e}}} = {a: 1, b: {c: 2, d: {e: 3}}}; a + c + e;");
        Assert.Equal(6d, result);
    }

    // Mixed Array and Object Destructuring
    [Fact]
    public async Task ArrayDestructuringWithNestedObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, {b, c}] = [1, {b: 2, c: 3}]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithNestedArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: [c, d]} = {a: 1, b: [2, 3]}; a + c + d;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ComplexMixedDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let {x, y: [a, b], z: {m, n}} = {x: 1, y: [2, 3], z: {m: 4, n: 5}};
            x + a + b + m + n;
        ");
        Assert.Equal(15d, result);
    }

    // Const and Var Destructuring
    [Fact]
    public async Task ConstArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("const [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ConstObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("const {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task VarArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task VarObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    // Edge Cases
    [Fact]
    public async Task ArrayDestructuringEmptyArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = []; a;");
        Assert.Null(result);
    }

    [Fact]
    public async Task ObjectDestructuringEmptyObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y} = {}; x;");
        Assert.Null(result);
    }

    [Fact]
    public async Task ArrayDestructuringWithDefaultAndValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 10, b = 20] = [5, 15]; a + b;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task ObjectDestructuringMultiplePropertiesWithSomeDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let {a = 1, b, c = 3} = {b: 2}; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithRestAndDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 1, b = 2, ...rest] = [10]; a + b + rest.length;");
        Assert.Equal(12d, result);
    }

    // Real-world patterns
    [Fact]
    public async Task FunctionReturnDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function getCoords() {
                return [10, 20];
            }
            let [x, y] = getCoords();
            x + y;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task ObjectReturnDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function getUser() {
                return {name: ""Alice"", age: 30};
            }
            let {age} = getUser();
            age;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task ArrayDestructuringInExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let arr = [1, 2, 3];
            let [first, second] = arr;
            first + second;
        ");
        Assert.Equal(3d, result);
    }

    // Additional edge cases
    [Fact]
    public async Task MultipleArrayDestructuringStatements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let [a, b] = [1, 2];
            let [c, d] = [3, 4];
            a + b + c + d;
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task MultipleObjectDestructuringStatements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let {x} = {x: 1};
            let {y} = {y: 2};
            x + y;
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithExpressions()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let [a, b] = [1 + 1, 2 + 2];
            a + b;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ObjectDestructuringWithExpressionDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let {x = 5 + 5, y = 10 + 10} = {};
            x + y;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task ArrayDestructuringWithComputedValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function getArray() { return [10, 20, 30]; }
            let [x, y, z] = getArray();
            x + y + z;
        ");
        Assert.Equal(60d, result);
    }

    [Fact]
    public async Task RestElementCapturesEmpty()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let [a, b, ...rest] = [1, 2];
            rest.length;
        ");
        Assert.Equal(0d, result);
    }

    // Function Parameter Destructuring Tests
    [Fact]
    public async Task FunctionParameterArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test([x, y]) {
                return x + y;
            }
            test([1, 2]);
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task FunctionParameterObjectDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test({x, y}) {
                return x + y;
            }
            test({x: 1, y: 2});
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task FunctionParameterArrayDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test([x = 10, y = 20]) {
                return x + y;
            }
            test([5]);
        ");
        Assert.Equal(25d, result);
    }

    [Fact]
    public async Task FunctionParameterObjectDestructuringWithDefaults()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test({x = 10, y = 20}) {
                return x + y;
            }
            test({x: 5});
        ");
        Assert.Equal(25d, result);
    }

    [Fact]
    public async Task FunctionParameterNestedDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test({a, b: [c, d]}) {
                return a + c + d;
            }
            test({a: 1, b: [2, 3]});
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task FunctionParameterArrayRest()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test([a, ...rest]) {
                return a + rest.length;
            }
            test([1, 2, 3, 4]);
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task FunctionParameterObjectRest()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test({x, ...rest}) {
                return x + rest.y + rest.z;
            }
            test({x: 1, y: 2, z: 3});
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task FunctionParameterMixedDestructuringAndRegular()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function test(a, [b, c], {d}) {
                return a + b + c + d;
            }
            test(1, [2, 3], {d: 4});
        ");
        Assert.Equal(10d, result);
    }

    // Assignment Destructuring Tests (without declaration)
    [Fact]
    public async Task AssignmentArrayDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var a = 0;
            var b = 0;
            [a, b] = [10, 20];
            a + b;
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task VariableSwapping()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var x = 1;
            var y = 2;
            [x, y] = [y, x];
            x * 10 + y;
        ");
        Assert.Equal(21d, result);
    }

    [Fact]
    public async Task AssignmentNestedDestructuring()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var a = 0;
            var b = 0;
            var c = 0;
            [a, [b, c]] = [1, [2, 3]];
            a + b + c;
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task AssignmentWithRest()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var a = 0;
            var rest = [];
            [a, ...rest] = [1, 2, 3, 4];
            a + rest.length;
        ");
        Assert.Equal(4d, result);
    }
}