using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Foundation tests covering basic JavaScript evaluation.
/// These are simple sanity checks for core language features.
/// </summary>
public class FoundationTests
{
    #region Literals

    [Fact]
    public async Task Literal_Integer()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Literal_Float()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("3.14");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public async Task Literal_String()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'hello'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Literal_StringDoubleQuotes()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("\"world\"");
        Assert.Equal("world", result);
    }

    [Fact]
    public async Task Literal_True()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Literal_False()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("false");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task Literal_Null()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null");
        Assert.Null(result);
    }

    [Fact]
    public async Task Literal_Undefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("undefined");
        Assert.Equal(Symbol.Undefined, result);
    }

    #endregion

    #region Arithmetic Operators

    [Fact]
    public async Task Operator_Add()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 + 2");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Operator_Subtract()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5 - 3");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Operator_Multiply()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("4 * 3");
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task Operator_Divide()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10 / 2");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Operator_Modulo()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("10 % 3");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Operator_Exponent()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 ** 3");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task Operator_UnaryMinus()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("-5");
        Assert.Equal(-5d, result);
    }

    [Fact]
    public async Task Operator_UnaryPlus()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("+'42'");
        Assert.Equal(42d, result);
    }

    #endregion

    #region Comparison Operators

    [Fact]
    public async Task Operator_Equal()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 == 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_StrictEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 === 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_NotEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 != 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_StrictNotEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 !== '1'");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LessThan()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("1 < 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_GreaterThan()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 > 1");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LessThanOrEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 <= 2");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_GreaterThanOrEqual()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("3 >= 2");
        Assert.Equal(true, result);
    }

    #endregion

    #region Logical Operators

    [Fact]
    public async Task Operator_LogicalAnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("true && true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LogicalOr()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("false || true");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_LogicalNot()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("!false");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Operator_NullishCoalescing()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null ?? 'default'");
        Assert.Equal("default", result);
    }

    #endregion

    #region Bitwise Operators

    [Fact]
    public async Task Operator_BitwiseAnd()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5 & 3");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Operator_BitwiseOr()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5 | 3");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Operator_BitwiseXor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("5 ^ 3");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Operator_BitwiseNot()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("~5");
        Assert.Equal(-6d, result);
    }

    [Fact]
    public async Task Operator_LeftShift()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("2 << 3");
        Assert.Equal(16d, result);
    }

    [Fact]
    public async Task Operator_RightShift()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("16 >> 2");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Operator_UnsignedRightShift()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("-1 >>> 0");
        Assert.Equal(4294967295d, result);
    }

    #endregion

    #region String Operations

    [Fact]
    public async Task String_Concatenation()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'hello' + ' ' + 'world'");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task String_Length()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'hello'.length");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task String_CharAt()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'hello'.charAt(1)");
        Assert.Equal("e", result);
    }

    [Fact]
    public async Task String_ToUpperCase()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("'hello'.toUpperCase()");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task String_TemplateLiteral()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("`1 + 2 = ${1 + 2}`");
        Assert.Equal("1 + 2 = 3", result);
    }

    #endregion

    #region Variables

    [Fact]
    public async Task Variable_Var()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var x = 10; x");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Variable_Let()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 20; x");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Variable_Const()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const x = 30; x");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task Variable_Assignment()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 1; x = 5; x");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Variable_CompoundAssignment()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 10; x += 5; x");
        Assert.Equal(15d, result);
    }

    #endregion

    #region Conditionals

    [Fact]
    public async Task Conditional_IfTrue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 0; if (true) { x = 1; } x");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Conditional_IfFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 0; if (false) { x = 1; } x");
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Conditional_IfElse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 0; if (false) { x = 1; } else { x = 2; } x");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Conditional_Ternary()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("true ? 'yes' : 'no'");
        Assert.Equal("yes", result);
    }

    [Fact]
    public async Task Conditional_Switch()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let sum = 0; for (let i = 1; i <= 5; i++) { sum += i; } sum");
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task Loop_While()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let i = 0; while (i < 5) { i++; } i");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Loop_DoWhile()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let i = 0; do { i++; } while (i < 3); i");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Loop_ForOf()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let sum = 0; for (let x of [1, 2, 3]) { sum += x; } sum");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task Loop_ForIn()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let keys = ''; for (let k in {a: 1, b: 2}) { keys += k; } keys");
        Assert.Equal("ab", result);
    }

    [Fact]
    public async Task Loop_Break()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let i = 0; while (true) { i++; if (i === 3) break; } i");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Loop_Continue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let sum = 0; for (let i = 1; i <= 5; i++) { if (i === 3) continue; sum += i; } sum");
        Assert.Equal(12d, result); // 1+2+4+5 = 12
    }

    #endregion

    #region Functions

    [Fact]
    public async Task Function_Declaration()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("function add(a, b) { return a + b; } add(2, 3)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Function_Expression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const add = function(a, b) { return a + b; }; add(3, 4)");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task Function_Arrow()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const add = (a, b) => a + b; add(4, 5)");
        Assert.Equal(9d, result);
    }

    [Fact]
    public async Task Function_ArrowWithBlock()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const add = (a, b) => { return a + b; }; add(5, 6)");
        Assert.Equal(11d, result);
    }

    [Fact]
    public async Task Function_DefaultParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("function greet(name = 'World') { return 'Hello, ' + name; } greet()");
        Assert.Equal("Hello, World", result);
    }

    [Fact]
    public async Task Function_RestParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("function sum(...nums) { return nums.reduce((a, b) => a + b, 0); } sum(1, 2, 3, 4)");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Function_Recursion()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("function factorial(n) { return n <= 1 ? 1 : n * factorial(n - 1); } factorial(5)");
        Assert.Equal(120d, result);
    }

    [Fact]
    public async Task Function_Closure()
    {
        await using var engine = new JsEngine();
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
    public async Task Function_IIFE()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("(function() { return 42; })()");
        Assert.Equal(42d, result);
    }

    #endregion

    #region Arrays

    [Fact]
    public async Task Array_Literal()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[1, 2, 3].length");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Array_Access()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[10, 20, 30][1]");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Array_Push()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let arr = [1]; arr.push(2); arr.length");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Array_Pop()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[1, 2, 3].pop()");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Array_Map()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[1, 2, 3].map(x => x * 2).join(',')");
        Assert.Equal("2,4,6", result);
    }

    [Fact]
    public async Task Array_Filter()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[1, 2, 3, 4, 5].filter(x => x % 2 === 0).join(',')");
        Assert.Equal("2,4", result);
    }

    [Fact]
    public async Task Array_Reduce()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[1, 2, 3, 4].reduce((acc, x) => acc + x, 0)");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Array_Spread()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[...[1, 2], ...[3, 4]].join(',')");
        Assert.Equal("1,2,3,4", result);
    }

    [Fact]
    public async Task Array_Destructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const [a, b, c] = [1, 2, 3]; a + b + c");
        Assert.Equal(6d, result);
    }

    #endregion

    #region Objects

    [Fact]
    public async Task Object_Literal()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("({a: 1, b: 2}).a");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Object_DotAccess()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const obj = {x: 10}; obj.x");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task Object_BracketAccess()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const obj = {x: 20}; obj['x']");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task Object_ComputedProperty()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const key = 'foo'; const obj = {[key]: 42}; obj.foo");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task Object_Shorthand()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const x = 1; const y = 2; const obj = {x, y}; obj.x + obj.y");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Object_Method()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const obj = { getValue() { return 99; } }; obj.getValue()");
        Assert.Equal(99d, result);
    }

    [Fact]
    public async Task Object_Spread()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const obj = {...{a: 1}, ...{b: 2}}; obj.a + obj.b");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Object_Destructuring()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const {a, b} = {a: 10, b: 20}; a + b");
        Assert.Equal(30d, result);
    }

    [Fact]
    public async Task Object_This()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("const obj = { x: 5, getX() { return this.x; } }; obj.getX()");
        Assert.Equal(5d, result);
    }

    #endregion

    #region Classes

    [Fact]
    public async Task Class_Constructor()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 42");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task Typeof_String()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof 'hello'");
        Assert.Equal("string", result);
    }

    [Fact]
    public async Task Typeof_Boolean()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof true");
        Assert.Equal("boolean", result);
    }

    [Fact]
    public async Task Typeof_Object()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof {}");
        Assert.Equal("object", result);
    }

    [Fact]
    public async Task Typeof_Function()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof function() {}");
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task Typeof_Undefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("typeof undefined");
        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task Instanceof_Array()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("[] instanceof Array");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task Instanceof_Object()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("({}) instanceof Object");
        Assert.Equal(true, result);
    }

    #endregion

    #region Math Object

    [Fact]
    public async Task Math_Abs()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.abs(-5)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Math_Floor()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.floor(3.7)");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Math_Ceil()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.ceil(3.2)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Round()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.round(3.5)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Max()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.max(1, 5, 3)");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Math_Min()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.min(1, 5, 3)");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Math_Sqrt()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.sqrt(16)");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task Math_Pow()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.pow(2, 10)");
        Assert.Equal(1024d, result);
    }

    #endregion

    #region JSON

    [Fact]
    public async Task JSON_Stringify()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("JSON.stringify({a: 1, b: 2})");
        Assert.Equal("{\"a\":1,\"b\":2}", result);
    }

    [Fact]
    public async Task JSON_Parse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("JSON.parse('{\"x\":10}').x");
        Assert.Equal(10d, result);
    }

    #endregion
}
