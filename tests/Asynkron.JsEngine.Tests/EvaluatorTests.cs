using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class EvaluatorTests
{
    [Fact]
    public void EvaluateArithmeticAndVariableLookup()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let answer = 1 + 2 * 3; answer;");
        Assert.Equal(7d, result);
    }

    [Fact]
    public void EvaluateFunctionDeclarationAndInvocation()
    {
        var engine = new JsEngine();
        var source = "function add(a, b) { return a + b; } let result = add(2, 3); result;";
        var result = engine.Evaluate(source);
        Assert.Equal(5d, result);
    }

    [Fact]
    public void EvaluateClosureCapturesOuterVariable()
    {
        var engine = new JsEngine();
        var source = "function makeAdder(x) { function inner(y) { return x + y; } return inner; } let plusTen = makeAdder(10); let fifteen = plusTen(5); fifteen;";
        var result = engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact]
    public void EvaluateFunctionExpression()
    {
        var engine = new JsEngine();
        var source = "let add = function(a, b) { return a + b; }; add(4, 5);";
        var result = engine.Evaluate(source);
        Assert.Equal(9d, result);
    }

    [Fact]
    public void HostFunctionInterop()
    {
        var captured = new List<object?>();
        var engine = new JsEngine();
        engine.SetGlobalFunction("collect", args =>
        {
            captured.AddRange(args);
            return args.Count;
        });

        var result = engine.Evaluate("collect(\"hello\", 3); collect(\"world\");");

        Assert.Equal(1, result); // last call returns number of args
        Assert.Collection(captured,
            item => Assert.Equal("hello", item),
            item => Assert.Equal(3d, item),
            item => Assert.Equal("world", item));
    }

    [Fact]
    public void EvaluateObjectLiteralAndPropertyUsage()
    {
        var engine = new JsEngine();
        var source = "let obj = { a: 10, x: function () { return 5; } }; let total = obj.a + obj.x(); total;";

        var result = engine.Evaluate(source);

        Assert.Equal(15d, result); // object property read plus function invocation
    }

    [Fact]
    public void EvaluateArrayLiteralSupportsIndexing()
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

        var result = engine.Evaluate(source);

        Assert.Equal(7d, result); // length reflects new entry and missing reads return null
    }

    [Fact]
    public void LogicalOperatorsShortCircuitAndReturnOperands()
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

        engine.Evaluate(source);

        Assert.Equal(1d, engine.Evaluate("hits;")); // only the nullish coalescing branch invokes record
        Assert.False(Assert.IsType<bool>(engine.Evaluate("andResult;")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("orResult;")));
        Assert.Equal(3d, engine.Evaluate("coalesceResult;"));
        Assert.Equal(0d, engine.Evaluate("coalesceNonNull;"));
    }

    [Fact]
    public void StrictEqualityRequiresMatchingTypes()
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

        engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[0];")));
        Assert.False(Assert.IsType<bool>(engine.Evaluate("outcomes[1];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[2];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[3];")));
        Assert.True(Assert.IsType<bool>(engine.Evaluate("outcomes[4];")));
    }

    [Fact]
    public void VarDeclarationHoistsToFunctionScope()
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

        var result = engine.Evaluate(source);

        Assert.Equal(42d, result);
    }

    [Fact]
    public void ConstAssignmentThrows()
    {
        var engine = new JsEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Evaluate("const fixed = 1; fixed = 2;"));
    }

    [Fact]
    public void TryCatchFinallyBindsThrownValueAndRunsCleanup()
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

        var result = engine.Evaluate(source);

        Assert.Equal(42d, result); // catch observes thrown value and finally still executes
    }

    [Fact]
    public void FinallyRunsForUnhandledThrow()
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

        Assert.ThrowsAny<Exception>(() => engine.Evaluate(source));

        var cleanupValue = engine.Evaluate("cleanup;");
        Assert.Equal(1d, cleanupValue); // finally executed even though the throw escaped
    }

    [Fact]
    public void FinallyReturnOverridesTryReturn()
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

        var result = engine.Evaluate(source);

        Assert.Equal(2d, result); // return inside finally shadows earlier return
    }

    [Fact]
    public void MethodInvocationBindsThis()
    {
        var engine = new JsEngine();
        var source = "let obj = { x: 10, f: function () { return this.x; } }; obj.f();";

        var result = engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact]
    public void IndexedMethodInvocationBindsThis()
    {
        var engine = new JsEngine();
        var source = @"
let obj = { value: 10, getter: function() { return this.value; } };
obj[""getter""]();
";

        var result = engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact]
    public void HostFunctionReceivesThisBinding()
    {
        var engine = new JsEngine();
        engine.SetGlobalFunction("reflectThis", (self, _) => self);

        var result = engine.Evaluate("let obj = { value: 42, reflect: reflectThis }; obj.reflect();");

        var thisBinding = Assert.IsAssignableFrom<IDictionary<string, object?>>(result);
        Assert.Equal(42d, thisBinding["value"]);
    }

    [Fact]
    public void PrototypeLookupResolvesInheritedMethods()
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

        var result = engine.Evaluate(source);

        Assert.Equal(14d, result);
    }

    [Fact]
    public void PrototypeAssignmentLinksObjectsAfterCreation()
    {
        var engine = new JsEngine();
        var source = @"
let base = { greet: function() { return ""hi "" + this.name; } };
let user = { name: ""Alice"" };
user.__proto__ = base;
user.greet();
";

        var result = engine.Evaluate(source);

        Assert.Equal("hi Alice", result);
    }

    [Fact]
    public void NewCreatesInstancesWithConstructorPrototypes()
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

        var result = engine.Evaluate(source);

        Assert.Equal("Person:Bob", result);
    }

    [Fact]
    public void MethodClosuresCanReachThisViaCapturedReference()
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

        var result = engine.Evaluate(source);

        Assert.Equal(18d, result);
    }

    [Fact]
    public void DistinctMethodCallsProvideIndependentThisBindings()
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

        var result = engine.Evaluate(source);

        Assert.Equal(15d, result);
    }

    [Fact]
    public void ClassDeclarationSupportsConstructorsAndMethods()
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

        var result = engine.Evaluate(source);

        Assert.Equal(6d, result);
    }

    [Fact]
    public void ClassWithoutExplicitConstructorFallsBackToDefault()
    {
        var engine = new JsEngine();
        var source = @"
class Widget {
    describe() { return ""widget""; }
}
let widget = new Widget();
Widget.prototype.constructor == Widget;
";

        var result = engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ClassInheritanceSupportsSuperConstructorAndMethodCalls()
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

        var result = engine.Evaluate(source);

        Assert.Equal(8d, result);
    }

    [Fact]
    public void EvaluateIfElseAndBlockScopes()
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

        var result = engine.Evaluate(source);
        Assert.Equal(2d, result);
    }

    [Fact]
    public void EvaluateWhileLoopUpdatesValues()
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

        var result = engine.Evaluate(source);
        Assert.Equal(6d, result);
    }

    [Fact]
    public void EvaluateForLoopHonoursBreakAndContinue()
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

        var result = engine.Evaluate(source);
        Assert.Equal(7d, result); // adds 0 + 1 + 2 + 4 before breaking at 5
    }

    [Fact]
    public void SwitchStatementSupportsFallthrough()
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

        var result = engine.Evaluate(source);

        Assert.Equal("few", result);
    }

    [Fact]
    public void SwitchBreakRemainsInsideLoop()
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

        var result = engine.Evaluate(source);

        // Ensure the break only exits the switch and not the outer loop.
        Assert.Equal(12d, result);
    }

    [Fact]
    public void EvaluateDoWhileRunsBodyAtLeastOnce()
    {
        var engine = new JsEngine();
        var source = @"
let attempts = 0;
do {
    attempts = attempts + 1;
} while (false);
attempts;
";

        var result = engine.Evaluate(source);
        Assert.Equal(1d, result);
    }

    [Fact]
    public void TernaryOperatorReturnsThenBranchWhenConditionIsTrue()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("true ? 10 : 20;");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void TernaryOperatorReturnsElseBranchWhenConditionIsFalse()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("false ? 10 : 20;");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void TernaryOperatorEvaluatesConditionForTruthiness()
    {
        var engine = new JsEngine();
        var source = @"
let x = 5;
let result = x > 3 ? ""big"" : ""small"";
result;
";
        var result = engine.Evaluate(source);
        Assert.Equal("big", result);
    }

    [Fact]
    public void TernaryOperatorWithZeroAsFalsyCondition()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"0 ? ""yes"" : ""no"";");
        Assert.Equal("no", result);
    }

    [Fact]
    public void TernaryOperatorWithNullAsFalsyCondition()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("null ? 1 : 2;");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void TernaryOperatorCanBeNested()
    {
        var engine = new JsEngine();
        var source = @"
let score = 85;
let grade = score >= 90 ? ""A"" : score >= 80 ? ""B"" : score >= 70 ? ""C"" : ""D"";
grade;
";
        var result = engine.Evaluate(source);
        Assert.Equal("B", result);
    }

    [Fact]
    public void TernaryOperatorOnlyEvaluatesSelectedBranch()
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
        var result = engine.Evaluate(source);
        Assert.Equal(0d, result); // increment should not be called
    }

    [Fact]
    public void TernaryOperatorWorksInComplexExpressions()
    {
        var engine = new JsEngine();
        var source = @"
let a = 5;
let b = 10;
let max = a > b ? a : b;
let doubled = (max === 10 ? max : 0) * 2;
doubled;
";
        var result = engine.Evaluate(source);
        Assert.Equal(20d, result);
    }

    [Fact]
    public void TernaryOperatorInFunctionReturn()
    {
        var engine = new JsEngine();
        var source = @"
function absoluteValue(x) {
    return x >= 0 ? x : -x;
}
absoluteValue(-42);
";
        var result = engine.Evaluate(source);
        Assert.Equal(42d, result);
    }

    [Fact]
    public void TemplateLiteralWithSimpleString()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("`hello world`;");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void TemplateLiteralWithSingleExpression()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let x = 42; `The answer is ${x}`;");
        Assert.Equal("The answer is 42", result);
    }

    [Fact]
    public void TemplateLiteralWithMultipleExpressions()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("let a = 10; let b = 20; `${a} + ${b} = ${a + b}`;");
        Assert.Equal("10 + 20 = 30", result);
    }

    [Fact]
    public void TemplateLiteralWithStringInterpolation()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
