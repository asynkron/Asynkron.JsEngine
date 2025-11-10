using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class EvaluatorTests
{
    [Fact]
    public async Task EvaluateArithmeticAndVariableLookup()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let answer = 1 + 2 * 3; answer;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task EvaluateFunctionDeclarationAndInvocation()
    {
        var engine = new JsEngine();
        var source = "function add(a, b) { return a + b; } let result = add(2, 3); result;";
        var result = await engine.Evaluate(source);
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task EvaluateClosureCapturesOuterVariable()
    {
        var engine = new JsEngine();
        var source = "function makeAdder(x) { function inner(y) { return x + y; } return inner; } let plusTen = makeAdder(10); let fifteen = plusTen(5); fifteen;";
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task EvaluateFunctionExpression()
    {
        var engine = new JsEngine();
        var source = "let add = function(a, b) { return a + b; }; add(4, 5);";
        var result = await engine.Evaluate(source);
        Assert.Equal(9d, result);
    }

    [Fact]
    public async Task HostFunctionInterop()
    {
        var captured = new List<object?>();
        var engine = new JsEngine();
        engine.SetGlobalFunction("collect", args =>
        {
            captured.AddRange(args);
            return args.Count;
        });

        var result = await engine.Evaluate("collect(\"hello\", 3); collect(\"world\");");

        Assert.Equal(1, result); // last call returns number of args
        Assert.Collection(captured,
            item => Assert.Equal("hello", item),
            item => Assert.Equal(3d, item),
            item => Assert.Equal("world", item));
    }

    [Fact]
    public async Task EvaluateObjectLiteralAndPropertyUsage()
    {
        var engine = new JsEngine();
        var source = "let obj = { a: 10, x: function () { return 5; } }; let total = obj.a + obj.x(); total;";

        var result = await engine.Evaluate(source);

        Assert.Equal(15d, result); // object property read plus function invocation
    }

    [Fact]
    public async Task EvaluateArrayLiteralSupportsIndexing()
    {
        var engine = new JsEngine();
        var source = @"
let values = [1, 2];
values[2] = values[0] + values[1];
let alias = values[""length""];
let missing = values[5];
if (missing == null) { missing = 1; } else { missing = 0; }
alias + missing + values[2];
";

        var result = await engine.Evaluate(source);

        Assert.Equal(7d, result); // length reflects new entry and missing reads return null
    }

    [Fact]
    public async Task LogicalOperatorsShortCircuitAndReturnOperands()
    {
        var engine = new JsEngine();
        var source = @"
let hits = 0;
function record(value) {
    hits = hits + 1;
    return value; // propagate the input to observe operator return values
}

let andResult = false && record(1);
let orResult = true || record(2);
let coalesceResult = null ?? record(3);
let coalesceNonNull = 0 ?? record(4);
";

        object? temp = await engine.Evaluate(source);

        Assert.Equal(1d,await  engine.Evaluate("hits;")); // only the nullish coalescing branch invokes record
        Assert.False(Assert.IsType<bool>(await engine.Evaluate("andResult;")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("orResult;")));
        Assert.Equal(3d,await  engine.Evaluate("coalesceResult;"));
        Assert.Equal(0d,await  engine.Evaluate("coalesceNonNull;"));
    }

    [Fact]
    public async Task StrictEqualityRequiresMatchingTypes()
    {
        var engine = new JsEngine();
        engine.SetGlobalFunction("getInt", _ => 1);

        var source = @"
let outcomes = [
    1 === 1,
    1 === ""1"",
    1 !== 2,
    null === null,
    getInt() === 1
];
outcomes;
";

        object? temp = await engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[0];")));
        Assert.False(Assert.IsType<bool>(engine.Evaluate("outcomes[1];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[2];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[3];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[4];")));
    }

    [Fact]
    public async Task VarDeclarationHoistsToFunctionScope()
    {
        var engine = new JsEngine();
        var source = @"
function sample() {
    if (true) {
        var hidden = 41;
    }

    return hidden + 1;
}
sample();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task ConstAssignmentThrows()
    {
        var engine = new JsEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate("const fixed = 1; fixed = 2;"));
    }

    [Fact]
    public async Task TryCatchFinallyBindsThrownValueAndRunsCleanup()
    {
        var engine = new JsEngine();
        var source = @"
let captured = 0;
try {
    throw 21;
} catch (err) {
    captured = err;
} finally {
    captured = captured + 21;
}
captured;
";

        var result = await engine.Evaluate(source);

        Assert.Equal(42d, result); // catch observes thrown value and finally still executes
    }

    [Fact]
    public async Task FinallyRunsForUnhandledThrow()
    {
        var engine = new JsEngine();
        var source = @"
let cleanup = 0;
try {
    throw ""boom"";
} finally {
    cleanup = cleanup + 1;
}
";

        await Assert.ThrowsAsync<Exception>(async () => await engine.Evaluate(source));

        var cleanupValue = await engine.Evaluate("cleanup;");
        Assert.Equal(1d, cleanupValue); // finally executed even though the throw escaped
    }

    [Fact]
    public async Task FinallyReturnOverridesTryReturn()
    {
        var engine = new JsEngine();
        var source = @"
function sample() {
    try {
        return 1;
    } finally {
        return 2;
    }
}
sample();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(2d, result); // return inside finally shadows earlier return
    }

    [Fact]
    public async Task MethodInvocationBindsThis()
    {
        var engine = new JsEngine();
        var source = "let obj = { x: 10, f: function () { return this.x; } }; obj.f();";

        var result = await engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task IndexedMethodInvocationBindsThis()
    {
        var engine = new JsEngine();
        var source = @"
let obj = { value: 10, getter: function() { return this.value; } };
obj[""getter""]();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task HostFunctionReceivesThisBinding()
    {
        var engine = new JsEngine();
        engine.SetGlobalFunction("reflectThis", (self, _) => self);

        var result = await engine.Evaluate("let obj = { value: 42, reflect: reflectThis }; obj.reflect();");

        var thisBinding = Assert.IsAssignableFrom<IDictionary<string, object?>>(result);
        Assert.Equal(42d, thisBinding["value"]);
    }

    [Fact]
    public async Task PrototypeLookupResolvesInheritedMethods()
    {
        var engine = new JsEngine();
        var source = @"
let base = {
    multiplier: 2,
    calculate: function(value) { return value * this.multiplier; }
};
let derived = { value: 7, __proto__: base };
derived.calculate(derived.value);
";

        var result = await engine.Evaluate(source);

        Assert.Equal(14d, result);
    }

    [Fact]
    public async Task PrototypeAssignmentLinksObjectsAfterCreation()
    {
        var engine = new JsEngine();
        var source = @"
let base = { greet: function() { return ""hi "" + this.name; } };
let user = { name: ""Alice"" };
user.__proto__ = base;
user.greet();
";

        var result = await engine.Evaluate(source);

        Assert.Equal("hi Alice", result);
    }

    [Fact]
    public async Task NewCreatesInstancesWithConstructorPrototypes()
    {
        var engine = new JsEngine();
        var source = @"
function Person(name) {
    this.name = name;
}
Person.prototype.describe = function() { return ""Person:"" + this.name; };
let person = new Person(""Bob"");
person.describe();
";

        var result = await engine.Evaluate(source);

        Assert.Equal("Person:Bob", result);
    }

    [Fact]
    public async Task MethodClosuresCanReachThisViaCapturedReference()
    {
        var engine = new JsEngine();
        var source = @"
let obj = {
    value: 10,
    makeIncrementer: function(step) {
        let receiver = this; // capture the current method receiver for later use
        return function(extra) {
            return receiver.value + step + extra;
        };
    }
};
let inc = obj.makeIncrementer(5);
inc(3);
";

        var result = await engine.Evaluate(source);

        Assert.Equal(18d, result);
    }

    [Fact]
    public async Task DistinctMethodCallsProvideIndependentThisBindings()
    {
        var engine = new JsEngine();
        var source = @"
let factory = {
    create: function(number) {
        return {
            value: number,
            read: function() { return this.value; } // rely on this binding when invoked as a method
        };
    }
};
let first = factory.create(7);
let second = factory.create(8);
first.read() + second.read();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task ClassDeclarationSupportsConstructorsAndMethods()
    {
        var engine = new JsEngine();
        var source = @"
class Counter {
    constructor(start) {
        this.value = start;
    }

    increment() {
        this.value = this.value + 1; // mutate state then return it for verification
        return this.value;
    }
}
let instance = new Counter(5);
instance.increment();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ClassWithoutExplicitConstructorFallsBackToDefault()
    {
        var engine = new JsEngine();
        var source = @"
class Widget {
    describe() { return ""widget""; }
}
let widget = new Widget();
Widget.prototype.constructor == Widget;
";

        var result = await engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public async Task ClassInheritanceSupportsSuperConstructorAndMethodCalls()
    {
        var engine = new JsEngine();
        var source = @"
class Base {
    constructor(value) {
        this.base = value;
    }

    read() {
        return this.base;
    }
}

class Derived extends Base {
    constructor(value) {
        super(value + 1); // forward to the base constructor with a transformed argument
        this.extra = 5;
    }

    read() {
        return super.read() + this.extra; // reuse the base implementation and add derived state
    }
}

let instance = new Derived(2);
instance.read();
";

        var result = await engine.Evaluate(source);

        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task EvaluateIfElseAndBlockScopes()
    {
        var engine = new JsEngine();
        var source = @"
let value = 0;
if (false) {
    value = 1;
} else {
    value = 2;
}
value;
";

        var result = await engine.Evaluate(source);
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task EvaluateWhileLoopUpdatesValues()
    {
        var engine = new JsEngine();
        var source = @"
let total = 0;
let current = 1;
while (current <= 3) {
    total = total + current;
    current = current + 1;
}
total;
";

        var result = await engine.Evaluate(source);
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task EvaluateForLoopHonoursBreakAndContinue()
    {
        var engine = new JsEngine();
        var source = @"
let sum = 0;
for (let i = 0; i < 10; i = i + 1) {
    if (i == 3) {
        continue;
    }

    if (i == 5) {
        break;
    }

    sum = sum + i;
}
sum;
";

        var result = await engine.Evaluate(source);
        Assert.Equal(7d, result); // adds 0 + 1 + 2 + 4 before breaking at 5
    }

    [Fact]
    public async Task SwitchStatementSupportsFallthrough()
    {
        var engine = new JsEngine();
        var source = @"
function describe(value) {
    switch (value) {
        case 1:
            return ""one"";
        case 2:
        case 3:
            return ""few"";
        default:
            return ""many"";
    }
}
describe(3);
";

        var result = await engine.Evaluate(source);

        Assert.Equal("few", result);
    }

    [Fact]
    public async Task SwitchBreakRemainsInsideLoop()
    {
        var engine = new JsEngine();
        var source = @"
let total = 0;
for (let i = 0; i < 3; i = i + 1) {
    switch (i) {
        case 1:
            total = total + 10;
            break;
        default:
            total = total + 1;
    }
}
total;
";

        var result = await engine.Evaluate(source);

        // Ensure the break only exits the switch and not the outer loop.
        Assert.Equal(12d, result);
    }

    [Fact]
    public async Task EvaluateDoWhileRunsBodyAtLeastOnce()
    {
        var engine = new JsEngine();
        var source = @"
let attempts = 0;
do {
    attempts = attempts + 1;
} while (false);
attempts;
";

        var result = await engine.Evaluate(source);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task TernaryOperatorReturnsThenBranchWhenConditionIsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("true ? 10 : 20;");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task TernaryOperatorReturnsElseBranchWhenConditionIsFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("false ? 10 : 20;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task TernaryOperatorEvaluatesConditionForTruthiness()
    {
        var engine = new JsEngine();
        var source = @"
let x = 5;
let result = x > 3 ? ""big"" : ""small"";
result;
";
        var result = await engine.Evaluate(source);
        Assert.Equal("big", result);
    }

    [Fact]
    public async Task TernaryOperatorWithZeroAsFalsyCondition()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"0 ? ""yes"" : ""no"";");
        Assert.Equal("no", result);
    }

    [Fact]
    public async Task TernaryOperatorWithNullAsFalsyCondition()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("null ? 1 : 2;");
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task TernaryOperatorCanBeNested()
    {
        var engine = new JsEngine();
        var source = @"
let score = 85;
let grade = score >= 90 ? ""A"" : score >= 80 ? ""B"" : score >= 70 ? ""C"" : ""D"";
grade;
";
        var result = await engine.Evaluate(source);
        Assert.Equal("B", result);
    }

    [Fact]
    public async Task TernaryOperatorOnlyEvaluatesSelectedBranch()
    {
        var engine = new JsEngine();
        var source = @"
let sideEffect = 0;
function increment() {
    sideEffect = sideEffect + 1;
    return sideEffect;
}
let result = true ? 100 : increment();
sideEffect;
";
        var result = await engine.Evaluate(source);
        Assert.Equal(0d, result); // increment should not be called
    }

    [Fact]
    public async Task TernaryOperatorWorksInComplexExpressions()
    {
        var engine = new JsEngine();
        var source = @"
let a = 5;
let b = 10;
let max = a > b ? a : b;
let doubled = (max === 10 ? max : 0) * 2;
doubled;
";
        var result = await engine.Evaluate(source);
        Assert.Equal(20d, result);
    }

    [Fact]
    public async Task TernaryOperatorInFunctionReturn()
    {
        var engine = new JsEngine();
        var source = @"
function absoluteValue(x) {
    return x >= 0 ? x : -x;
}
absoluteValue(-42);
";
        var result = await engine.Evaluate(source);
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task TemplateLiteralWithSimpleString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("`hello world`;");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task TemplateLiteralWithSingleExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 42; `The answer is ${x}`;");
        Assert.Equal("The answer is 42", result);
    }

    [Fact]
    public async Task TemplateLiteralWithMultipleExpressions()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let a = 10; let b = 20; `${a} + ${b} = ${a + b}`;");
        Assert.Equal("10 + 20 = 30", result);
    }

    [Fact]
    public async Task TemplateLiteralWithStringInterpolation()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let name = ""Alice"";
let age = 30;
`Hello, my name is ${name} and I am ${age} years old.`;
");
        Assert.Equal("Hello, my name is Alice and I am 30 years old.", result);
    }

    [Fact]
    public async Task TemplateLiteralWithComplexExpressions()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
function greet(name) { return ""Hello, "" + name; }
let user = ""Bob"";
`${greet(user)}! You have ${3 * 5} messages.`;
");
        Assert.Equal("Hello, Bob! You have 15 messages.", result);
    }

    [Fact]
    public async Task TemplateLiteralWithBooleanAndNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("`true: ${true}, false: ${false}, null: ${null}`;");
        Assert.Equal("true: true, false: false, null: null", result);
    }

    [Fact]
    public async Task GetterInObjectLiteral()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let obj = {
    _value: 42,
    get value() { return this._value; }
};
obj.value;
");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task SetterInObjectLiteral()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let obj = {
    _value: 0,
    set value(v) { this._value = v * 2; }
};
obj.value = 21;
obj._value;
");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task GetterAndSetterTogether()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let obj = {
    _temp: 0,
    get celsius() { return this._temp; },
    set celsius(c) { this._temp = c; },
    get fahrenheit() { return this._temp * 9 / 5 + 32; },
    set fahrenheit(f) { this._temp = (f - 32) * 5 / 9; }
};
obj.celsius = 100;
obj.fahrenheit;
");
        Assert.Equal(212d, result);
    }

    [Fact]
    public async Task GetterInClass()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
class Rectangle {
    constructor(width, height) {
        this.width = width;
        this.height = height;
    }
    get area() {
        return this.width * this.height;
    }
}
let rect = new Rectangle(5, 10);
rect.area;
");
        Assert.Equal(50d, result);
    }

    [Fact]
    public async Task SetterInClass()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
class Person {
    constructor(firstName, lastName) {
        this.firstName = firstName;
        this.lastName = lastName;
    }
    get fullName() {
        return this.firstName + "" "" + this.lastName;
    }
    set fullName(name) {
        this.firstName = ""Updated"";
        this.lastName = name;
    }
}
let person = new Person(""John"", ""Doe"");
person.fullName = ""Smith"";
person.fullName;
");
        Assert.Equal("Updated Smith", result);
    }

    [Fact]
    public async Task RestParameterCollectsRemainingArguments()
    {
        var engine = new JsEngine();
        var source = @"
function sum(first, ...rest) {
    let total = first;
    let i = 0;
    while (i < rest[""length""]) {
        total = total + rest[i];
        i = i + 1;
    }
    return total;
}
sum(1, 2, 3, 4, 5);
";
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task RestParameterWithNoExtraArgumentsCreatesEmptyArray()
    {
        var engine = new JsEngine();
        var source = @"
function test(a, ...rest) {
    return rest[""length""];
}
test(1);
";
        var result = await engine.Evaluate(source);
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task SpreadOperatorInArrayLiteral()
    {
        var engine = new JsEngine();
        var source = @"
let arr1 = [1, 2, 3];
let arr2 = [4, 5, 6];
let combined = [0, ...arr1, ...arr2, 7];
combined[0] + combined[1] + combined[2] + combined[3] + combined[4] + combined[5] + combined[6] + combined[7];
";
        var result = await engine.Evaluate(source);
        Assert.Equal(28d, result); // 0+1+2+3+4+5+6+7
    }

    [Fact]
    public async Task SpreadOperatorInFunctionCall()
    {
        var engine = new JsEngine();
        var source = @"
function add(a, b, c) {
    return a + b + c;
}
let numbers = [10, 20, 30];
add(...numbers);
";
        var result = await engine.Evaluate(source);
        Assert.Equal(60d, result);
    }

    [Fact]
    public async Task SpreadOperatorWithMixedArguments()
    {
        var engine = new JsEngine();
        var source = @"
function greet(greeting, name1, name2) {
    return greeting + "" "" + name1 + "" and "" + name2;
}
let names = [""Alice"", ""Bob""];
greet(""Hello"", ...names);
";
        var result = await engine.Evaluate(source);
        Assert.Equal("Hello Alice and Bob", result);
    }

    [Fact]
    public async Task RestParameterWithSpreadInCall()
    {
        var engine = new JsEngine();
        var source = @"
function joinAll(...items) {
    let result = """";
    let i = 0;
    while (i < items[""length""]) {
        if (i > 0) {
            result = result + "","";
        }
        result = result + items[i];
        i = i + 1;
    }
    return result;
}
let arr = [""b"", ""c""];
joinAll(""a"", ...arr, ""d"");
";
        var result = await engine.Evaluate(source);
        Assert.Equal("a,b,c,d", result);
    }

    [Fact]
    public async Task SpreadInNestedArrays()
    {
        var engine = new JsEngine();
        var source = @"
let inner = [2, 3];
let outer = [1, ...inner, 4];
let final = [0, ...outer, 5];
final[0] + final[1] + final[2] + final[3] + final[4] + final[5];
";
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result); // 0+1+2+3+4+5
    }

    [Fact]
    public async Task MathObjectProvidesConstants()
    {
        var engine = new JsEngine();
        
        var pi = await engine.Evaluate("Math.PI;");
        Assert.Equal(Math.PI, pi);
        
        var e = await engine.Evaluate("Math.E;");
        Assert.Equal(Math.E, e);
        
        var sqrt2 = await engine.Evaluate("Math.SQRT2;");
        Assert.Equal(Math.Sqrt(2), sqrt2);
    }

    [Fact]
    public async Task MathSqrtCalculatesSquareRoot()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.sqrt(16);");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task MathPowCalculatesPower()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.pow(2, 3);");
        Assert.Equal(8d, result);
    }

    [Fact]
    public async Task MathAbsReturnsAbsoluteValue()
    {
        var engine = new JsEngine();
        
        var positive = await engine.Evaluate("Math.abs(-5);");
        Assert.Equal(5d, positive);
        
        var alreadyPositive = await engine.Evaluate("Math.abs(3);");
        Assert.Equal(3d, alreadyPositive);
    }

    [Fact]
    public async Task MathFloorCeilRound()
    {
        var engine = new JsEngine();
        
        var floor = await engine.Evaluate("Math.floor(4.7);");
        Assert.Equal(4d, floor);
        
        var ceil = await engine.Evaluate("Math.ceil(4.3);");
        Assert.Equal(5d, ceil);
        
        var round = await engine.Evaluate("Math.round(4.5);");
        Assert.Equal(5d, round);
    }

    [Fact]
    public async Task MathMaxMinFunctions()
    {
        var engine = new JsEngine();
        
        var max = await engine.Evaluate("Math.max(1, 5, 3, 9, 2);");
        Assert.Equal(9d, max);
        
        var min = await engine.Evaluate("Math.min(1, 5, 3, 9, 2);");
        Assert.Equal(1d, min);
    }

    [Fact]
    public async Task MathRandomReturnsBetweenZeroAndOne()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Math.random();");
        
        Assert.IsType<double>(result);
        var value = (double)result;
        Assert.True(value >= 0 && value < 1);
    }

    [Fact]
    public async Task MathTrigonometricFunctions()
    {
        var engine = new JsEngine();
        
        // Test sin(PI/2) = 1
        var sin = await engine.Evaluate("Math.sin(Math.PI / 2);");
        Assert.Equal(1d, (double)sin!, precision: 10);
        
        // Test cos(PI) = -1
        var cos = await engine.Evaluate("Math.cos(Math.PI);");
        Assert.Equal(-1d, (double)cos!, precision: 10);
        
        // Test tan(PI/4) ≈ 1
        var tan = await engine.Evaluate("Math.tan(Math.PI / 4);");
        Assert.Equal(1d, (double)tan!, precision: 10);
    }

    [Fact]
    public async Task MathLogarithmicFunctions()
    {
        var engine = new JsEngine();
        
        var log = await engine.Evaluate("Math.log(Math.E);");
        Assert.Equal(1d, (double)log!, precision: 10);
        
        var log10 = await engine.Evaluate("Math.log10(100);");
        Assert.Equal(2d, (double)log10!, precision: 10);
        
        var exp = await engine.Evaluate("Math.exp(1);");
        Assert.Equal(Math.E, (double)exp!, precision: 10);
    }

    [Fact]
    public async Task MathCanBeUsedInComplexExpressions()
    {
        var engine = new JsEngine();
        
        // Calculate hypotenuse: sqrt(3^2 + 4^2) = 5
        var result = await engine.Evaluate(@"
let a = 3;
let b = 4;
let c = Math.sqrt(Math.pow(a, 2) + Math.pow(b, 2));
c;
");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task MathSignReturnsSignOfNumber()
    {
        var engine = new JsEngine();
        
        var positive = await engine.Evaluate("Math.sign(10);");
        Assert.Equal(1, positive);
        
        var negative = await engine.Evaluate("Math.sign(-5);");
        Assert.Equal(-1, negative);
        
        var zero = await engine.Evaluate("Math.sign(0);");
        Assert.Equal(0, zero);
    }

    [Fact]
    public async Task MathTruncRemovesDecimalPart()
    {
        var engine = new JsEngine();
        
        var positive = await engine.Evaluate("Math.trunc(4.9);");
        Assert.Equal(4d, positive);
        
        var negative = await engine.Evaluate("Math.trunc(-4.9);");
        Assert.Equal(-4d, negative);
    }

    [Fact]
    public async Task ArrayMapTransformsElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4];
let doubled = numbers.map(function(x) { return x * 2; });
doubled[0] + doubled[1] + doubled[2] + doubled[3];
");
        Assert.Equal(20d, result); // 2 + 4 + 6 + 8
    }

    [Fact]
    public async Task ArrayFilterSelectsElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5, 6];
let greaterThanThree = numbers.filter(function(x) { return x > 3; });
greaterThanThree[""length""];
");
        Assert.Equal(3d, result); // [4, 5, 6]
    }

    [Fact]
    public async Task ArrayReduceAccumulatesValues()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5];
let sum = numbers.reduce(function(acc, x, i, arr) { return acc + x; }, 0);
sum;
");
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task ArrayForEachIteratesElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3];
let sum = 0;
numbers.forEach(function(x) { sum = sum + x; });
sum;
");
        Assert.Equal(6d, result);
    }

    [Fact]
    public async Task ArrayFindReturnsFirstMatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5];
let found = numbers.find(function(x) { return x > 3; });
found;
");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task ArrayFindIndexReturnsIndexOfFirstMatch()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5];
let index = numbers.findIndex(function(x) { return x > 3; });
index;
");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task ArraySomeReturnsTrueIfAnyMatch()
    {
        var engine = new JsEngine();
        
        var hasLarge = await engine.Evaluate(@"
let numbers = [1, 3, 5, 6];
numbers.some(function(x, i, arr) { return x > 5; });
");
        Assert.True((bool)hasLarge!);

        var hasNegative = await engine.Evaluate(@"
let numbers = [1, 2, 3];
numbers.some(function(x, i, arr) { return x < 0; });
");
        Assert.False((bool)hasNegative!);
    }

    [Fact]
    public async Task ArrayEveryReturnsTrueIfAllMatch()
    {
        var engine = new JsEngine();
        
        var allPositive = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4];
numbers.every(function(x, i, arr) { return x > 0; });
");
        Assert.True((bool)allPositive!);

        var allLarge = await engine.Evaluate(@"
let numbers = [2, 3, 4];
numbers.every(function(x, i, arr) { return x > 3; });
");
        Assert.False((bool)allLarge!);
    }

    [Fact]
    public async Task ArrayJoinConcatenatesElements()
    {
        var engine = new JsEngine();
        
        var withComma = await engine.Evaluate(@"
let items = [""a"", ""b"", ""c""];
items.join("","");
");
        Assert.Equal("a,b,c", withComma);

        var withDash = await engine.Evaluate(@"
let items = [""x"", ""y"", ""z""];
items.join(""-"");
");
        Assert.Equal("x-y-z", withDash);
    }

    [Fact]
    public async Task ArrayIncludesChecksForElement()
    {
        var engine = new JsEngine();
        
        var hasTwo = await engine.Evaluate(@"
let numbers = [1, 2, 3];
numbers.includes(2);
");
        Assert.True((bool)hasTwo!);

        var hasFive = await engine.Evaluate(@"
let numbers = [1, 2, 3];
numbers.includes(5);
");
        Assert.False((bool)hasFive!);
    }

    [Fact]
    public async Task ArrayIndexOfFindsElementPosition()
    {
        var engine = new JsEngine();
        
        var index = await engine.Evaluate(@"
let items = [""a"", ""b"", ""c"", ""b""];
items.indexOf(""b"");
");
        Assert.Equal(1d, index);

        var notFound = await engine.Evaluate(@"
let items = [""a"", ""b"", ""c""];
items.indexOf(""d"");
");
        Assert.Equal(-1d, notFound);
    }

    [Fact]
    public async Task ArraySliceExtractsSubarray()
    {
        var engine = new JsEngine();
        
        var result = await engine.Evaluate(@"
let numbers = [0, 1, 2, 3, 4];
let slice = numbers.slice(1, 3);
slice[0] + slice[1];
");
        Assert.Equal(3d, result); // [1, 2]
    }

    [Fact]
    public async Task ArrayMethodsCanBeChained()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5, 6];
let result = numbers
    .filter(function(x, i, arr) { return x > 3; })
    .map(function(x, i, arr) { return x * 2; })
    .reduce(function(acc, x, i, arr) { return acc + x; }, 0);
result;
");
        Assert.Equal(30d, result); // filter: [4,5,6], map: [8,10,12], reduce: 30
    }

    [Fact]
    public async Task ArrayPushAddsElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3];
numbers.push(4);
numbers.push(5);
numbers[""length""];
");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task ArrayPopRemovesLastElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4];
let last = numbers.pop();
last;
");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task ArrayShiftRemovesFirstElement()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [10, 20, 30];
let first = numbers.shift();
first;
");
        Assert.Equal(10d, result);
    }

    [Fact]
    public async Task ArrayUnshiftAddsToBeginning()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [3, 4];
numbers.unshift(1, 2);
numbers[0];
");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task ArraySpliceRemovesAndInserts()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4, 5];
let removed = numbers.splice(2, 2, 99);
numbers[2];
");
        Assert.Equal(99d, result);
    }

    [Fact]
    public async Task ArrayConcatCombinesArrays()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let arr1 = [1, 2];
let arr2 = [3, 4];
let combined = arr1.concat(arr2);
combined[3];
");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task ArrayReverseReversesInPlace()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [1, 2, 3, 4];
numbers.reverse();
numbers[0];
");
        Assert.Equal(4d, result);
    }

    [Fact]
    public async Task ArraySortSortsElements()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let numbers = [3, 1, 4, 1, 5, 9, 2, 6];
numbers.sort(function(a, b) { return a - b; });
numbers[0];
");
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task DateNowReturnsMilliseconds()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("Date.now();");
        Assert.IsType<double>(result);
        Assert.True((double)result > 0);
    }

    [Fact]
    public async Task DateConstructorCreatesInstance()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let d = new Date(2024, 0, 15);
d.getFullYear();
");
        Assert.Equal(2024d, result);
    }

    [Fact]
    public async Task DateGetMonthReturnsZeroIndexed()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let d = new Date(2024, 5, 15);
d.getMonth();
");
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task DateToISOStringReturnsFormattedString()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let d = new Date(2024, 0, 1);
d.toISOString();
");
        Assert.IsType<string>(result);
        Assert.Contains("2024", (string)result);
    }

    [Fact]
    public async Task JsonParseHandlesObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let jsonStr = `{""name"":""Alice"",""age"":30}`;
let obj = JSON.parse(jsonStr);
obj.name;
");
        Assert.Equal("Alice", result);
    }

    [Fact]
    public async Task JsonParseHandlesArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let jsonStr = `[1,2,3,4]`;
let arr = JSON.parse(jsonStr);
arr[2];
");
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task JsonStringifyHandlesObject()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let obj = { name: ""Bob"", age: 25 };
JSON.stringify(obj);
");
        Assert.IsType<string>(result);
        Assert.Contains("Bob", (string)result);
    }

    [Fact]
    public async Task JsonStringifyHandlesArray()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let arr = [1, 2, 3];
JSON.stringify(arr);
");
        Assert.Equal("[1,2,3]", result);
    }
}
