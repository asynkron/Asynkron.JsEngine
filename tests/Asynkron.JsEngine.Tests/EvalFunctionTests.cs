using Xunit;

namespace Asynkron.JsEngine.Tests;

public class EvalFunctionTests
{
    [Fact(Timeout = 2000)]
    public async Task Eval_EvaluatesSimpleExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('2 + 2;');
                                                   
                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_AccessesVariablesInScope()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 10;
                                                       eval('x + 5;');
                                                   
                                           """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_CreatesVariables()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('let y = 42;');
                                                       y;
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('"hello" + " " + "world";');
                                                   
                                           """);
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithFunction()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval('function add(a, b) { return a + b; }');
                                                       add(3, 7);
                                                   
                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithNonStringReturnsValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval(42);
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithNoArgumentsReturnsUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       eval();
                                                   
                                           """);
        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact(Timeout = 2000)]
    public async Task Eval_WithComplexExpression()
    {
        var engine = new JsEngine();
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
//         var engine = new JsEngine();
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
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = eval('({ x: 10, y: 20 });');
                                                       obj.x + obj.y;
                                                   
                                           """);
        Assert.Equal(30d, result);
    }
}
