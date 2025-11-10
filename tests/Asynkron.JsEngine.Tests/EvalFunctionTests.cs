using Xunit;

namespace Asynkron.JsEngine.Tests;

public class EvalFunctionTests
{
    [Fact]
    public void Eval_EvaluatesSimpleExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval('2 + 2;');
        ");
        Assert.Equal(4d, result);
    }

    [Fact]
    public void Eval_AccessesVariablesInScope()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = 10;
            eval('x + 5;');
        ");
        Assert.Equal(15d, result);
    }

    [Fact]
    public void Eval_CreatesVariables()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval('let y = 42;');
            y;
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void Eval_WithString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval('""hello"" + "" "" + ""world"";');
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Eval_WithFunction()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval('function add(a, b) { return a + b; }');
            add(3, 7);
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void Eval_WithNonStringReturnsValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval(42);
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void Eval_WithNoArgumentsReturnsUndefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            eval();
        ");
        Assert.True(ReferenceEquals(result, JsSymbols.Undefined));
    }

    [Fact]
    public void Eval_WithComplexExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let base = 5;
            let exp = eval('base * base;');
            exp;
        ");
        Assert.Equal(25d, result);
    }

    [Fact]
    public void Eval_WithArrayExpression()
    {
        var engine = new JsEngine();
        // Array literals need to be properly parenthesized when used with eval
        var result = engine.EvaluateSync(@"
            let arr = eval('([1, 2, 3, 4, 5]);');
            arr[2];
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public void Eval_WithObjectExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let obj = eval('({ x: 10, y: 20 });');
            obj.x + obj.y;
        ");
        Assert.Equal(30d, result);
    }
}
