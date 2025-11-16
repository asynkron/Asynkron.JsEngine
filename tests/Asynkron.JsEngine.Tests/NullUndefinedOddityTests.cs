namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for JavaScript oddities related to null and undefined values.
/// These tests ensure the engine correctly implements JavaScript's quirky behavior with these values.
/// </summary>
public class NullUndefinedOddityTests
{
    [Fact(Timeout = 2000)]
    public async Task TypeofNull_ReturnsObject()
    {
        // JavaScript oddity: typeof null === "object" (historical bug)
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof null;");
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofUndefined_ReturnsUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undefined;");
        Assert.Equal("undefined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofNumber_ReturnsNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 42;");
        Assert.Equal("number", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofString_ReturnsString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof \"hello\";");
        Assert.Equal("string", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofBoolean_ReturnsBoolean()
    {
        await using var engine = new JsEngine();
        var trueResult = await engine.Evaluate("typeof true;");
        var falseResult = await engine.Evaluate("typeof false;");
        Assert.Equal("boolean", trueResult);
        Assert.Equal("boolean", falseResult);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofFunction_ReturnsFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof function() { return 1; };");
        Assert.Equal("function", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofObject_ReturnsObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof { a: 1 };");
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofArray_ReturnsObject()
    {
        // Arrays are objects in JavaScript
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof [1, 2, 3];");
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_NullEqualsUndefined()
    {
        // JavaScript oddity: null == undefined (with loose equality)
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null == undefined;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictEquality_NullNotEqualsUndefined()
    {
        // But null !== undefined (with strict equality)
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null === undefined;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseInequality_NullNotNotEqualUndefined()
    {
        // null != undefined should be false
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null != undefined;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictInequality_NullNotEqualUndefined()
    {
        // null !== undefined should be true
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null !== undefined;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Null_IsFalsy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Undefined_IsFalsy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NotNull_IsTrue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("!null;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task NotUndefined_IsTrue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("!undefined;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task NullishCoalescing_NullReturnsDefault()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullishCoalescing_UndefinedReturnsDefault()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined ?? \"default\";");
        Assert.Equal("default", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalOr_NullReturnsRightOperand()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalOr_UndefinedReturnsRightOperand()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined || \"default\";");
        Assert.Equal("default", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullPlusNumber_ReturnsNumber()
    {
        // null coerces to 0 in arithmetic
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null + 1;");
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UndefinedPlusNumber_ReturnsNaN()
    {
        // undefined coerces to NaN in arithmetic
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined + 1;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task NullMultipliedByNumber_ReturnsZero()
    {
        // null coerces to 0
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null * 5;");
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task UndefinedMultipliedByNumber_ReturnsNaN()
    {
        // undefined coerces to NaN
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined * 5;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task StringConcatenation_NullToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("\"value: \" + null;");
        Assert.Equal("value: null", result);
    }

    [Fact(Timeout = 2000)]
    public async Task StringConcatenation_UndefinedToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("\"value: \" + undefined;");
        Assert.Equal("value: undefined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteral_NullToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("`value: ${null}`;");
        Assert.Equal("value: null", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteral_UndefinedToString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("`value: ${undefined}`;");
        Assert.Equal("value: undefined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullComparison_WithZero()
    {
        // JavaScript oddity: null >= 0 is true, but null > 0 and null == 0 are false
        await using var engine = new JsEngine();

        var greaterOrEqual = await engine.Evaluate("null >= 0;");
        Assert.True((bool)greaterOrEqual!);

        var greater = await engine.Evaluate("null > 0;");
        Assert.False((bool)greater!);

        var equals = await engine.Evaluate("null == 0;");
        Assert.False((bool)equals!);
    }

    [Fact(Timeout = 2000)]
    public async Task UndefinedComparison_WithZero()
    {
        // undefined compared with numbers returns false (except for !=)
        await using var engine = new JsEngine();

        var greater = await engine.Evaluate("undefined > 0;");
        Assert.False((bool)greater!);

        var less = await engine.Evaluate("undefined < 0;");
        Assert.False((bool)less!);

        var equals = await engine.Evaluate("undefined == 0;");
        Assert.False((bool)equals!);
    }

    [Fact(Timeout = 2000)]
    public async Task NullNotEqualToZero()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null != 0;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task UndefinedNotEqualToZero()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined != 0;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task StrictEquality_NullWithNull()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null === null;");
        Assert.True((bool)result!);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task StrictEquality_UndefinedWithUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined === undefined;");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_NullNotEqualToNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null == 0;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_UndefinedNotEqualToNumber()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == 0;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_NullNotEqualToFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null == false;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_UndefinedNotEqualToFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == false;");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_NullNotEqualToEmptyString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null == \"\";");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task LooseEquality_UndefinedNotEqualToEmptyString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined == \"\";");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofInExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undefined === \"undefined\" ? \"correct\" : \"wrong\";");
        Assert.Equal("correct", result);
    }

    [Fact(Timeout = 2000)]
    public async Task UndefinedAsVariableValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = undefined;
                                                       typeof x;

                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NullAsVariableValue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = null;
                                                       typeof x;

                                           """);
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeofInFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function checkType(value) {
                                                           return typeof value;
                                                       }
                                                       checkType(undefined);

                                           """);
        Assert.Equal("undefined", result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleTypeofChecks()
    {
        await using var engine = new JsEngine();
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