let name = ""Alice"";
let age = 30;
`Hello, my name is ${name} and I am ${age} years old.`;
");
        Assert.Equal("Hello, my name is Alice and I am 30 years old.", result);
    }

    [Fact]
    public void TemplateLiteralWithComplexExpressions()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
function greet(name) { return ""Hello, "" + name; }
let user = ""Bob"";
`${greet(user)}! You have ${3 * 5} messages.`;
");
        Assert.Equal("Hello, Bob! You have 15 messages.", result);
    }

    [Fact]
    public void TemplateLiteralWithBooleanAndNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate("`true: ${true}, false: ${false}, null: ${null}`;");
        Assert.Equal("true: true, false: false, null: null", result);
    }

    [Fact]
    public void GetterInObjectLiteral()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
let obj = {
    _value: 42,
    get value() { return this._value; }
};
obj.value;
");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void SetterInObjectLiteral()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
    public void GetterAndSetterTogether()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
    public void GetterInClass()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
    public void SetterInClass()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
    public void RestParameterCollectsRemainingArguments()
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
        var result = engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact]
    public void RestParameterWithNoExtraArgumentsCreatesEmptyArray()
    {
        var engine = new JsEngine();
        var source = @"
function test(a, ...rest) {
    return rest[""length""];
}
test(1);
";
        var result = engine.Evaluate(source);
        Assert.Equal(0d, result);
    }

    [Fact]
    public void SpreadOperatorInArrayLiteral()
    {
        var engine = new JsEngine();
        var source = @"
let arr1 = [1, 2, 3];
let arr2 = [4, 5, 6];
let combined = [0, ...arr1, ...arr2, 7];
combined[0] + combined[1] + combined[2] + combined[3] + combined[4] + combined[5] + combined[6] + combined[7];
";
        var result = engine.Evaluate(source);
        Assert.Equal(28d, result); // 0+1+2+3+4+5+6+7
    }

    [Fact]
    public void SpreadOperatorInFunctionCall()
    {
        var engine = new JsEngine();
        var source = @"
function add(a, b, c) {
    return a + b + c;
}
let numbers = [10, 20, 30];
add(...numbers);
";
        var result = engine.Evaluate(source);
        Assert.Equal(60d, result);
    }

    [Fact]
    public void SpreadOperatorWithMixedArguments()
    {
        var engine = new JsEngine();
        var source = @"
function greet(greeting, name1, name2) {
    return greeting + "" "" + name1 + "" and "" + name2;
}
let names = [""Alice"", ""Bob""];
greet(""Hello"", ...names);
";
        var result = engine.Evaluate(source);
        Assert.Equal("Hello Alice and Bob", result);
    }

    [Fact]
    public void RestParameterWithSpreadInCall()
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
        var result = engine.Evaluate(source);
        Assert.Equal("a,b,c,d", result);
    }

    [Fact]
    public void SpreadInNestedArrays()
    {
        var engine = new JsEngine();
        var source = @"
let inner = [2, 3];
let outer = [1, ...inner, 4];
let final = [0, ...outer, 5];
final[0] + final[1] + final[2] + final[3] + final[4] + final[5];
";
        var result = engine.Evaluate(source);
        Assert.Equal(15d, result); // 0+1+2+3+4+5
    }
}
