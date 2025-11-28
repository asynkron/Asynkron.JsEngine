namespace Asynkron.JsEngine.Tests;

using Asynkron.JsEngine.Ast;

public class DestructuringTests
{
    // Basic Array Destructuring Tests
    [Fact(Timeout = 2000)]
    public async Task BasicArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithMoreElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = [1, 2, 3, 4]; a * b;");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithFewerElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b, c] = [1, 2]; c;");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithSkippedElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, , c] = [1, 2, 3]; a + c;");
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 10, b = 20] = [5]; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithAllDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 1, b = 2, c = 3] = []; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithRestElement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest.length;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringRestElementValues()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, ...rest] = [1, 2, 3, 4]; rest[0] + rest[1] + rest[2];");
        Assert.Equal(9d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithOnlyRestElement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [...all] = [1, 2, 3]; all.length;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NestedArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, [b, c]] = [1, [2, 3]]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DeepNestedArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, [b, [c, d]]] = [1, [2, [3, 4]]]; a + b + c + d;");
        Assert.Equal(10d, result);
    }

    // Basic Object Destructuring Tests
    [Fact(Timeout = 2000)]
    public async Task BasicObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithRenaming()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x: a, y: b} = {x: 1, y: 2}; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x = 10, y = 20} = {x: 5}; x + y;");
        Assert.Equal(25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithRenamingAndDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x: a = 10, y: b = 20} = {x: 5}; a + b;");
        Assert.Equal(25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithRestProperties()
    {
        await using var engine = new JsEngine();
        var temp = await engine.Evaluate("let {x, ...rest} = {x: 1, y: 2, z: 3};");
        var result = await engine.Evaluate("rest.y + rest.z;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringMissingProperties()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y, z} = {x: 1, y: 2}; z;");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NestedObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: {c}} = {a: 1, b: {c: 2}}; a + c;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DeepNestedObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: {c, d: {e}}} = {a: 1, b: {c: 2, d: {e: 3}}}; a + c + e;");
        Assert.Equal(6d, result);
    }

    // Mixed Array and Object Destructuring
    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithNestedObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, {b, c}] = [1, {b: 2, c: 3}]; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithNestedArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {a, b: [c, d]} = {a: 1, b: [2, 3]}; a + c + d;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComplexMixedDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let {x, y: [a, b], z: {m, n}} = {x: 1, y: [2, 3], z: {m: 4, n: 5}};
                                                       x + a + b + m + n;

                                           """);
        Assert.Equal(15d, result);
    }

    // Const and Var Destructuring
    [Fact(Timeout = 2000)]
    public async Task ConstArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task VarArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var [a, b] = [1, 2]; a + b;");
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task VarObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var {x, y} = {x: 1, y: 2}; x + y;");
        Assert.Equal(3d, result);
    }

    // Edge Cases
    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringEmptyArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a, b] = []; a;");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringEmptyObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {x, y} = {}; x;");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithDefaultAndValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 10, b = 20] = [5, 15]; a + b;");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringMultiplePropertiesWithSomeDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let {a = 1, b, c = 3} = {b: 2}; a + b + c;");
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithRestAndDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let [a = 1, b = 2, ...rest] = [10]; a + b + rest.length;");
        Assert.Equal(12d, result);
    }

    // Real-world patterns
    [Fact(Timeout = 2000)]
    public async Task FunctionReturnDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function getCoords() {
                                                           return [10, 20];
                                                       }
                                                       let [x, y] = getCoords();
                                                       x + y;

                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectReturnDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function getUser() {
                                                           return {name: "Alice", age: 30};
                                                       }
                                                       let {age} = getUser();
                                                       age;

                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringInExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3];
                                                       let [first, second] = arr;
                                                       first + second;

                                           """);
        Assert.Equal(3d, result);
    }

    // Additional edge cases
    [Fact(Timeout = 2000)]
    public async Task MultipleArrayDestructuringStatements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let [a, b] = [1, 2];
                                                       let [c, d] = [3, 4];
                                                       a + b + c + d;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleObjectDestructuringStatements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let {x} = {x: 1};
                                                       let {y} = {y: 2};
                                                       x + y;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithExpressions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let [a, b] = [1 + 1, 2 + 2];
                                                       a + b;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectDestructuringWithExpressionDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let {x = 5 + 5, y = 10 + 10} = {};
                                                       x + y;

                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayDestructuringWithComputedValues()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function getArray() { return [10, 20, 30]; }
                                                       let [x, y, z] = getArray();
                                                       x + y + z;

                                           """);
        Assert.Equal(60d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RestElementCapturesEmpty()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let [a, b, ...rest] = [1, 2];
                                                       rest.length;

                                           """);
        Assert.Equal(0d, result);
    }

    // Function Parameter Destructuring Tests
    [Fact(Timeout = 2000)]
    public async Task FunctionParameterArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test([x, y]) {
                                                           return x + y;
                                                       }
                                                       test([1, 2]);

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterObjectDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test({x, y}) {
                                                           return x + y;
                                                       }
                                                       test({x: 1, y: 2});

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterArrayDestructuringWithDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test([x = 10, y = 20]) {
                                                           return x + y;
                                                       }
                                                       test([5]);

                                           """);
        Assert.Equal(25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterObjectDestructuringWithDefaults()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test({x = 10, y = 20}) {
                                                           return x + y;
                                                       }
                                                       test({x: 5});

                                           """);
        Assert.Equal(25d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterNestedDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test({a, b: [c, d]}) {
                                                           return a + c + d;
                                                       }
                                                       test({a: 1, b: [2, 3]});

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterArrayRest()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test([a, ...rest]) {
                                                           return a + rest.length;
                                                       }
                                                       test([1, 2, 3, 4]);

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterObjectRest()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test({x, ...rest}) {
                                                           return x + rest.y + rest.z;
                                                       }
                                                       test({x: 1, y: 2, z: 3});

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task FunctionParameterMixedDestructuringAndRegular()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test(a, [b, c], {d}) {
                                                           return a + b + c + d;
                                                       }
                                                       test(1, [2, 3], {d: 4});

                                           """);
        Assert.Equal(10d, result);
    }

    // Assignment Destructuring Tests (without declaration)
    [Fact(Timeout = 2000)]
    public async Task AssignmentArrayDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       var a = 0;
                                                       var b = 0;
                                                       [a, b] = [10, 20];
                                                       a + b;

                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task VariableSwapping()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       var x = 1;
                                                       var y = 2;
                                                       [x, y] = [y, x];
                                                       x * 10 + y;

                                           """);
        Assert.Equal(21d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task AssignmentNestedDestructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       var a = 0;
                                                       var b = 0;
                                                       var c = 0;
                                                       [a, [b, c]] = [1, [2, 3]];
                                                       a + b + c;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task AssignmentWithRest()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       var a = 0;
                                                       var rest = [];
                                                       [a, ...rest] = [1, 2, 3, 4];
                                                       a + rest.length;

                                           """);
        Assert.Equal(4d, result);
    }
}
