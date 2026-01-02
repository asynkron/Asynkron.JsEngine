using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Foundation tests covering basic JavaScript evaluation.
/// These are simple sanity checks for core language features.
/// </summary>
public class FoundationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    #region Literals

    [Fact]
    public async Task Literal_Integer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Literal_Float()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("3.14");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public async Task Literal_String()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Literal_StringDoubleQuotes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("\"world\"");
        Assert.Equal("world", result);
    }

    [Fact]
    public async Task Literal_True()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Literal_False()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("false");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task Literal_Null()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("null");
        Assert.Null(result);
    }

    [Fact]
    public async Task Literal_Undefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("undefined");
        Assert.Equal(Symbol.Undefined, result);
    }

    #endregion

    #region Arithmetic Operators

    [Fact]
    public async Task Operator_Add()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 + 2");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Operator_Subtract()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 - 3");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Operator_Multiply()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("4 * 3");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Operator_Divide()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10 / 2");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Operator_Modulo()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10 % 3");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Operator_Exponent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 ** 3");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task Operator_UnaryMinus()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("-5");
        Assert.Equal(-5d, result);
    }

    [Fact]
    public async Task Operator_UnaryPlus()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("+'42'");
        Assert.Equal(42d, result);
    }

    #endregion

    #region Comparison Operators

    [Fact]
    public async Task Operator_Equal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 == 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_StrictEqual()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 === 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_NotEqual()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 != 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_StrictNotEqual()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 !== '1'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LessThan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("1 < 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_GreaterThan()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 > 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LessThanOrEqual()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 <= 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_GreaterThanOrEqual()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("3 >= 2");
        Assert.Equal(true, result);
    }

    #endregion

    #region Logical Operators

    [Fact]
    public async Task Operator_LogicalAnd()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("true && true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LogicalOr()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("false || true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LogicalNot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("!false");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_NullishCoalescing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("null ?? 'default'");
        Assert.Equal("default", result);
    }

    #endregion

    #region Bitwise Operators

    [Fact]
    public async Task Operator_BitwiseAnd()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 & 3");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Operator_BitwiseOr()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 | 3");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Operator_BitwiseXor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("5 ^ 3");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Operator_BitwiseNot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("~5");
        Assert.Equal(-6d, result);
    }

    [Fact]
    public async Task Operator_LeftShift()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("2 << 3");
        Assert.Equal(16d, result);
    }

    [Fact]
    public async Task Operator_RightShift()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("16 >> 2");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Operator_UnsignedRightShift()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("-1 >>> 0");
        Assert.Equal(4294967295d, result);
    }

    #endregion

    #region String Operations

    [Fact]
    public async Task String_Concatenation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello' + ' ' + 'world'");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task String_Length()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.length");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task String_CharAt()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.charAt(1)");
        Assert.Equal("e", result);
    }

    [Fact]
    public async Task String_ToUpperCase()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.toUpperCase()");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task String_TemplateLiteral()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("`1 + 2 = ${1 + 2}`");
        Assert.Equal("1 + 2 = 3", result);
    }

    #endregion

    #region Variables

    [Fact]
    public async Task Variable_Var()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var x = 10; x");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Variable_Let()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 20; x");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Variable_Const()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const x = 30; x");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task Variable_Assignment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 1; x = 5; x");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Variable_CompoundAssignment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 10; x += 5; x");
        Assert.Equal(15d, result);
    }

    #endregion

    #region Conditionals

    [Fact]
    public async Task Conditional_IfTrue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 0; if (true) { x = 1; } x");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Conditional_IfFalse()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 0; if (false) { x = 1; } x");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Conditional_IfElse()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let x = 0; if (false) { x = 1; } else { x = 2; } x");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Conditional_Ternary()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("true ? 'yes' : 'no'");
        Assert.Equal("yes", result);
    }

    [Fact]
    public async Task Conditional_Switch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 2;
            let result;
            switch (x) {
                case 1: result = 'one'; break;
                case 2: result = 'two'; break;
                default: result = 'other';
            }
            result
        ");
        Assert.Equal("two", result);
    }

    #endregion

    #region Loops

    [Fact]
    public async Task Loop_For()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let sum = 0; for (let i = 1; i <= 5; i++) { sum += i; } sum");
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task Loop_While()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let i = 0; while (i < 5) { i++; } i");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Loop_DoWhile()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let i = 0; do { i++; } while (i < 3); i");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Loop_ForOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let sum = 0; for (let x of [1, 2, 3]) { sum += x; } sum");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Loop_ForIn()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let keys = ''; for (let k in {a: 1, b: 2}) { keys += k; } keys");
        Assert.Equal("ab", result);
    }

    [Fact]
    public async Task Loop_Break()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let i = 0; while (true) { i++; if (i === 3) break; } i");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Loop_Continue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let sum = 0; for (let i = 1; i <= 5; i++) { if (i === 3) continue; sum += i; } sum");
        Assert.Equal(12d, result); // 1+2+4+5 = 12
    }

    #endregion

    #region Functions

    [Fact]
    public async Task Function_Declaration()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function add(a, b) { return a + b; } add(2, 3)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Function_Expression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const add = function(a, b) { return a + b; }; add(3, 4)");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Function_Arrow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const add = (a, b) => a + b; add(4, 5)");
        Assert.Equal(9d, result);
    }

    [Fact]
    public async Task Function_ArrowWithBlock()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const add = (a, b) => { return a + b; }; add(5, 6)");
        Assert.Equal(11d, result);
    }

    [Fact]
    public async Task Function_DefaultParameters()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function greet(name = 'World') { return 'Hello, ' + name; } greet()");
        Assert.Equal("Hello, World", result);
    }

    [Fact]
    public async Task Function_RestParameters()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function sum(...nums) { return nums.reduce((a, b) => a + b, 0); } sum(1, 2, 3, 4)");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Function_Recursion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function factorial(n) { return n <= 1 ? 1 : n * factorial(n - 1); } factorial(5)");
        Assert.Equal(120d, result);
    }

    [Fact]
    public async Task Function_RecursiveAnonymous_ViaOuterVariable()
    {
        // Anonymous function expression that calls itself via the outer variable name
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            'use strict';
            var __func = function (arg){
                if (arg === 1) {
                    return arg;
                } else {
                    return __func(arg-1)*arg;
                }
            };
            __func(3);
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Function_RecursiveNamed_ViaInternalName()
    {
        // Named function expression that calls itself via its internal name
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var f = function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            };
            f(5);
        ");
        Assert.Equal(120d, result);
    }

    [Fact]
    public async Task Function_Closure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function makeCounter() {
                let count = 0;
                return function() { return ++count; };
            }
            const counter = makeCounter();
            counter(); counter(); counter()
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Function_ArgumentsWithDestructuringDefault()
    {
        // Test that arguments object is correctly created in functions
        // with destructuring parameters that use default expressions.
        // Per ES2024 9.2.12 steps 17-20, arguments object must be created when
        // hasParameterExpressions is true, even if 'arguments' is in bodyLexicalNames.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var fn, fnParam;
            (function ({test262 = fnParam = arguments}) {
                fn = arguments;
            })('test arg');

            JSON.stringify({
                fnLength: fn ? fn.length : 'null',
                fnFirst: fn && fn[0] ? fn[0] : 'null',
                fnParamLength: fnParam ? fnParam.length : 'null',
                fnParamFirst: fnParam && fnParam[0] ? fnParam[0] : 'null'
            });
        ");
        Assert.Equal("{\"fnLength\":1,\"fnFirst\":\"test arg\",\"fnParamLength\":1,\"fnParamFirst\":\"test arg\"}", result);
    }

    [Fact]
    public async Task Function_ArgumentsMappedNonStrict()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function foo(a, b, c) {
                arguments[0] = 1;
                arguments[1] = 'str';
                arguments[2] = 2.1;
                return a === 1 && b === 'str' && c === 2.1;
            }
            foo(10, 'sss', 1);
        ");
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Function_IIFE()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("(function() { return 42; })()");
        Assert.Equal(42d, result);
    }

    #endregion

    #region Arrays

    [Fact]
    public async Task Array_Literal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3].length");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Array_Access()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[10, 20, 30][1]");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Array_Push()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("let arr = [1]; arr.push(2); arr.length");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Array_Pop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3].pop()");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Array_Map()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3].map(x => x * 2).join(',')");
        Assert.Equal("2,4,6", result);
    }

    [Fact]
    public async Task Array_Filter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3, 4, 5].filter(x => x % 2 === 0).join(',')");
        Assert.Equal("2,4", result);
    }

    [Fact]
    public async Task Array_Reduce()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3, 4].reduce((acc, x) => acc + x, 0)");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Array_Spread()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[...[1, 2], ...[3, 4]].join(',')");
        Assert.Equal("1,2,3,4", result);
    }

    [Fact]
    public async Task Array_Destructuring()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const [a, b, c] = [1, 2, 3]; a + b + c");
        Assert.Equal(6d, result);
    }

    #endregion

    #region Objects

    [Fact]
    public async Task Object_Literal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("({a: 1, b: 2}).a");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Object_DotAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const obj = {x: 10}; obj.x");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Object_BracketAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const obj = {x: 20}; obj['x']");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Object_ComputedProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const key = 'foo'; const obj = {[key]: 42}; obj.foo");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Object_Shorthand()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const x = 1; const y = 2; const obj = {x, y}; obj.x + obj.y");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Object_Method()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const obj = { getValue() { return 99; } }; obj.getValue()");
        Assert.Equal(99d, result);
    }

    [Fact]
    public async Task Object_Spread()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const obj = {...{a: 1}, ...{b: 2}}; obj.a + obj.b");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Object_Destructuring()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const {a, b} = {a: 10, b: 20}; a + b");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task Object_This()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const obj = { x: 5, getX() { return this.x; } }; obj.getX()");
        Assert.Equal(5d, result);
    }

    #endregion

    #region Classes

    [Fact]
    public async Task Class_Constructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Point {
                constructor(x, y) {
                    this.x = x;
                    this.y = y;
                }
            }
            const p = new Point(3, 4);
            p.x + p.y
        ");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Class_Method()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Calculator {
                add(a, b) { return a + b; }
            }
            new Calculator().add(5, 7)
        ");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Class_Inheritance()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Animal {
                speak() { return 'sound'; }
            }
            class Dog extends Animal {
                speak() { return 'woof'; }
            }
            new Dog().speak()
        ");
        Assert.Equal("woof", result);
    }

    [Fact]
    public async Task Class_Super()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class A {
                getValue() { return 10; }
            }
            class B extends A {
                getValue() { return super.getValue() + 5; }
            }
            new B().getValue()
        ");
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task Class_Static()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class MathUtil {
                static double(x) { return x * 2; }
            }
            MathUtil.double(21)
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Class_Getter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Circle {
                constructor(r) { this.radius = r; }
                get diameter() { return this.radius * 2; }
            }
            new Circle(5).diameter
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Class_Setter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Box {
                constructor() { this._value = 0; }
                set value(v) { this._value = v * 2; }
                get value() { return this._value; }
            }
            const b = new Box();
            b.value = 5;
            b.value
        ");
        Assert.Equal(10d, result);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task TryCatch_CatchesError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let caught = false;
            try {
                throw new Error('test');
            } catch (e) {
                caught = true;
            }
            caught
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task TryCatch_Finally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let finallyRan = false;
            try {
                throw new Error('test');
            } catch (e) {
            } finally {
                finallyRan = true;
            }
            finallyRan
        ");
        Assert.Equal(true, result);
    }

    #endregion

    #region Typeof and Instanceof

    [Fact]
    public async Task Typeof_Number()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof 42");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task Typeof_String()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof 'hello'");
        Assert.Equal("string", result);
    }

    [Fact]
    public async Task Typeof_Boolean()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof true");
        Assert.Equal("boolean", result);
    }

    [Fact]
    public async Task Typeof_Object()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof {}");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task Typeof_Function()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof function() {}");
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task Typeof_Undefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof undefined");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task Instanceof_Array()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[] instanceof Array");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Instanceof_Object()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("({}) instanceof Object");
        Assert.Equal(true, result);
    }

    #endregion

    #region Math Object

    [Fact]
    public async Task Math_Abs()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.abs(-5)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Math_Floor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.floor(3.7)");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Math_Ceil()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.ceil(3.2)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Round()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.round(3.5)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Max()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.max(1, 5, 3)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Math_Min()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.min(1, 5, 3)");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Math_Sqrt()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.sqrt(16)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Pow()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Math.pow(2, 10)");
        Assert.Equal(1024d, result);
    }

    #endregion

    #region JSON

    [Fact]
    public async Task JSON_Stringify()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("JSON.stringify({a: 1, b: 2})");
        Assert.Equal("{\"a\":1,\"b\":2}", result);
    }

    [Fact]
    public async Task JSON_Parse()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("JSON.parse('{\"x\":10}').x");
        Assert.Equal(10d, result);
    }

    #endregion

    #region Async/Await

    [Fact]
    public async Task Async_SimpleReturn()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate("async function test() { return 42; } test().then(capture)");
        Assert.Equal("42", captured);
    }

    [Fact]
    public async Task Async_AwaitPromise()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate(@"
            async function test() {
                const result = await Promise.resolve(100);
                return result;
            }
            test().then(capture)
        ");
        Assert.Equal("100", captured);
    }

    [Fact]
    public async Task Async_AwaitMultiple()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate(@"
            async function test() {
                const a = await Promise.resolve(10);
                const b = await Promise.resolve(20);
                return a + b;
            }
            test().then(capture)
        ");
        Assert.Equal("30", captured);
    }

    [Fact]
    public async Task Async_ArrowFunction()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate(@"
            const test = async () => {
                return await Promise.resolve('async arrow');
            };
            test().then(capture)
        ");
        Assert.Equal("async arrow", captured);
    }

    [Fact]
    public async Task Async_TryCatch()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate(@"
            async function test() {
                try {
                    await Promise.reject('error');
                    return 'not reached';
                } catch (e) {
                    return 'caught: ' + e;
                }
            }
            test().then(capture)
        ");
        Assert.Equal("caught: error", captured);
    }

    #endregion

    #region Promises

    [Fact]
    public async Task Promise_Resolve()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate("Promise.resolve(55).then(capture)");
        Assert.Equal("55", captured);
    }

    [Fact]
    public async Task Promise_Then()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate("Promise.resolve(5).then(x => x * 2).then(capture)");
        Assert.Equal("10", captured);
    }

    [Fact]
    public async Task Promise_ThenChain()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate("Promise.resolve(1).then(x => x + 1).then(x => x + 1).then(x => x + 1).then(capture)");
        Assert.Equal("4", captured);
    }

    [Fact]
    public async Task Promise_Catch()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate("Promise.reject('error').catch(e => 'caught').then(capture)");
        Assert.Equal("caught", captured);
    }

    [Fact]
    public async Task Promise_Finally()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("capture", args =>
        {
            if (args.Count > 0) captured = args[0].ToObject()?.ToString() ?? "";
            return JsValue.Undefined;
        });
        await engine.Evaluate(@"
            let finallyRan = false;
            Promise.resolve(10)
                .finally(() => { finallyRan = true; })
                .then(() => finallyRan)
                .then(capture)
        ");
        Assert.Equal("True", captured);
    }

    #endregion

    #region Switch Advanced

    [Fact]
    public async Task Switch_Default()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 99;
            let result;
            switch (x) {
                case 1: result = 'one'; break;
                case 2: result = 'two'; break;
                default: result = 'default';
            }
            result
        ");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task Switch_Fallthrough()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 1;
            let result = '';
            switch (x) {
                case 1: result += 'a';
                case 2: result += 'b';
                case 3: result += 'c'; break;
                default: result += 'd';
            }
            result
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public async Task Switch_StringCase()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 'hello';
            let result;
            switch (x) {
                case 'hello': result = 'greeting'; break;
                case 'bye': result = 'farewell'; break;
                default: result = 'unknown';
            }
            result
        ");
        Assert.Equal("greeting", result);
    }

    [Fact]
    public async Task Switch_NoMatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 'other';
            let result = 'initial';
            switch (x) {
                case 'a': result = 'found'; break;
                case 'b': result = 'found'; break;
            }
            result
        ");
        Assert.Equal("initial", result);
    }

    #endregion

    #region If/Else Advanced

    [Fact]
    public async Task If_ElseIf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 5;
            let result;
            if (x < 0) {
                result = 'negative';
            } else if (x === 0) {
                result = 'zero';
            } else if (x < 10) {
                result = 'small';
            } else {
                result = 'large';
            }
            result
        ");
        Assert.Equal("small", result);
    }

    [Fact]
    public async Task If_WithoutBraces()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let x = 1;
            if (x === 1) x = 10;
            x
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task If_NestedConditions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let a = 5, b = 10;
            let result;
            if (a > 0) {
                if (b > 5) {
                    result = 'both positive and b > 5';
                } else {
                    result = 'both positive but b <= 5';
                }
            } else {
                result = 'a not positive';
            }
            result
        ");
        Assert.Equal("both positive and b > 5", result);
    }

    #endregion

    #region Labeled Breaks and Continue

    [Fact]
    public async Task LabeledBreak_OuterLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let count = 0;
            outer: for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                    count++;
                    if (i === 1 && j === 1) break outer;
                }
            }
            count
        ");
        Assert.Equal(5d, result); // 3 from i=0, 2 from i=1 (j=0,1)
    }

    [Fact]
    public async Task LabeledContinue_OuterLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let result = '';
            outer: for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                    if (j === 1) continue outer;
                    result += i + ',' + j + ';';
                }
            }
            result
        ");
        Assert.Equal("0,0;1,0;2,0;", result);
    }

    [Fact]
    public async Task LabeledBreak_WhileLoop()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let sum = 0;
            outer: while (true) {
                let i = 0;
                while (i < 10) {
                    sum += i;
                    i++;
                    if (sum > 20) break outer;
                }
            }
            sum
        ");
        Assert.Equal(21d, result);
    }

    #endregion

    #region Classes Advanced

    [Fact]
    public async Task Class_PrivateField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Counter {
                #count = 0;
                increment() { this.#count++; }
                getCount() { return this.#count; }
            }
            const c = new Counter();
            c.increment();
            c.increment();
            c.increment();
            c.getCount()
        ");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Class_PrivateMethod()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Calculator {
                #double(x) { return x * 2; }
                compute(x) { return this.#double(x) + 1; }
            }
            new Calculator().compute(5)
        ");
        Assert.Equal(11d, result);
    }

    [Fact]
    public async Task Class_StaticField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Counter {
                static count = 0;
                static increment() { Counter.count++; }
            }
            Counter.increment();
            Counter.increment();
            Counter.count
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Class_StaticPrivateField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Config {
                static #secret = 42;
                static getSecret() { return Config.#secret; }
            }
            Config.getSecret()
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Class_ComputedMethodName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const methodName = 'compute';
            class Test {
                [methodName](x) { return x * 10; }
            }
            new Test().compute(5)
        ");
        Assert.Equal(50d, result);
    }

    [Fact]
    public async Task Class_SuperConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class Animal {
                constructor(name) { this.name = name; }
            }
            class Dog extends Animal {
                constructor(name, breed) {
                    super(name);
                    this.breed = breed;
                }
            }
            const d = new Dog('Rex', 'Shepherd');
            d.name + ' is a ' + d.breed
        ");
        Assert.Equal("Rex is a Shepherd", result);
    }

    #endregion

    #region Arrow Functions Advanced

    [Fact]
    public async Task Arrow_SingleParam_NoParens()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const double = x => x * 2; double(5)");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Arrow_ImplicitReturn_Object()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("const makeObj = x => ({value: x}); makeObj(42).value");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Arrow_This_Binding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {
                value: 10,
                getValue: function() {
                    const inner = () => this.value;
                    return inner();
                }
            };
            obj.getValue()
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Arrow_AsCallback()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, 2, 3].reduce((acc, x) => acc * x, 1)");
        Assert.Equal(6d, result);
    }

    #endregion

    #region Generators

    [Fact]
    public async Task Generator_Basic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function* gen() {
                yield 1;
                yield 2;
                yield 3;
            }
            const g = gen();
            g.next().value + g.next().value + g.next().value
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Generator_Values()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function* gen() {
                yield 'a';
                yield 'b';
                yield 'c';
            }
            const g = gen();
            let s = '';
            s += g.next().value;
            s += g.next().value;
            s += g.next().value;
            s
        ");
        Assert.Equal("abc", result);
    }

    [Fact]
    public async Task Generator_Next()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function* gen() { yield 1; yield 2; }
            const g = gen();
            const r1 = g.next();
            const r2 = g.next();
            const r3 = g.next();
            r1.value + ',' + r2.value + ',' + r3.done
        ");
        Assert.Equal("1,2,true", result);
    }

    #endregion

    #region Spread and Rest Operators

    [Fact]
    public async Task Spread_FunctionCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function sum(a, b, c) { return a + b + c; }
            const args = [1, 2, 3];
            sum(...args)
        ");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Spread_ArrayConcat()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("[1, ...[2, 3], 4].join(',')");
        Assert.Equal("1,2,3,4", result);
    }

    [Fact]
    public async Task Rest_ArrayDestructuring()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const [first, ...rest] = [1, 2, 3, 4];
            rest.join(',')
        ");
        Assert.Equal("2,3,4", result);
    }

    [Fact]
    public async Task Rest_ObjectDestructuring()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const {a, ...rest} = {a: 1, b: 2, c: 3};
            rest.b + rest.c
        ");
        Assert.Equal(5d, result);
    }

    #endregion

    #region Optional Chaining and Nullish

    [Fact]
    public async Task OptionalChaining_Property()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {a: {b: 42}};
            obj?.a?.b
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task OptionalChaining_Undefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {};
            obj?.a?.b ?? 'default'
        ");
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task OptionalChaining_MethodCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = {
                method() { return 'called'; }
            };
            obj.method?.()
        ");
        Assert.Equal("called", result);
    }

    [Fact]
    public async Task OptionalChaining_ArrayAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3];
            arr?.[1]
        ");
        Assert.Equal(2d, result);
    }

    #endregion

    #region Symbol

    [Fact]
    public async Task Symbol_Create()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof Symbol('test')");
        Assert.Equal("symbol", result);
    }

    [Fact]
    public async Task Symbol_Unique()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("Symbol('a') === Symbol('a')");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task Symbol_AsPropertyKey()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const sym = Symbol('key');
            const obj = { [sym]: 42 };
            obj[sym]
        ");
        Assert.Equal(42d, result);
    }

    #endregion

    #region Map and Set

    [Fact]
    public async Task Map_SetGet()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const map = new Map();
            map.set('key', 100);
            map.get('key')
        ");
        Assert.Equal(100d, result);
    }

    [Fact]
    public async Task Map_Size()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const map = new Map([['a', 1], ['b', 2]]);
            map.size
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Set_AddHas()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const set = new Set();
            set.add(1);
            set.add(2);
            set.add(1); // duplicate
            set.size
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Set_Has()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const set = new Set([1, 2, 3]);
            set.has(2)
        ");
        Assert.Equal(true, result);
    }

    #endregion

    #region BigInt

    [Fact]
    public async Task BigInt_Literal()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("typeof 123n");
        Assert.Equal("bigint", result);
    }

    [Fact]
    public async Task BigInt_Arithmetic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("String(10n + 20n)");
        Assert.Equal("30", result);
    }

    [Fact]
    public async Task BigInt_Comparison()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("10n > 5n");
        Assert.Equal(true, result);
    }

    #endregion

    #region RegExp

    [Fact]
    public async Task RegExp_Test()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("/hello/.test('hello world')");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task RegExp_Exec()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("/o/.exec('hello')[0]");
        Assert.Equal("o", result);
    }

    [Fact]
    public async Task RegExp_Match()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello world'.match(/o/g).length");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task RegExp_Replace()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("'hello'.replace(/l/g, 'L')");
        Assert.Equal("heLLo", result);
    }

    #endregion

    #region Date

    [Fact]
    public async Task Date_Create()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Date(2024, 0, 15).getFullYear()");
        Assert.Equal(2024d, result);
    }

    [Fact]
    public async Task Date_GetMonth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Date(2024, 5, 15).getMonth()");
        Assert.Equal(5d, result); // June (0-indexed)
    }

    [Fact]
    public async Task Date_GetDate()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("new Date(2024, 0, 15).getDate()");
        Assert.Equal(15d, result);
    }

    #endregion

    #region Block Scope in Try/Catch

    [Fact]
    public async Task TryCatch_BlockScopeShadowing_CorrectlyRestored()
    {
        // Tests that when an exception is thrown inside a nested block scope,
        // the catch handler sees the outer scope (parameter 'x' = 'outer')
        // not the inner block-scoped 'x' = 'inner'.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function(x) {
                try {
                    let x = 'inner';
                    throw 0;
                } catch (e) {
                    return x;  // Should see parameter 'outer', not let 'inner'
                }
            })('outer');
            """);
        Assert.Equal("outer", result);
    }

    [Fact]
    public async Task TryFinally_BlockScopeShadowing_CorrectlyRestored()
    {
        // Tests that finally block sees the outer scope after block-scoped variable
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function(x) {
                var finallyX;
                try {
                    let x = 'inner';
                    throw 0;
                } catch (e) {
                    // ignore
                } finally {
                    finallyX = x;  // Should see parameter 'outer'
                }
                return finallyX;
            })('outer');
            """);
        Assert.Equal("outer", result);
    }

    [Fact]
    public async Task Generator_TryCatch_BlockScopeShadowing_CorrectlyRestored()
    {
        // Tests that generators correctly restore block scope in try/catch
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                function* gen(x) {
                    try {
                        let x = 'inner';
                        throw 0;
                    } catch (e) {
                        yield x;  // Should see parameter 'outer'
                    }
                }
                let g = gen('outer');
                return g.next().value;
            })();
            """);
        Assert.Equal("outer", result);
    }

    #endregion

    #region Arrow Function This Binding

    [Fact]
    public async Task ArrowFunction_InClassMethod_AccessesThisCorrectly()
    {
        // Tests that arrow functions inside class methods can access `this`
        // This uses a PUBLIC field to isolate the `this` binding issue from private field issues
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Counter {
                count = 0;

                increment() {
                    const inc = () => {
                        this.count++;
                        return this.count;
                    };
                    return inc();
                }
            }
            const c = new Counter();
            c.increment();
            """);
        Assert.Equal(1d, result);
    }

    #endregion

    #region Private Fields in Arrow Functions

    [Fact]
    public async Task PrivateField_AccessibleFromArrowFunction()
    {
        // Tests that private fields are accessible from arrow functions inside class methods
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Counter {
                #count = 0;

                increment() {
                    const inc = () => {
                        this.#count++;
                        return this.#count;
                    };
                    return inc();
                }
            }
            const c = new Counter();
            c.increment();
            """);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task PrivateMethod_AccessibleFromArrowFunction()
    {
        // Tests that private methods are accessible from arrow functions inside class methods
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Calculator {
                #add(a, b) {
                    return a + b;
                }

                compute(a, b) {
                    const fn = () => this.#add(a, b);
                    return fn();
                }
            }
            const c = new Calculator();
            c.compute(3, 4);
            """);
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Generator_PrivateFieldAccess_FromArrowFunction()
    {
        // Tests that generators with arrow functions can access private fields
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function() {
                class Counter {
                    #count = 0;

                    *counter() {
                        const inc = () => ++this.#count;
                        yield inc();
                        yield inc();
                        yield inc();
                    }
                }
                const c = new Counter();
                const g = c.counter();
                const r1 = g.next().value;
                const r2 = g.next().value;
                const r3 = g.next().value;
                return r1 === 1 && r2 === 2 && r3 === 3;
            })();
            """);
        Assert.True((bool)result!);
    }

    #endregion

    #region Try-Catch-Finally Edge Cases

    [Fact]
    public async Task TryFinally_SimpleReturnFromFinally()
    {
        // Simplest case: just try-finally with return in finally
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                try {
                } finally {
                    return 42;
                }
            }
            test();
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task TryFinally_ReturnVariableFromFinally_Var()
    {
        // Return a var variable from finally
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                var x = 42;
                try {
                } finally {
                    return x;
                }
            }
            test();
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task TryFinally_ReturnVariableFromFinally_Let()
    {
        // Return a let variable from finally
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                let x = 42;
                try {
                } finally {
                    return x;
                }
            }
            test();
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task TryFinally_ReturnVariableDefinedInsideFinally()
    {
        // Define and return variable inside finally
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                try {
                } finally {
                    var y = 99;
                    return y;
                }
            }
            test();
        ");
        Assert.Equal(99.0, result);
    }

    [Fact]
    public async Task TryCatchFinally_SimplestReturnFromFinally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function test() {
                var x = 42;
                try {
                    throw 'error';
                } catch (e) {
                    // caught
                } finally {
                    return x;
                }
            }
            test();
        ");
        Assert.Equal(42.0, result);
    }

    [Fact]
    public async Task TryCatchFinally_SwitchInsideTry_ReturnFromFinally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function SwitchTest1(value){
              var result = 0;
              try{
                switch(value) {
                  case 1:
                    result += 4;
                    throw result;
                    break;
                  case 4:
                    result += 64;
                    throw 'ex';
                }
                return result;
              }
              catch(e){
                // caught exception
              }
              finally{
                return result;
              }
            }
            SwitchTest1(1);
        ");
        Assert.Equal(4.0, result);
    }

    [Fact]
    public async Task TryCatchFinally_ThrowFromFinally()
    {
        // Test that throw from finally propagates and overrides catch exception
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate(@"
                var count = { catch: 0, finally: 0 };
                function test() {
                    try {
                        throw 'try';
                    } catch (e) {
                        count.catch += 1;
                        throw 'catch';
                    } finally {
                        count.finally += 1;
                        throw 'finally';
                    }
                    return 'wat';
                }
                test();
            ");
        });
        Assert.Equal("finally", exception.ThrownValue.ToObject()?.ToString());
    }

    [Fact]
    public async Task TryCatchFinally_ThrowFromFinally_OverridesReturn()
    {
        // Test that throw from finally propagates and overrides return from catch
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate(@"
                var count = { catch: 0, finally: 0 };
                function test() {
                    try {
                        throw 'try';
                    } catch (e) {
                        count.catch += 1;
                        return 'catch';
                    } finally {
                        count.finally += 1;
                        throw 'finally';
                    }
                    return 'wat';
                }
                test();
            ");
        });
        Assert.Equal("finally", exception.ThrownValue.ToObject()?.ToString());
    }

    [Fact]
    public async Task TryFinally_ThrowFromFinally_OverridesReturn()
    {
        // Test that throw from finally propagates and overrides return from try
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate(@"
                function test() {
                    try {
                        return 'try';
                    } finally {
                        throw 'finally';
                    }
                    return 'wat';
                }
                test();
            ");
        });
        Assert.Equal("finally", exception.ThrownValue.ToObject()?.ToString());
    }

    [Fact]
    public async Task SwitchTest3_BreakInFinallyInsideSwitch()
    {
        // This test has break inside finally inside switch case
        // which is valid JavaScript - finally break overrides throw
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function SwitchTest3(value){
              var result = 0;
              switch(value) {
                case 0:
                  try{
                    result += 2;
                    throw 'ex';
                  }
                  finally{
                    break;
                  }
                default:
                  result += 32;
                  break;
              }
              return result;
            }
            SwitchTest3(0);
        ");
        // When we hit case 0, we do result += 2, throw 'ex', then finally runs
        // and break in finally overrides the throw, exiting the switch
        // So we return 2, not 34 (2+32)
        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task SwitchTest2_BreakInTryInsideSwitch()
    {
        // This test has break inside try (with finally) inside switch case
        // break should trigger finally then exit switch
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var c2 = 0;
            function SwitchTest2(value){
              var result = 0;
              switch(value) {
                case 0:
                  try{
                    result += 2;
                    break;
                  }
                  finally{
                    c2 = 1;
                  }
                case 1:
                  result += 4;
                  break;
                default:
                  result += 32;
                  break;
              }
              return result;
            }
            SwitchTest2(0);
        ");
        // When we hit case 0, we do result += 2, then break
        // break triggers finally (c2 = 1), then exits switch
        // So we return 2
        Assert.Equal(2.0, result);
    }

    [Fact]
    public async Task SwitchTest1_SwitchInsideTryWithFinallyReturn()
    {
        // Test from S12.14_A15.js - switch inside try with finally returning result
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function SwitchTest1(value){
              var result = 0;
              try{
                switch(value) {
                  case 1:
                    result += 4;
                    throw result;
                    break;
                  default:
                    result += 32;
                    break;
                  case 4:
                    result += 64;
                    throw 'ex';
                }
                return result;
              }
              catch(e){
                // catch handles the throw
              }
              finally{
                return result;
              }
            }
            SwitchTest1(1);
        ");
        // case 1: result = 4, throw 4, caught, finally returns 4
        Assert.Equal(4.0, result);
    }

    #endregion
}
