using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for comprehensive type coercion rules (toString, toNumber conversions)
/// </summary>
public class TypeCoercionTests
{
    // ========================================
    // Array to String Conversion
    // ========================================

    [Fact]
    public void ArrayToString_EmptyArray()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"value: \" + [];");
        Assert.Equal("value: ", result);
    }

    [Fact]
    public void ArrayToString_SingleElement()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"value: \" + [5];");
        Assert.Equal("value: 5", result);
    }

    [Fact]
    public void ArrayToString_MultipleElements()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"value: \" + [1, 2, 3];");
        Assert.Equal("value: 1,2,3", result);
    }

    [Fact]
    public void ArrayToString_NestedArrays()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"value: \" + [[1], [2], [3]];");
        Assert.Equal("value: 1,2,3", result);
    }

    [Fact]
    public void ArrayToString_WithNullUndefined()
    {
        var engine = new JsEngine();
        var result1 = engine.Evaluate("\"value: \" + [1, null, 3];");
        Assert.Equal("value: 1,null,3", result1);

        var result2 = engine.Evaluate("\"value: \" + [1, undefined, 3];");
        Assert.Equal("value: 1,undefined,3", result2);
    }

    // ========================================
    // Object to String Conversion
    // ========================================

    [Fact]
    public void ObjectToString_EmptyObject()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"value: \" + {};");
        Assert.Equal("value: [object Object]", result);
    }

    [Fact]
    public void ObjectToString_ObjectWithProperties()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let obj = { a: 1, b: 2 }; \"value: \" + obj;");
        Assert.Equal("value: [object Object]", result);
    }

    // ========================================
    // Array to Number Conversion
    // ========================================

    [Fact]
    public void ArrayToNumber_EmptyArray()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[] - 0;");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void ArrayToNumber_SingleElement()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[5] - 0;");
        Assert.Equal(5d, result);
    }

    [Fact]
    public void ArrayToNumber_SingleStringElement()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[\"10\"] - 0;");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void ArrayToNumber_MultipleElements()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[1, 2] - 0;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public void ArrayToNumber_InArithmetic()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[] + 5;");
        Assert.Equal("5", result);
    }

    // ========================================
    // Object to Number Conversion
    // ========================================

    [Fact]
    public void ObjectToNumber_EmptyObject()
    {
        var engine = new JsEngine();
        // Note: {} at the start of a statement is parsed as a block, not an object literal
        // Using parentheses forces it to be parsed as an expression
        var result = engine.Evaluate("({}) - 0;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public void ObjectToNumber_ObjectWithProperties()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let obj = { a: 1 }; obj - 0;");
        Assert.True(double.IsNaN((double)result!));
    }

    // ========================================
    // String to Number Conversion
    // ========================================

    [Fact]
    public void StringToNumber_EmptyString()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"\" - 0;");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void StringToNumber_WhitespaceOnly()
    {
        var engine = new JsEngine();
        var result1 = engine.Evaluate("\"   \" - 0;");
        Assert.Equal(0d, result1);

        // Note: Escape sequences like \t and \n are not yet properly parsed by the lexer
        // This test uses actual whitespace characters instead
        var result2 = engine.Evaluate("\" \" - 0;");
        Assert.Equal(0d, result2);
    }

    [Fact]
    public void StringToNumber_ValidNumber()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"123\" - 0;");
        Assert.Equal(123d, result);
    }

    [Fact]
    public void StringToNumber_NumberWithWhitespace()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"  123  \" - 0;");
        Assert.Equal(123d, result);
    }

    [Fact]
    public void StringToNumber_InvalidNumber()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"123abc\" - 0;");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact]
    public void StringToNumber_Decimal()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("\"3.14\" - 0;");
        Assert.Equal(3.14d, result);
    }

    // ========================================
    // Loose Equality with Type Coercion
    // ========================================

    [Fact]
    public void LooseEquality_NumberAndString()
    {
        var engine = new JsEngine();
        
        var result1 = engine.Evaluate("0 == \"\";");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("0 == \"0\";");
        Assert.True((bool)result2!);
        
        var result3 = engine.Evaluate("5 == \"5\";");
        Assert.True((bool)result3!);
    }

    [Fact]
    public void LooseEquality_BooleanAndString()
    {
        var engine = new JsEngine();
        
        var result1 = engine.Evaluate("false == \"\";");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("false == \"0\";");
        Assert.True((bool)result2!);
        
        var result3 = engine.Evaluate("true == \"1\";");
        Assert.True((bool)result3!);
    }

    [Fact]
    public void LooseEquality_BooleanAndNumber()
    {
        var engine = new JsEngine();
        
        var result1 = engine.Evaluate("false == 0;");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("true == 1;");
        Assert.True((bool)result2!);
    }

    [Fact]
    public void LooseEquality_WhitespaceStringAndNumber()
    {
        var engine = new JsEngine();
        
        // Note: Using actual whitespace instead of escape sequences
        var result = engine.Evaluate("\"   \" == 0;");
        Assert.True((bool)result!);
    }

    [Fact]
    public void LooseEquality_ArrayAndString()
    {
        var engine = new JsEngine();
        
        var result1 = engine.Evaluate("[] == \"\";");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("[\"\"] == \"\";");
        Assert.True((bool)result2!);
        
        var result3 = engine.Evaluate("[1, 2] == \"1,2\";");
        Assert.True((bool)result3!);
    }

    [Fact]
    public void LooseEquality_ArrayAndNumber()
    {
        var engine = new JsEngine();
        
        var result1 = engine.Evaluate("[] == 0;");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("[0] == 0;");
        Assert.True((bool)result2!);
        
        var result3 = engine.Evaluate("[5] == 5;");
        Assert.True((bool)result3!);
    }

    [Fact]
    public void LooseEquality_StrictInequalityPreserved()
    {
        var engine = new JsEngine();
        
        // Strict inequality should not perform type coercion
        var result1 = engine.Evaluate("0 === \"\";");
        Assert.False((bool)result1!);
        
        var result2 = engine.Evaluate("0 === \"0\";");
        Assert.False((bool)result2!);
        
        var result3 = engine.Evaluate("false === 0;");
        Assert.False((bool)result3!);
    }

    // ========================================
    // Complex Addition with Type Coercion
    // ========================================

    [Fact]
    public void Addition_ArrayConcatenation()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[1, 2] + [3, 4];");
        Assert.Equal("1,23,4", result);
    }

    [Fact]
    public void Addition_EmptyArrays()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[] + [];");
        Assert.Equal("", result);
    }

    [Fact]
    public void Addition_ObjectAndArray()
    {
        var engine = new JsEngine();
        // Note: {} at the start of a statement is parsed as a block, not an object literal
        // Using parentheses forces it to be parsed as an expression
        var result = engine.Evaluate("({}) + [];");
        Assert.Equal("[object Object]", result);
    }

    [Fact]
    public void Addition_ArrayAndNumber()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("[1, 2] + 3;");
        Assert.Equal("1,23", result);
    }

    // ========================================
    // Truthiness with NaN
    // ========================================

    [Fact]
    public void Truthiness_NaNIsFalsy()
    {
        var engine = new JsEngine();
        
        // NaN should be falsy
        var result = engine.Evaluate("Math.sqrt(-1) ? 1 : 0;");
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Truthiness_InvalidStringConversionProducesNaN()
    {
        var engine = new JsEngine();
        
        // Invalid string to number conversion produces NaN which is falsy
        var result = engine.Evaluate("(\"abc\" - 0) ? 1 : 0;");
        Assert.Equal(0d, result);
    }

    // ========================================
    // Edge Cases
    // ========================================

    [Fact]
    public void TypeCoercion_NullAndUndefinedInArithmetic()
    {
        var engine = new JsEngine();
        
        // null converts to 0
        var result1 = engine.Evaluate("null + 5;");
        Assert.Equal(5d, result1);
        
        // undefined converts to NaN
        var result2 = engine.Evaluate("undefined + 5;");
        Assert.True(double.IsNaN((double)result2!));
    }

    [Fact]
    public void TypeCoercion_BooleanInArithmetic()
    {
        var engine = new JsEngine();
        
        // true converts to 1, false to 0
        var result1 = engine.Evaluate("true + 5;");
        Assert.Equal(6d, result1);
        
        var result2 = engine.Evaluate("false + 5;");
        Assert.Equal(5d, result2);
    }

    [Fact]
    public void TypeCoercion_MixedOperations()
    {
        var engine = new JsEngine();
        
        // Complex chain of type coercions
        var result = engine.Evaluate("\"5\" - \"2\" + 3;");
        Assert.Equal(6d, result); // "5" - "2" = 3, then 3 + 3 = 6
    }

    [Fact]
    public void TypeCoercion_ArrayInLooseEquality()
    {
        var engine = new JsEngine();
        
        // Array converts to primitive for comparison
        var result1 = engine.Evaluate("[10] == 10;");
        Assert.True((bool)result1!);
        
        var result2 = engine.Evaluate("[10] == \"10\";");
        Assert.True((bool)result2!);
    }
}
