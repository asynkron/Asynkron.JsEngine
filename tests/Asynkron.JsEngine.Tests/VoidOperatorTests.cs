using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class VoidOperatorTests
{
    [Fact]
    public async Task VoidZero_ShouldReturnUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("void 0;");
        
        // Result should be the Undefined symbol
        Assert.IsType<Symbol>(result);
        var symbol = (Symbol)result;
        Assert.True(ReferenceEquals(symbol, JsSymbols.Undefined));
    }

    [Fact]
    public async Task TypeofVoidZero_ShouldReturnUndefinedString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof (void 0);");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task VoidExpression_ShouldEvaluateExpressionAndReturnUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("void (1 + 2);");
        
        Assert.IsType<Symbol>(result);
        var symbol = (Symbol)result;
        Assert.True(ReferenceEquals(symbol, JsSymbols.Undefined));
    }

    [Fact]
    public async Task VarAssignmentWithVoid_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("var value = void 0; value;");
        
        Assert.IsType<Symbol>(result);
        var symbol = (Symbol)result;
        Assert.True(ReferenceEquals(symbol, JsSymbols.Undefined));
    }

    [Fact]
    public async Task VarAssignmentWithVoid_TypeofShouldReturnUndefined()
    {
        var engine = new JsEngine();
        await engine.Evaluate("var value = void 0;");
        var result = await engine.Evaluate("typeof value;");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task VoidWithSideEffects_ShouldEvaluateExpression()
    {
        var engine = new JsEngine();
        var code = """
            let x = 0;
            let result = void (x = 42);
            x;
            """;
        var result = await engine.Evaluate(code);
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task VoidFunctionCall_ShouldCallFunctionAndReturnUndefined()
    {
        var engine = new JsEngine();
        var code = """
            let called = false;
            function test() { called = true; return 42; }
            let result = void test();
            [result, called];
            """;
        var result = await engine.Evaluate(code);
        
        Assert.IsType<JsArray>(result);
        var arr = (JsArray)result;
        Assert.Equal(2, arr.Length);
        
        // First element should be undefined
        var first = arr.Items[0];
        Assert.IsType<Symbol>(first);
        Assert.True(ReferenceEquals((Symbol)first, JsSymbols.Undefined));
        
        // Second element should be true
        Assert.Equal(true, arr.Items[1]);
    }

    [Fact]
    public async Task VoidAnyValue_ShouldReturnUndefined()
    {
        var engine = new JsEngine();
        
        // Test with various values
        var testCases = new[]
        {
            "void 'hello'",
            "void 123",
            "void true",
            "void false",
            "void null",
            "void undefined",
            "void []",
            "void {}"
        };

        foreach (var testCase in testCases)
        {
            var result = await engine.Evaluate(testCase);
            Assert.IsType<Symbol>(result);
            var symbol = (Symbol)result;
            Assert.True(ReferenceEquals(symbol, JsSymbols.Undefined), 
                $"Failed for: {testCase}");
        }
    }

    [Fact]
    public async Task VoidInExpression_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof (void 0) === 'undefined';");
        Assert.Equal(true, result);
    }
}
