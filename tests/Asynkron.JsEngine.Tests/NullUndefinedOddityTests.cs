using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for JavaScript oddities related to null and undefined values.
/// These tests ensure the engine correctly implements JavaScript's quirky behavior with these values.
/// </summary>
public class NullUndefinedOddityTests
{
    [Fact]
    public void TypeofNull_ReturnsObject()
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof null;");
        Assert.Equal("object", result);
    }

    [Fact]
    public void TypeofUndefined_ReturnsUndefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof undefined;");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public void TypeofNumber_ReturnsNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof 42;");
        Assert.Equal("number", result);
    }

    [Fact]
    public void TypeofString_ReturnsString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof \"hello\";");
        Assert.Equal("string", result);
    }

    [Fact]
    public void TypeofBoolean_ReturnsBoolean()
    {
        var engine = new JsEngine();
        var trueResult = engine.EvaluateSync("typeof true;");
        var falseResult = engine.EvaluateSync("typeof false;");
        Assert.Equal("boolean", trueResult);
        Assert.Equal("boolean", falseResult);
    }

    [Fact]
    public void TypeofFunction_ReturnsFunction()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof function() { return 1; };");
        Assert.Equal("function", result);
    }

    [Fact]
    public void TypeofObject_ReturnsObject()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof { a: 1 };");
        Assert.Equal("object", result);
    }

    [Fact]
    public void TypeofArray_ReturnsObject()
    {
        // Arrays are objects in JavaScript
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof [1, 2, 3];");
        Assert.Equal("object", result);
    }

    [Fact]
    public void LooseEquality_NullEqualsUndefined()
    {
        // JavaScript oddity: null == undefined (with loose equality)
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null == undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void StrictEquality_NullNotEqualsUndefined()
    {
        // But null !== undefined (with strict equality)
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null === undefined;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseInequality_NullNotNotEqualUndefined()
    {
        // null != undefined should be false
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null != undefined;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void StrictInequality_NullNotEqualUndefined()
    {
        // null !== undefined should be true
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null !== undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void Null_IsFalsy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact]
    public void Undefined_IsFalsy()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact]
    public void NotNull_IsTrue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("!null;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void NotUndefined_IsTrue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("!undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void NullishCoalescing_NullReturnsDefault()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public void NullishCoalescing_UndefinedReturnsDefault()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public void LogicalOr_NullReturnsRightOperand()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public void LogicalOr_UndefinedReturnsRightOperand()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact]
    public void NullPlusNumber_ReturnsNumber()
    {
        // null coerces to 0 in arithmetic
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null + 1;");
        Assert.Equal(1d, result);
    }

    [Fact]
    public void UndefinedPlusNumber_ReturnsNaN()
    {
        // undefined coerces to NaN in arithmetic
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined + 1;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public void NullMultipliedByNumber_ReturnsZero()
    {
        // null coerces to 0
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null * 5;");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void UndefinedMultipliedByNumber_ReturnsNaN()
    {
        // undefined coerces to NaN
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined * 5;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public void StringConcatenation_NullToString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("\"value: \" + null;");
        Assert.Equal("value: null", result);
    }

    [Fact]
    public void StringConcatenation_UndefinedToString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("\"value: \" + undefined;");
        Assert.Equal("value: undefined", result);
    }

    [Fact]
    public void TemplateLiteral_NullToString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("`value: ${null}`;");
        Assert.Equal("value: null", result);
    }

    [Fact]
    public void TemplateLiteral_UndefinedToString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("`value: ${undefined}`;");
        Assert.Equal("value: undefined", result);
    }

    [Fact]
    public void NullComparison_WithZero()
    {
        // JavaScript oddity: null >= 0 is true, but null > 0 and null == 0 are false
        var engine = new JsEngine();
        
        var greaterOrEqual = engine.EvaluateSync("null >= 0;");
        Assert.True((bool)greaterOrEqual!);
        
        var greater = engine.EvaluateSync("null > 0;");
        Assert.False((bool)greater!);
        
        var equals = engine.EvaluateSync("null == 0;");
        Assert.False((bool)equals!);
    }

    [Fact]
    public void UndefinedComparison_WithZero()
    {
        // undefined compared with numbers returns false (except for !=)
        var engine = new JsEngine();
        
        var greater = engine.EvaluateSync("undefined > 0;");
        Assert.False((bool)greater!);
        
        var less = engine.EvaluateSync("undefined < 0;");
        Assert.False((bool)less!);
        
        var equals = engine.EvaluateSync("undefined == 0;");
        Assert.False((bool)equals!);
    }

    [Fact]
    public void NullNotEqualToZero()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null != 0;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void UndefinedNotEqualToZero()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined != 0;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void StrictEquality_NullWithNull()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null === null;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void StrictEquality_UndefinedWithUndefined()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined === undefined;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void LooseEquality_NullNotEqualToNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null == 0;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseEquality_UndefinedNotEqualToNumber()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined == 0;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseEquality_NullNotEqualToFalse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null == false;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseEquality_UndefinedNotEqualToFalse()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined == false;");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseEquality_NullNotEqualToEmptyString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("null == \"\";");
        Assert.False((bool)result!);
    }

    [Fact]
    public void LooseEquality_UndefinedNotEqualToEmptyString()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("undefined == \"\";");
        Assert.False((bool)result!);
    }

    [Fact]
    public void TypeofInExpression()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync("typeof undefined === \"undefined\" ? \"correct\" : \"wrong\";");
        Assert.Equal("correct", result);
    }

    [Fact]
    public void UndefinedAsVariableValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = undefined;
            typeof x;
        ");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public void NullAsVariableValue()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let x = null;
            typeof x;
        ");
        Assert.Equal("object", result);
    }

    [Fact]
    public void TypeofInFunction()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            function checkType(value) {
                return typeof value;
            }
            checkType(undefined);
        ");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public void MultipleTypeofChecks()
    {
        var engine = new JsEngine();
        var result = engine.EvaluateSync(@"
            let results = [
                typeof null,
                typeof undefined,
                typeof 42,
                typeof ""hello"",
                typeof true
            ];
            results[0] + "","" + results[1] + "","" + results[2] + "","" + results[3] + "","" + results[4];
        ");
        Assert.Equal("object,undefined,number,string,boolean", result);
    }
}
