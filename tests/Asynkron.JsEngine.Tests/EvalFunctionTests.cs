using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Tests;

public class EvalFunctionTests
{
    [Fact(Timeout = 2000)]
    public async Task Eval_EvaluatesSimpleExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('2 + 2;');

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_AccessesVariablesInScope()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 10;
                                                       eval('x + 5;');

                                           """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_CreatesVariables()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('let y = 42;');
                                                       y;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('"hello" + " " + "world";');

                                           """);
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('function add(a, b) { return a + b; }');
                                                       add(3, 7);

                                           """);
        Assert.Equal(10d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Eval_WithNonStringReturnsValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval(42);

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithNoArgumentsReturnsUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval();

                                           """);
        Assert.True(ReferenceEquals(result, Symbols.Undefined));
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithComplexExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let base = 5;
                                                       let exp = eval('base * base;');
                                                       exp;

                                           """);
        Assert.Equal(25d, result);
    }

//     [Fact(Timeout = 2000)]
//     public async Task Eval_WithArrayExpression()
//     {
//         await using var engine = new JsEngine();
//         // Array literals need to be properly parenthesized when used with eval
//         var result = await engine.Evaluate("""
//
//                                                        let arr = eval('([1, 2, 3, 4, 5]);');
//                                                        arr[2];
//
//                                            """);
//         Assert.Equal(3d, result);
//     }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithObjectExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = eval('({ x: 10, y: 20 });');
                                                       obj.x + obj.y;

                                           """);
        Assert.Equal(30d, result);
    }
}
