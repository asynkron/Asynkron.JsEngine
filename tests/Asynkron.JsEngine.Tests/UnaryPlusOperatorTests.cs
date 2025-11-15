using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the unary plus operator which converts values to numbers
/// </summary>
public class UnaryPlusOperatorTests
{
    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Number_ShouldReturnNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+5;");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_NegativeNumber_ShouldReturnNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+-5;");
        Assert.Equal(-5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_StringNumber_ShouldConvertToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'42';");
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_StringFloat_ShouldConvertToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'3.14';");
        Assert.Equal(3.14d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_EmptyString_ShouldReturnZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'';");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_WhitespaceString_ShouldReturnZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'   ';");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InvalidString_ShouldReturnNaN()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'abc';");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_True_ShouldReturnOne()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+true;");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_False_ShouldReturnZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+false;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Null_ShouldReturnZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+null;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Undefined_ShouldReturnNaN()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+undefined;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Variable_ShouldConvertToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = '10'; +x;");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InCondition_ShouldWork()
    {
        var engine = new JsEngine();
        var code = """
            let length = '10';
            let result;
            if (+length !== length) {
                result = 'different';
            } else {
                result = 'same';
            }
            result;
            """;
        var result = await engine.Evaluate(code);
        Assert.Equal("different", result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InComparison_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+5 === 5;");
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InArithmetic_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+'5' + +'3';");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Array_ShouldConvertToNumber()
    {
        var engine = new JsEngine();
        
        // Empty array converts to 0
        var result1 = await engine.Evaluate("+[];");
        Assert.Equal(0d, result1);

        // Single element array
        var result2 = await engine.Evaluate("+[42];");
        Assert.Equal(42d, result2);

        // Multiple elements should be NaN
        var result3 = await engine.Evaluate("+[1, 2];");
        Assert.True(double.IsNaN((double)result3!));
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_Object_ShouldReturnNaN()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+({}); ");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_MultipleUnaryOperators_ShouldWork()
    {
        var engine = new JsEngine();
        
        // Double unary plus with parentheses
        var result1 = await engine.Evaluate("+(+5);");
        Assert.Equal(5d, result1);

        // Unary plus and minus
        var result2 = await engine.Evaluate("+-5;");
        Assert.Equal(-5d, result2);

        // Minus and plus
        var result3 = await engine.Evaluate("-+5;");
        Assert.Equal(-5d, result3);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_WithParentheses_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("+(+'5');");
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_BigInt_ShouldThrowError()
    {
        var engine = new JsEngine();
        
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await engine.Evaluate("+10n;");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InVariableAssignment_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = +'100'; x;");
        Assert.Equal(100d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UnaryPlus_InFunctionCall_ShouldWork()
    {
        var engine = new JsEngine();
        var code = """
            function add(a, b) {
                return a + b;
            }
            add(+'5', +'3');
            """;
        var result = await engine.Evaluate(code);
        Assert.Equal(8d, result);
    }
}
