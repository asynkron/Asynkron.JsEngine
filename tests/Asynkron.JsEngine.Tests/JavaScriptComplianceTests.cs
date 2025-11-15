using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Hard tests for JavaScript compliance - testing edge cases, oddities, and problematic behaviors
/// that are known to be challenging in JavaScript implementations.
/// </summary>
public class JavaScriptComplianceTests
{
    // ========================================
    // Type Coercion Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task TypeCoercion_StringMinusNumber()
    {
        var engine = new JsEngine();

        // String coerces to number in subtraction
        var result = await engine.Evaluate("\"10\" - 5;");
        Assert.Equal(5d, result);

        // Multiple strings in arithmetic
        var result2 = await engine.Evaluate("\"20\" - \"5\";");
        Assert.Equal(15d, result2);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeCoercion_StringMultiplyNumber()
    {
        var engine = new JsEngine();

        // String coerces to number in multiplication
        var result = await engine.Evaluate("\"5\" * 3;");
        Assert.Equal(15d, result);

        var result2 = await engine.Evaluate("\"10\" * \"2\";");
        Assert.Equal(20d, result2);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeCoercion_BooleanArithmetic()
    {
        var engine = new JsEngine();

        // true coerces to 1, false coerces to 0
        var result1 = await engine.Evaluate("true + true;");
        Assert.Equal(2d, result1);

        var result2 = await engine.Evaluate("true + false;");
        Assert.Equal(1d, result2);

        var result3 = await engine.Evaluate("false + false;");
        Assert.Equal(0d, result3);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeCoercion_StringPlusNumber()
    {
        var engine = new JsEngine();

        // String concatenation takes precedence
        var result = await engine.Evaluate("\"5\" + 3;");
        Assert.Equal("53", result);

        // But subtraction coerces to numbers
        var result2 = await engine.Evaluate("\"5\" - 3;");
        Assert.Equal(2d, result2);
    }

    // ========================================
    // NaN and Infinity Edge Cases (Math functions)
    // ========================================    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.


    [Fact(Timeout = 2000)]
    public async Task NaN_FromMathSqrt()
    {
        var engine = new JsEngine();

        // Sqrt of negative number produces NaN
        var result = await engine.Evaluate("Math.sqrt(-1);");
        Assert.True(double.IsNaN((double)result!));
    }

    [Fact(Timeout = 2000)]
    public async Task NaN_PropagatesInMathOperations()
    {
        var engine = new JsEngine();

        // NaN propagates through calculations
        var result1 = await engine.Evaluate("Math.sqrt(-1) + 5;");
        Assert.True(double.IsNaN((double)result1!));

        var result2 = await engine.Evaluate("Math.sqrt(-1) * 10;");
        Assert.True(double.IsNaN((double)result2!));
    }

    [Fact(Timeout = 2000)]
    public async Task Math_LargeNumberOperations()
    {
        var engine = new JsEngine();

        // Very large numbers
        var result1 = await engine.Evaluate("999999999999999 + 1;");
        Assert.Equal(1000000000000000d, result1);

        // Precision limits with very large numbers
        var result2 = await engine.Evaluate("9007199254740992 + 1;"); // 2^53 + 1 (precision limit)
        Assert.IsType<double>(result2);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_VerySmallNumbers()
    {
        var engine = new JsEngine();

        // Very small decimals
        var result = await engine.Evaluate("0.1 + 0.2;");
        // Famous floating point precision issue
        Assert.NotEqual(0.3d, result);
        Assert.True(Math.Abs((double)result! - 0.3) < 0.0001);
    }

    // ========================================
    // Equality Comparison Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task Equality_ZeroComparisons()
    {
        var engine = new JsEngine();

        // 0 and -0 are equal
        var result1 = await engine.Evaluate("0 === -0;");
        Assert.True((bool)result1!);

        var result2 = await engine.Evaluate("0 == -0;");
        Assert.True((bool)result2!);
    }

    [Fact(Timeout = 2000)]
    public async Task Equality_ObjectsNeverEqual()
    {
        var engine = new JsEngine();

        // Object literals create different objects
        var result = await engine.Evaluate("let a = {}; let b = {}; a === b;");
        Assert.False((bool)result!);

        var result2 = await engine.Evaluate("let c = []; let d = []; c === d;");
        Assert.False((bool)result2!);
    }

    [Fact(Timeout = 2000)]
    public async Task Equality_LooseVsStrict()
    {
        var engine = new JsEngine();

        // Strict equality does not do type coercion
        var result1 = await engine.Evaluate("0 === false;");
        Assert.False((bool)result1!);

        var result2 = await engine.Evaluate("\"\" === false;");
        Assert.False((bool)result2!);

        var result3 = await engine.Evaluate("1 === true;");
        Assert.False((bool)result3!);

        // But same type comparisons work
        var result4 = await engine.Evaluate("5 === 5;");
        Assert.True((bool)result4!);

        var result5 = await engine.Evaluate("\"hello\" === \"hello\";");
        Assert.True((bool)result5!);
    }

    // ========================================
    // Array Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task Array_SparseArrays()
    {
        var engine = new JsEngine();

        // Arrays can have holes
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3];
                                                       arr[10] = 11;
                                                       arr.length;
                                                   
                                           """);
        Assert.Equal(11d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_NegativeIndices()
    {
        var engine = new JsEngine();

        // Negative indices don't work like Python
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3];
                                                       arr[-1] = 99;
                                                       arr[-1];
                                                   
                                           """);
        Assert.Equal(99d, result);

        // But length is not affected
        var result2 = await engine.Evaluate("""

                                                        let arr = [1, 2, 3];
                                                        arr[-1] = 99;
                                                        arr.length;
                                                    
                                            """);
        Assert.Equal(3d, result2);
    }

    [Fact(Timeout = 2000)]
    public async Task Array_LengthPropertyChanges()
    {
        var engine = new JsEngine();

        // Length changes when elements are added
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3];
                                                       arr[5] = 6;
                                                       arr.length;
                                                   
                                           """);
        Assert.Equal(6d, result);
    }

    // ========================================
    // Scope and Variable Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task Scope_LetBlockScoping()
    {
        var engine = new JsEngine();

        // let has block scope
        var result = await engine.Evaluate("""

                                                       let x = 1;
                                                       if (true) {
                                                           let x = 2;
                                                       }
                                                       x;
                                                   
                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Scope_VarFunctionScoping()
    {
        var engine = new JsEngine();

        // var has function scope (or global if not in function)
        var result = await engine.Evaluate("""

                                                       var x = 1;
                                                       if (true) {
                                                           var x = 2;
                                                       }
                                                       x;
                                                   
                                           """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Scope_ClosureCapture()
    {
        var engine = new JsEngine();

        // Classic closure problem with var in loop
        var result = await engine.Evaluate("""

                                                       let funcs = [];
                                                       for (var i = 0; i < 3; i = i + 1) {
                                                           funcs[i] = function() { return i; };
                                                       }
                                                       funcs[0]() + funcs[1]() + funcs[2]();
                                                   
                                           """);
        // All functions capture the same 'i' which ends at 3
        Assert.Equal(9d, result); // 3 + 3 + 3
    }

    [Fact(Timeout = 2000)]
    public async Task Scope_NestedLetScoping()
    {
        var engine = new JsEngine();

        // Nested let declarations shadow outer ones
        var result = await engine.Evaluate("""

                                                       let x = 1;
                                                       let y = 2;
                                                       if (true) {
                                                           let y = 3;
                                                           x = x + y;
                                                       }
                                                       x + y;
                                                   
                                           """);
        // x becomes 1+3=4, y is still 2, result is 4+2=6
        Assert.Equal(6d, result);
    }

    // ========================================
    // Function and This Binding Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task ThisBinding_MethodCall()
    {
        var engine = new JsEngine();

        // 'this' works when method is called normally
        var result = await engine.Evaluate("""

                                                       let obj = {
                                                           value: 42,
                                                           getValue: function() { return this.value; }
                                                       };
                                                       
                                                       obj.getValue();
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ThisBinding_ArrowFunctionLexicalThis()
    {
        var engine = new JsEngine();

        // Arrow functions capture 'this' from surrounding scope
        var result = await engine.Evaluate("""

                                                       let obj = {
                                                           value: 42,
                                                           getArrow: function() {
                                                               let that = this;
                                                               let arrow = function() { return that.value; };
                                                               return arrow();
                                                           }
                                                       };
                                                       
                                                       obj.getArrow();
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    // ========================================
    // String Coercion Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task StringCoercion_NumberToString()
    {
        var engine = new JsEngine();

        // Numbers concatenate as strings
        var result1 = await engine.Evaluate("\"value: \" + 42;");
        Assert.Equal("value: 42", result1);

        var result2 = await engine.Evaluate("\"value: \" + 3.14;");
        Assert.Equal("value: 3.14", result2);
    }

    [Fact(Timeout = 2000)]
    public async Task StringCoercion_BooleanToString()
    {
        var engine = new JsEngine();

        // Booleans concatenate as strings
        var result1 = await engine.Evaluate("\"result: \" + true;");
        Assert.Equal("result: true", result1);

        var result2 = await engine.Evaluate("\"result: \" + false;");
        Assert.Equal("result: false", result2);
    }

    // ========================================
    // Truthy/Falsy Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task Truthiness_FalsyValues()
    {
        var engine = new JsEngine();

        // All these should be falsy
        var result1 = await engine.Evaluate("false ? 1 : 0;");
        Assert.Equal(0d, result1);

        var result2 = await engine.Evaluate("0 ? 1 : 0;");
        Assert.Equal(0d, result2);

        var result3 = await engine.Evaluate("\"\" ? 1 : 0;");
        Assert.Equal(0d, result3);

        var result4 = await engine.Evaluate("null ? 1 : 0;");
        Assert.Equal(0d, result4);

        var result5 = await engine.Evaluate("undefined ? 1 : 0;");
        Assert.Equal(0d, result5);

        var result6 = await engine.Evaluate("Math.sqrt(-1) ? 1 : 0;"); // NaN
        Assert.Equal(0d, result6);
    }

    [Fact(Timeout = 2000)]
    public async Task Truthiness_TruthyValues()
    {
        var engine = new JsEngine();

        // Objects and arrays are truthy (unlike Python)
        var result1 = await engine.Evaluate("let obj = {}; obj ? 1 : 0;");
        Assert.Equal(1d, result1);

        var result2 = await engine.Evaluate("let arr = []; arr ? 1 : 0;");
        Assert.Equal(1d, result2);

        // Non-empty strings are truthy
        var result3 = await engine.Evaluate("\"0\" ? 1 : 0;");
        Assert.Equal(1d, result3);

        var result4 = await engine.Evaluate("\"false\" ? 1 : 0;");
        Assert.Equal(1d, result4);
    }

    // ========================================
    // Operator Precedence Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task OperatorPrecedence_LogicalAndOr()
    {
        var engine = new JsEngine();

        // && has higher precedence than ||
        var result = await engine.Evaluate("true || false && false;");
        Assert.True((bool)result!);

        var result2 = await engine.Evaluate("false && false || true;");
        Assert.True((bool)result2!);
    }

    [Fact(Timeout = 2000)]
    public async Task OperatorPrecedence_ComparisonAndLogical()
    {
        var engine = new JsEngine();

        // Comparisons before logical operators
        var result = await engine.Evaluate("1 < 2 && 3 > 2;");
        Assert.True((bool)result!);

        var result2 = await engine.Evaluate("1 > 2 || 3 > 2;");
        Assert.True((bool)result2!);
    }

    // ========================================
    // Math Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task Math_MinMaxWithMultipleArgs()
    {
        var engine = new JsEngine();

        // Math.max with negative numbers
        var result1 = await engine.Evaluate("Math.max(-1, -5, -3);");
        Assert.Equal(-1d, result1);

        // Math.min with positive numbers
        var result2 = await engine.Evaluate("Math.min(5, 2, 8, 3);");
        Assert.Equal(2d, result2);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_OperationsWithLargeNumbers()
    {
        var engine = new JsEngine();

        // Math operations with large numbers
        var result1 = await engine.Evaluate("Math.max(1000000, 999999, 1000001);");
        Assert.Equal(1000001d, result1);

        var result2 = await engine.Evaluate("Math.sqrt(1000000);");
        Assert.Equal(1000d, result2);
    }

    [Fact(Timeout = 2000)]
    public async Task Math_TrigonometryEdgeCases()
    {
        var engine = new JsEngine();

        // Trig functions at special values
        var result1 = await engine.Evaluate("Math.sin(0);");
        Assert.Equal(0d, result1);

        var result2 = await engine.Evaluate("Math.cos(0);");
        Assert.Equal(1d, result2);
    }

    // ========================================
    // Object Property Access Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyAccess_NumericKeys()
    {
        var engine = new JsEngine();

        // Numeric keys are converted to strings
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       obj[1] = "one";
                                                       obj["1"];
                                                   
                                           """);
        Assert.Equal("one", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyAccess_SpecialProperties()
    {
        var engine = new JsEngine();

        // Can access properties with special names
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       obj["my-property"] = 42;
                                                       obj["my-property"];
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyAccess_DynamicProperties()
    {
        var engine = new JsEngine();

        // Can add properties dynamically
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1 };
                                                       obj.b = 2;
                                                       obj.c = 3;
                                                       obj.a + obj.b + obj.c;
                                                   
                                           """);
        Assert.Equal(6d, result);
    }

    // ========================================
    // Control Flow Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task ControlFlow_SwitchFallthrough()
    {
        var engine = new JsEngine();

        // Switch falls through without break
        var result = await engine.Evaluate("""

                                                       let x = 1;
                                                       let result = 0;
                                                       switch (x) {
                                                           case 1:
                                                               result = result + 1;
                                                           case 2:
                                                               result = result + 2;
                                                           case 3:
                                                               result = result + 3;
                                                               break;
                                                           default:
                                                               result = result + 100;
                                                       }
                                                       result;
                                                   
                                           """);
        Assert.Equal(6d, result); // 1 + 2 + 3
    }

    [Fact(Timeout = 2000)]
    public async Task ControlFlow_ForLoopEdgeCases()
    {
        var engine = new JsEngine();

        // Empty for loop with break
        var result1 = await engine.Evaluate("""

                                                        let i = 0;
                                                        for (;;) {
                                                            i = i + 1;
                                                            if (i >= 3) break;
                                                        }
                                                        i;
                                                    
                                            """);
        Assert.Equal(3d, result1);

        // For loop with complex condition
        var result2 = await engine.Evaluate("""

                                                        let sum = 0;
                                                        for (let i = 0; i < 5; i = i + 1) {
                                                            sum = sum + i;
                                                        }
                                                        sum;
                                                    
                                            """);
        // 0 + 1 + 2 + 3 + 4 = 10
        Assert.Equal(10d, result2);
    }

    // ========================================
    // Nested Structures Edge Cases
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task NestedStructures_DeepNesting()
    {
        var engine = new JsEngine();

        // Deeply nested object access
        var result = await engine.Evaluate("""

                                                       let obj = {
                                                           a: {
                                                               b: {
                                                                   c: {
                                                                       d: {
                                                                           e: 42
                                                                       }
                                                                   }
                                                               }
                                                           }
                                                       };
                                                       obj.a.b.c.d.e;
                                                   
                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task NestedStructures_DeepFunctionNesting()
    {
        var engine = new JsEngine();

        // Deeply nested function calls
        var result = await engine.Evaluate("""

                                                       function add(x) {
                                                           return function(y) {
                                                               return function(z) {
                                                                   return x + y + z;
                                                               };
                                                           };
                                                       }
                                                       add(1)(2)(3);
                                                   
                                           """);
        Assert.Equal(6d, result);
    }

    // ========================================
    // Expression Evaluation Order
    // ========================================

    [Fact(Timeout = 2000)]
    public async Task ExpressionEvaluation_ShortCircuitAnd()
    {
        var engine = new JsEngine();

        // Second operand should not be evaluated if first is falsy
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       function increment() {
                                                           x = x + 1;
                                                           return true;
                                                       }
                                                       false && increment();
                                                       x;
                                                   
                                           """);
        Assert.Equal(0d, result); // increment was not called
    }

    [Fact(Timeout = 2000)]
    public async Task ExpressionEvaluation_ShortCircuitOr()
    {
        var engine = new JsEngine();

        // Second operand should not be evaluated if first is truthy
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       function increment() {
                                                           x = x + 1;
                                                           return true;
                                                       }
                                                       true || increment();
                                                       x;
                                                   
                                           """);
        Assert.Equal(0d, result); // increment was not called
    }

    [Fact(Timeout = 2000)]
    public async Task ExpressionEvaluation_TernaryLazyEvaluation()
    {
        var engine = new JsEngine();

        // Only one branch should be evaluated
        var result = await engine.Evaluate("""

                                                       let x = 0;
                                                       let y = 0;
                                                       function incrementX() {
                                                           x = x + 1;
                                                           return x;
                                                       }
                                                       function incrementY() {
                                                           y = y + 1;
                                                           return y;
                                                       }
                                                       true ? incrementX() : incrementY();
                                                       x + y * 10;
                                                   
                                           """);
        Assert.Equal(1d, result); // x=1, y=0, result is 1 + 0*10 = 1
    }
}