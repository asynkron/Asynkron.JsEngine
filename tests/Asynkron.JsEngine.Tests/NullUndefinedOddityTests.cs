using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for JavaScript oddities related to null and undefined values.
/// These tests ensure the engine correctly implements JavaScript's quirky behavior with these values.
/// </summary>
public class NullUndefinedOddityTests
{
    [Fact]
    public async Task TypeofNull_ReturnsObject()
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof null;");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task TypeofUndefined_ReturnsUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undefined;");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task TypeofNumber_ReturnsNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 42;");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task TypeofString_ReturnsString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof \"hello\";");
        Assert.Equal("string", result);
    }

    [Fact]
    public async Task TypeofBoolean_ReturnsBoolean()
    {
        var engine = new JsEngine();
        var trueResult = await engine.Evaluate("typeof true;");
        var falseResult = await engine.Evaluate("typeof false;");
        Assert.Equal("boolean", trueResult);
        Assert.Equal("boolean", falseResult);
    }

    [Fact]
    public async Task TypeofFunction_ReturnsFunction()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof function() { return 1; };");
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task TypeofObject_ReturnsObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof { a: 1 };");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task TypeofArray_ReturnsObject()
    {
        // Arrays are objects in JavaScript
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof [1, 2, 3];");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task LooseEquality_NullEqualsUndefined()
    {
        // JavaScript oddity: null == undefined (with loose equality)
        var engine = new JsEngine();
        var result = await engine.Evaluate("null == undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task StrictEquality_NullNotEqualsUndefined()
    {
        // But null !== undefined (with strict equality)
        var engine = new JsEngine();
        var result = await engine.Evaluate("null === undefined;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseInequality_NullNotNotEqualUndefined()
    {
        // null != undefined should be false
        var engine = new JsEngine();
        var result = await engine.Evaluate("null != undefined;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task StrictInequality_NullNotEqualUndefined()
    {
        // null !== undefined should be true
        var engine = new JsEngine();
        var result = await engine.Evaluate("null !== undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Null_IsFalsy()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact]
    public async Task Undefined_IsFalsy()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact]
    public async Task NotNull_IsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("!null;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task NotUndefined_IsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("!undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task NullishCoalescing_NullReturnsDefault()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task NullishCoalescing_UndefinedReturnsDefault()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task LogicalOr_NullReturnsRightOperand()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task LogicalOr_UndefinedReturnsRightOperand()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task NullPlusNumber_ReturnsNumber()
    {
        // null coerces to 0 in arithmetic
        var engine = new JsEngine();
        var result = await engine.Evaluate("null + 1;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task UndefinedPlusNumber_ReturnsNaN()
    {
        // undefined coerces to NaN in arithmetic
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined + 1;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public async Task NullMultipliedByNumber_ReturnsZero()
    {
        // null coerces to 0
        var engine = new JsEngine();
        var result = await engine.Evaluate("null * 5;");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task UndefinedMultipliedByNumber_ReturnsNaN()
    {
        // undefined coerces to NaN
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined * 5;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public async Task StringConcatenation_NullToString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("\"value: \" + null;");
        Assert.Equal("value: null", result);
    }

    [Fact]
    public async Task StringConcatenation_UndefinedToString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("\"value: \" + undefined;");
        Assert.Equal("value: undefined", result);
    }

    [Fact]
    public async Task TemplateLiteral_NullToString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("`value: ${null}`;");
        Assert.Equal("value: null", result);
    }

    [Fact]
    public async Task TemplateLiteral_UndefinedToString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("`value: ${undefined}`;");
        Assert.Equal("value: undefined", result);
    }

    [Fact]
    public async Task NullComparison_WithZero()
    {
        // JavaScript oddity: null >= 0 is true, but null > 0 and null == 0 are false
        var engine = new JsEngine();
        
        var greaterOrEqual = await engine.Evaluate("null >= 0;");
        Assert.True((bool)greaterOrEqual!);
        
        var greater = await engine.Evaluate("null > 0;");
        Assert.False((bool)greater!);
        
        var equals = await engine.Evaluate("null == 0;");
        Assert.False((bool)equals!);
    }

    [Fact]
    public async Task UndefinedComparison_WithZero()
    {
        // undefined compared with numbers returns false (except for !=)
        var engine = new JsEngine();
        
        var greater = await engine.Evaluate("undefined > 0;");
        Assert.False((bool)greater!);
        
        var less = await engine.Evaluate("undefined < 0;");
        Assert.False((bool)less!);
        
        var equals = await engine.Evaluate("undefined == 0;");
        Assert.False((bool)equals!);
    }

    [Fact]
    public async Task NullNotEqualToZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null != 0;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task UndefinedNotEqualToZero()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined != 0;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task StrictEquality_NullWithNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null === null;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task StrictEquality_UndefinedWithUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined === undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_NullNotEqualToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null == 0;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_UndefinedNotEqualToNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == 0;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_NullNotEqualToFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null == false;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_UndefinedNotEqualToFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == false;");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_NullNotEqualToEmptyString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null == \"\";");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task LooseEquality_UndefinedNotEqualToEmptyString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == \"\";");
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task TypeofInExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undefined === \"undefined\" ? \"correct\" : \"wrong\";");
        Assert.Equal("correct", result);
    }

    [Fact]
    public async Task UndefinedAsVariableValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = undefined;
                                                       typeof x;
                                                   
                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task NullAsVariableValue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = null;
                                                       typeof x;
                                                   
                                           """);
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task TypeofInFunction()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function checkType(value) {
                                                           return typeof value;
                                                       }
                                                       checkType(undefined);
                                                   
                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task MultipleTypeofChecks()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let results = [
                                                           typeof null,
                                                           typeof undefined,
                                                           typeof 42,
                                                           typeof "hello",
                                                           typeof true
                                                       ];
                                                       results[0] + "," + results[1] + "," + results[2] + "," + results[3] + "," + results[4];
                                                   
                                           """);
        Assert.Equal("object,undefined,number,string,boolean", result);
    }
}
