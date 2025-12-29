using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Foundation tests that run the same scripts in various execution contexts.
/// This ensures the engine behaves consistently whether code runs at top-level,
/// inside functions, try blocks, finally blocks, or class methods.
/// </summary>
public class WrappedFoundationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    /// <summary>
    /// Test data: (statements, returnExpr, expectedResult)
    /// - statements: Setup code (can be empty for pure expressions)
    /// - returnExpr: The expression to return/yield
    /// - expectedResult: Expected value
    /// </summary>
    public static TheoryData<string, string, object?> CoreSnippets => new()
    {
        // Loops
        { "let sum = 0; for (let i = 1; i <= 5; i++) { sum += i; }", "sum", 15d },
        { "let i = 0; while (i < 5) { i++; }", "i", 5d },
        { "let i = 0; do { i++; } while (i < 3);", "i", 3d },
        { "let sum = 0; for (let x of [1, 2, 3]) { sum += x; }", "sum", 6d },

        // Conditionals
        { "let x = 0; if (true) { x = 1; }", "x", 1d },
        { "let x = 0; if (false) { x = 1; } else { x = 2; }", "x", 2d },
        { "", "true ? 'yes' : 'no'", "yes" },

        // Variables
        { "let x = 20;", "x", 20d },
        { "const x = 30;", "x", 30d },
        { "let x = 1; x = 5;", "x", 5d },
        { "let x = 10; x += 5;", "x", 15d },

        // Arithmetic (pure expressions)
        { "", "1 + 2", 3d },
        { "", "5 - 3", 2d },
        { "", "4 * 3", 12d },
        { "", "10 / 2", 5d },
        { "", "10 % 3", 1d },
        { "", "2 ** 3", 8d },

        // Comparison
        { "", "1 == 1", true },
        { "", "1 === 1", true },
        { "", "1 != 2", true },
        { "", "1 < 2", true },
        { "", "2 > 1", true },

        // Logical
        { "", "true && true", true },
        { "", "false || true", true },
        { "", "!false", true },
        { "", "null ?? 'default'", "default" },

        // Arrays
        { "", "[1, 2, 3].length", 3d },
        { "", "[10, 20, 30][1]", 20d },
        { "", "[1, 2, 3].map(x => x * 2).join(',')", "2,4,6" },
        { "", "[1, 2, 3, 4].reduce((acc, x) => acc + x, 0)", 10d },
        { "const [a, b, c] = [1, 2, 3];", "a + b + c", 6d },

        // Objects
        { "", "({a: 1, b: 2}).a", 1d },
        { "const obj = {x: 10};", "obj.x", 10d },
        { "const {a, b} = {a: 10, b: 20};", "a + b", 30d },

        // Strings
        { "", "'hello' + ' ' + 'world'", "hello world" },
        { "", "'hello'.length", 5d },
        { "", "'hello'.toUpperCase()", "HELLO" },
        { "", "`1 + 2 = ${1 + 2}`", "1 + 2 = 3" },

        // Break/Continue
        { "let i = 0; while (true) { i++; if (i === 3) break; }", "i", 3d },
        { "let sum = 0; for (let i = 1; i <= 5; i++) { if (i === 3) continue; sum += i; }", "sum", 12d },

        // Nested functions/closures
        { "const add = (a, b) => a + b;", "add(4, 5)", 9d },
        { "function factorial(n) { return n <= 1 ? 1 : n * factorial(n - 1); }", "factorial(5)", 120d },
    };

    #region Wrappers

    private static string Strict(bool strict) => strict ? "'use strict'; " : "";

    // Function wrappers (can be strict or sloppy)
    private static string WrapInFunction(string statements, string returnExpr, bool strict = false) =>
        statements.Length > 0
            ? $"(function() {{ {Strict(strict)}{statements} return {returnExpr}; }})()"
            : $"(function() {{ {Strict(strict)}return {returnExpr}; }})()";

    private static string WrapInArrowFunction(string statements, string returnExpr, bool strict = false) =>
        statements.Length > 0
            ? $"(() => {{ {Strict(strict)}{statements} return {returnExpr}; }})()"
            : $"(() => {{ {Strict(strict)}return {returnExpr}; }})()";

    private static string WrapInTryBlock(string statements, string returnExpr, bool strict = false) =>
        $"(function() {{ {Strict(strict)}let __r; try {{ {statements} __r = {returnExpr}; }} catch(e) {{ throw e; }} return __r; }})()";

    private static string WrapInFinallyBlock(string statements, string returnExpr, bool strict = false) =>
        $"(function() {{ {Strict(strict)}let __r; try {{ }} finally {{ {statements} __r = {returnExpr}; }} return __r; }})()";

    private static string WrapInGenerator(string statements, string returnExpr, bool strict = false) =>
        statements.Length > 0
            ? $"(function* () {{ {Strict(strict)}{statements} yield {returnExpr}; }})().next().value"
            : $"(function* () {{ {Strict(strict)}yield {returnExpr}; }})().next().value";

    private static string WrapInNestedFunctions(string statements, string returnExpr, bool strict = false) =>
        statements.Length > 0
            ? $"(function() {{ {Strict(strict)}return (function() {{ {statements} return {returnExpr}; }})(); }})()"
            : $"(function() {{ {Strict(strict)}return (function() {{ return {returnExpr}; }})(); }})()";

    // Class wrappers (always strict by spec)
    private static string WrapInClassMethod(string statements, string returnExpr) =>
        statements.Length > 0
            ? $"class __T {{ run() {{ {statements} return {returnExpr}; }} }} new __T().run()"
            : $"class __T {{ run() {{ return {returnExpr}; }} }} new __T().run()";

    private static string WrapInStaticClassMethod(string statements, string returnExpr) =>
        statements.Length > 0
            ? $"class __T {{ static run() {{ {statements} return {returnExpr}; }} }} __T.run()"
            : $"class __T {{ static run() {{ return {returnExpr}; }} }} __T.run()";

    #endregion

    #region Tests - Function Wrappers

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InFunction(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInFunction(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InArrowFunction(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInArrowFunction(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InNestedFunctions(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInNestedFunctions(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    #endregion

    #region Tests - Try/Finally Wrappers

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InTryBlock(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInTryBlock(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InFinallyBlock(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInFinallyBlock(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    #endregion

    #region Tests - Class Wrappers

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InClassMethod(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInClassMethod(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InStaticClassMethod(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInStaticClassMethod(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    #endregion

    #region Tests - Generator Wrapper

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InGenerator(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInGenerator(statements, returnExpr);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    #endregion

    #region Tests - Strict Mode (function-based wrappers only, classes are always strict)

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InFunction_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInFunction(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InArrowFunction_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInArrowFunction(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InTryBlock_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInTryBlock(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InFinallyBlock_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInFinallyBlock(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InGenerator_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInGenerator(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(CoreSnippets))]
    public async Task InNestedFunctions_Strict(string statements, string returnExpr, object? expected)
    {
        await using var engine = CreateEngine();
        var script = WrapInNestedFunctions(statements, returnExpr, strict: true);
        Output.WriteLine($"Script: {script}");
        var result = await engine.Evaluate(script);
        Output.WriteLine($"Expected: {expected} | Actual: {result}");
        Assert.Equal(expected, result);
    }

    #endregion
}
