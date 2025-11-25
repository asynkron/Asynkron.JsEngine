namespace Asynkron.JsEngine.Tests;

public class JsEvaluatorTests
{
    [Fact(Timeout = 2000)]
    public async Task EvaluateArithmeticAndVariableLookup()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let answer = 1 + 2 * 3; answer;");
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateFunctionDeclarationAndInvocation()
    {
        await using var engine = new JsEngine();
        var source = "function add(a, b) { return a + b; } let result = add(2, 3); result;";
        var result = await engine.Evaluate(source);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateClosureCapturesOuterVariable()
    {
        await using var engine = new JsEngine();
        var source =
            "function makeAdder(x) { function inner(y) { return x + y; } return inner; } let plusTen = makeAdder(10); let fifteen = plusTen(5); fifteen;";
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateFunctionExpression()
    {
        await using var engine = new JsEngine();
        var source = "let add = function(a, b) { return a + b; }; add(4, 5);";
        var result = await engine.Evaluate(source);
        Assert.Equal(9d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task HostFunctionInterop()
    {
        var captured = new List<object?>();
        await using var engine = new JsEngine();
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

    [Fact(Timeout = 2000)]
    public async Task EvaluateObjectLiteralAndPropertyUsage()
    {
        await using var engine = new JsEngine();
        var source = "let obj = { a: 10, x: function () { return 5; } }; let total = obj.a + obj.x(); total;";

        var result = await engine.Evaluate(source);

        Assert.Equal(15d, result); // object property read plus function invocation
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateArrayLiteralSupportsIndexing()
    {
        await using var engine = new JsEngine();
        var source = """

                     let values = [1, 2];
                     values[2] = values[0] + values[1];
                     let alias = values["length"];
                     let missing = values[5];
                     if (missing == null) { missing = 1; } else { missing = 0; }
                     alias + missing + values[2];

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(7d, result); // length reflects new entry and missing reads return null
    }

    [Fact(Timeout = 2000)]
    public async Task LogicalOperatorsShortCircuitAndReturnOperands()
    {
        await using var engine = new JsEngine();
        var source = """

                     let hits = 0;
                     function record(value) {
                         hits = hits + 1;
                         return value; // propagate the input to observe operator return values
                     }

                     let andResult = false && record(1);
                     let orResult = true || record(2);
                     let coalesceResult = null ?? record(3);
                     let coalesceNonNull = 0 ?? record(4);

                     """;

        var temp = await engine.Evaluate(source);

        Assert.Equal(1d, await engine.Evaluate("hits;")); // only the nullish coalescing branch invokes record
        Assert.False(Assert.IsType<bool>(await engine.Evaluate("andResult;")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("orResult;")));
        Assert.Equal(3d, await engine.Evaluate("coalesceResult;"));
        Assert.Equal(0d, await engine.Evaluate("coalesceNonNull;"));
    }

    [Fact(Timeout = 2000)]
    public async Task StrictEqualityRequiresMatchingTypes()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("getInt", _ => 1);

        var source = """

                     let outcomes = [
                         1 === 1,
                         1 === "1",
                         1 !== 2,
                         null === null,
                         getInt() === 1
                     ];
                     outcomes;

                     """;

        var temp = await engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(await engine.Evaluate("outcomes[0];")));
        Assert.False(Assert.IsType<bool>(await engine.Evaluate("outcomes[1];")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("outcomes[2];")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("outcomes[3];")));
        Assert.True(Assert.IsType<bool>(await engine.Evaluate("outcomes[4];")));
    }

    [Fact(Timeout = 2000)]
    public async Task VarDeclarationHoistsToFunctionScope()
    {
        await using var engine = new JsEngine();
        var source = """

                     function sample() {
                         if (true) {
                             var hidden = 41;
                         }

                         return hidden + 1;
                     }
                     sample();

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstAssignmentThrows()
    {
        await using var engine = new JsEngine();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("const fixed = 1; fixed = 2;"));
    }

    [Fact(Timeout = 2000)]
    public async Task TryCatchFinallyBindsThrownValueAndRunsCleanup()
    {
        await using var engine = new JsEngine();
        var source = """

                     let captured = 0;
                     try {
                         throw 21;
                     } catch (err) {
                         captured = err;
                     } finally {
                         captured = captured + 21;
                     }
                     captured;

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(42d, result); // catch observes thrown value and finally still executes
    }

    [Fact(Timeout = 2000)]
    public async Task FinallyRunsForUnhandledThrow()
    {
        await using var engine = new JsEngine();
        var source = """

                     let cleanup = 0;
                     try {
                         throw "boom";
                     } finally {
                         cleanup = cleanup + 1;
                     }

                     """;

        await Assert.ThrowsAnyAsync<Exception>(async () => await engine.Evaluate(source));

        var cleanupValue = await engine.Evaluate("cleanup;");
        Assert.Equal(1d, cleanupValue); // finally executed even though the throw escaped
    }

    [Fact(Timeout = 2000)]
    public async Task FinallyReturnOverridesTryReturn()
    {
        await using var engine = new JsEngine();
        var source = """

                     function sample() {
                         try {
                             return 1;
                         } finally {
                             return 2;
                         }
                     }
                     sample();

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(2d, result); // return inside finally shadows earlier return
    }

    [Fact(Timeout = 2000)]
    public async Task MethodInvocationBindsThis()
    {
        await using var engine = new JsEngine();
        var source = "let obj = { x: 10, f: function () { return this.x; } }; obj.f();";

        var result = await engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task IndexedMethodInvocationBindsThis()
    {
        await using var engine = new JsEngine();
        var source = """

                     let obj = { value: 10, getter: function() { return this.value; } };
                     obj["getter"]();

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task HostFunctionReceivesThisBinding()
    {
        await using var engine = new JsEngine();
        engine.SetGlobalFunction("reflectThis", (self, _) => self);

        var result = await engine.Evaluate("let obj = { value: 42, reflect: reflectThis }; obj.reflect();");

        var thisBinding = Assert.IsAssignableFrom<IDictionary<string, object?>>(result);
        Assert.Equal(42d, thisBinding["value"]);
    }

    [Fact(Timeout = 2000)]
    public async Task PrototypeLookupResolvesInheritedMethods()
    {
        await using var engine = new JsEngine();
        var source = """

                     let base = {
                         multiplier: 2,
                         calculate: function(value) { return value * this.multiplier; }
                     };
                     let derived = { value: 7, __proto__: base };
                     derived.calculate(derived.value);

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(14d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrototypeAssignmentLinksObjectsAfterCreation()
    {
        await using var engine = new JsEngine();
        var source = """

                     let base = { greet: function() { return "hi " + this.name; } };
                     let user = { name: "Alice" };
                     user.__proto__ = base;
                     user.greet();

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal("hi Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task NewCreatesInstancesWithConstructorPrototypes()
    {
        await using var engine = new JsEngine();
        var source = """

                     function Person(name) {
                         this.name = name;
                     }
                     Person.prototype.describe = function() { return "Person:" + this.name; };
                     let person = new Person("Bob");
                     person.describe();

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal("Person:Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task MethodClosuresCanReachThisViaCapturedReference()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(18d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DistinctMethodCallsProvideIndependentThisBindings()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassDeclarationSupportsConstructorsAndMethods()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassWithoutExplicitConstructorFallsBackToDefault()
    {
        await using var engine = new JsEngine();
        var source = """

                     class Widget {
                         describe() { return "widget"; }
                     }
                     let widget = new Widget();
                     Widget.prototype.constructor == Widget;

                     """;

        var result = await engine.Evaluate(source);

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact(Timeout = 2000)]
    public async Task ClassInheritanceSupportsSuperConstructorAndMethodCalls()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateIfElseAndBlockScopes()
    {
        await using var engine = new JsEngine();
        var source = """

                     let value = 0;
                     if (false) {
                         value = 1;
                     } else {
                         value = 2;
                     }
                     value;

                     """;

        var result = await engine.Evaluate(source);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateWhileLoopUpdatesValues()
    {
        await using var engine = new JsEngine();
        var source = """

                     let total = 0;
                     let current = 1;
                     while (current <= 3) {
                         total = total + current;
                         current = current + 1;
                     }
                     total;

                     """;

        var result = await engine.Evaluate(source);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateForLoopHonoursBreakAndContinue()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);
        Assert.Equal(7d, result); // adds 0 + 1 + 2 + 4 before breaking at 5
    }

    [Fact(Timeout = 2000)]
    public async Task SwitchStatementSupportsFallthrough()
    {
        await using var engine = new JsEngine();
        var source = """

                     function describe(value) {
                         switch (value) {
                             case 1:
                                 return "one";
                             case 2:
                             case 3:
                                 return "few";
                             default:
                                 return "many";
                         }
                     }
                     describe(3);

                     """;

        var result = await engine.Evaluate(source);

        Assert.Equal("few", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SwitchBreakRemainsInsideLoop()
    {
        await using var engine = new JsEngine();
        var source = """

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

                     """;

        var result = await engine.Evaluate(source);

        // Ensure the break only exits the switch and not the outer loop.
        Assert.Equal(12d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EvaluateDoWhileRunsBodyAtLeastOnce()
    {
        await using var engine = new JsEngine();
        var source = """

                     let attempts = 0;
                     do {
                         attempts = attempts + 1;
                     } while (false);
                     attempts;

                     """;

        var result = await engine.Evaluate(source);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorReturnsThenBranchWhenConditionIsTrue()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("true ? 10 : 20;");
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorReturnsElseBranchWhenConditionIsFalse()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("false ? 10 : 20;");
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorEvaluatesConditionForTruthiness()
    {
        await using var engine = new JsEngine();
        var source = """

                     let x = 5;
                     let result = x > 3 ? "big" : "small";
                     result;

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal("big", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorWithZeroAsFalsyCondition()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""0 ? "yes" : "no";""");
        Assert.Equal("no", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorWithNullAsFalsyCondition()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("null ? 1 : 2;");
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorCanBeNested()
    {
        await using var engine = new JsEngine();
        var source = """

                     let score = 85;
                     let grade = score >= 90 ? "A" : score >= 80 ? "B" : score >= 70 ? "C" : "D";
                     grade;

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal("B", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorOnlyEvaluatesSelectedBranch()
    {
        await using var engine = new JsEngine();
        var source = """

                     let sideEffect = 0;
                     function increment() {
                         sideEffect = sideEffect + 1;
                         return sideEffect;
                     }
                     let result = true ? 100 : increment();
                     sideEffect;

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(0d, result); // increment should not be called
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorWorksInComplexExpressions()
    {
        await using var engine = new JsEngine();
        var source = """

                     let a = 5;
                     let b = 10;
                     let max = a > b ? a : b;
                     let doubled = (max === 10 ? max : 0) * 2;
                     doubled;

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TernaryOperatorInFunctionReturn()
    {
        await using var engine = new JsEngine();
        var source = """

                     function absoluteValue(x) {
                         return x >= 0 ? x : -x;
                     }
                     absoluteValue(-42);

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithSimpleString()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("`hello world`;");
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithSingleExpression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 42; `The answer is ${x}`;");
        Assert.Equal("The answer is 42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithMultipleExpressions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let a = 10; let b = 20; `${a} + ${b} = ${a + b}`;");
        Assert.Equal("10 + 20 = 30", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithStringInterpolation()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let name = "Alice";
                                           let age = 30;
                                           `Hello, my name is ${name} and I am ${age} years old.`;

                                           """);
        Assert.Equal("Hello, my name is Alice and I am 30 years old.", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithComplexExpressions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           function greet(name) { return "Hello, " + name; }
                                           let user = "Bob";
                                           `${greet(user)}! You have ${3 * 5} messages.`;

                                           """);
        Assert.Equal("Hello, Bob! You have 15 messages.", result);
    }

    [Fact(Timeout = 2000)]
    public async Task TemplateLiteralWithBooleanAndNull()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("`true: ${true}, false: ${false}, null: ${null}`;");
        Assert.Equal("true: true, false: false, null: null", result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetterInObjectLiteral()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let obj = {
                                               _value: 42,
                                               get value() { return this._value; }
                                           };
                                           obj.value;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SetterInObjectLiteral()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let obj = {
                                               _value: 0,
                                               set value(v) { this._value = v * 2; }
                                           };
                                           obj.value = 21;
                                           obj._value;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetterAndSetterTogether()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let obj = {
                                               _temp: 0,
                                               get celsius() { return this._temp; },
                                               set celsius(c) { this._temp = c; },
                                               get fahrenheit() { return this._temp * 9 / 5 + 32; },
                                               set fahrenheit(f) { this._temp = (f - 32) * 5 / 9; }
                                           };
                                           obj.celsius = 100;
                                           obj.fahrenheit;

                                           """);
        Assert.Equal(212d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task GetterInClass()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

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

                                           """);
        Assert.Equal(50d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SetterInClass()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           class Person {
                                               constructor(firstName, lastName) {
                                                   this.firstName = firstName;
                                                   this.lastName = lastName;
                                               }
                                               get fullName() {
                                                   return this.firstName + " " + this.lastName;
                                               }
                                               set fullName(name) {
                                                   this.firstName = "Updated";
                                                   this.lastName = name;
                                               }
                                           }
                                           let person = new Person("John", "Doe");
                                           person.fullName = "Smith";
                                           person.fullName;

                                           """);
        Assert.Equal("Updated Smith", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RestParameterCollectsRemainingArguments()
    {
        await using var engine = new JsEngine();
        var source = """

                     function sum(first, ...rest) {
                         let total = first;
                         let i = 0;
                         while (i < rest["length"]) {
                             total = total + rest[i];
                             i = i + 1;
                         }
                         return total;
                     }
                     sum(1, 2, 3, 4, 5);

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task RestParameterWithNoExtraArgumentsCreatesEmptyArray()
    {
        await using var engine = new JsEngine();
        var source = """

                     function test(a, ...rest) {
                         return rest["length"];
                     }
                     test(1);

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SpreadOperatorInArrayLiteral()
    {
        await using var engine = new JsEngine();
        var source = """

                     let arr1 = [1, 2, 3];
                     let arr2 = [4, 5, 6];
                     let combined = [0, ...arr1, ...arr2, 7];
                     combined[0] + combined[1] + combined[2] + combined[3] + combined[4] + combined[5] + combined[6] + combined[7];

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(28d, result); // 0+1+2+3+4+5+6+7
    }

    [Fact(Timeout = 2000)]
    public async Task SpreadOperatorInFunctionCall()
    {
        await using var engine = new JsEngine();
        var source = """

                     function add(a, b, c) {
                         return a + b + c;
                     }
                     let numbers = [10, 20, 30];
                     add(...numbers);

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(60d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task SpreadOperatorWithMixedArguments()
    {
        await using var engine = new JsEngine();
        var source = """

                     function greet(greeting, name1, name2) {
                         return greeting + " " + name1 + " and " + name2;
                     }
                     let names = ["Alice", "Bob"];
                     greet("Hello", ...names);

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal("Hello Alice and Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task RestParameterWithSpreadInCall()
    {
        await using var engine = new JsEngine();
        var source = """

                     function joinAll(...items) {
                         let result = "";
                         let i = 0;
                         while (i < items["length"]) {
                             if (i > 0) {
                                 result = result + ",";
                             }
                             result = result + items[i];
                             i = i + 1;
                         }
                         return result;
                     }
                     let arr = ["b", "c"];
                     joinAll("a", ...arr, "d");

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal("a,b,c,d", result);
    }

    [Fact(Timeout = 2000)]
    public async Task SpreadInNestedArrays()
    {
        await using var engine = new JsEngine();
        var source = """

                     let inner = [2, 3];
                     let outer = [1, ...inner, 4];
                     let final = [0, ...outer, 5];
                     final[0] + final[1] + final[2] + final[3] + final[4] + final[5];

                     """;
        var result = await engine.Evaluate(source);
        Assert.Equal(15d, result); // 0+1+2+3+4+5
    }

    [Fact(Timeout = 2000)]
    public async Task MathObjectProvidesConstants()
    {
        await using var engine = new JsEngine();

        var pi = await engine.Evaluate("Math.PI;");
        Assert.Equal(Math.PI, pi);

        var e = await engine.Evaluate("Math.E;");
        Assert.Equal(Math.E, e);

        var sqrt2 = await engine.Evaluate("Math.SQRT2;");
        Assert.Equal(Math.Sqrt(2), sqrt2);
    }

    [Fact(Timeout = 2000)]
    public async Task MathSqrtCalculatesSquareRoot()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.sqrt(16);");
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MathPowCalculatesPower()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.pow(2, 3);");
        Assert.Equal(8d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MathAbsReturnsAbsoluteValue()
    {
        await using var engine = new JsEngine();

        var positive = await engine.Evaluate("Math.abs(-5);");
        Assert.Equal(5d, positive);

        var alreadyPositive = await engine.Evaluate("Math.abs(3);");
        Assert.Equal(3d, alreadyPositive);
    }

    [Fact(Timeout = 2000)]
    public async Task MathFloorCeilRound()
    {
        await using var engine = new JsEngine();

        var floor = await engine.Evaluate("Math.floor(4.7);");
        Assert.Equal(4d, floor);

        var ceil = await engine.Evaluate("Math.ceil(4.3);");
        Assert.Equal(5d, ceil);

        var round = await engine.Evaluate("Math.round(4.5);");
        Assert.Equal(5d, round);
    }

    [Fact(Timeout = 2000)]
    public async Task MathMaxMinFunctions()
    {
        await using var engine = new JsEngine();

        var max = await engine.Evaluate("Math.max(1, 5, 3, 9, 2);");
        Assert.Equal(9d, max);

        var min = await engine.Evaluate("Math.min(1, 5, 3, 9, 2);");
        Assert.Equal(1d, min);
    }

    [Fact(Timeout = 2000)]
    public async Task MathRandomReturnsBetweenZeroAndOne()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Math.random();");

        Assert.IsType<double>(result);
        var value = (double)result;
        Assert.True(value is >= 0 and < 1);
    }

    [Fact(Timeout = 2000)]
    public async Task MathTrigonometricFunctions()
    {
        await using var engine = new JsEngine();

        // Test sin(PI/2) = 1
        var sin = await engine.Evaluate("Math.sin(Math.PI / 2);");
        Assert.Equal(1d, (double)sin!, 10);

        // Test cos(PI) = -1
        var cos = await engine.Evaluate("Math.cos(Math.PI);");
        Assert.Equal(-1d, (double)cos!, 10);

        // Test tan(PI/4) ≈ 1
        var tan = await engine.Evaluate("Math.tan(Math.PI / 4);");
        Assert.Equal(1d, (double)tan!, 10);
    }

    [Fact(Timeout = 2000)]
    public async Task MathLogarithmicFunctions()
    {
        await using var engine = new JsEngine();

        var log = await engine.Evaluate("Math.log(Math.E);");
        Assert.Equal(1d, (double)log!, 10);

        var log10 = await engine.Evaluate("Math.log10(100);");
        Assert.Equal(2d, (double)log10!, 10);

        var exp = await engine.Evaluate("Math.exp(1);");
        Assert.Equal(Math.E, (double)exp!, 10);
    }

    [Fact(Timeout = 2000)]
    public async Task MathCanBeUsedInComplexExpressions()
    {
        await using var engine = new JsEngine();

        // Calculate hypotenuse: sqrt(3^2 + 4^2) = 5
        var result = await engine.Evaluate("""

                                           let a = 3;
                                           let b = 4;
                                           let c = Math.sqrt(Math.pow(a, 2) + Math.pow(b, 2));
                                           c;

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MathSignReturnsSignOfNumber()
    {
        await using var engine = new JsEngine();

        var positive = await engine.Evaluate("Math.sign(10);");
        Assert.Equal(1, positive);

        var negative = await engine.Evaluate("Math.sign(-5);");
        Assert.Equal(-1, negative);

        var zero = await engine.Evaluate("Math.sign(0);");
        Assert.Equal(0, zero);
    }

    [Fact(Timeout = 2000)]
    public async Task MathTruncRemovesDecimalPart()
    {
        await using var engine = new JsEngine();

        var positive = await engine.Evaluate("Math.trunc(4.9);");
        Assert.Equal(4d, positive);

        var negative = await engine.Evaluate("Math.trunc(-4.9);");
        Assert.Equal(-4d, negative);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayMapTransformsElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4];
                                           let doubled = numbers.map(function(x) { return x * 2; });
                                           doubled[0] + doubled[1] + doubled[2] + doubled[3];

                                           """);
        Assert.Equal(20d, result); // 2 + 4 + 6 + 8
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayFilterSelectsElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5, 6];
                                           let greaterThanThree = numbers.filter(function(x) { return x > 3; });
                                           greaterThanThree["length"];

                                           """);
        Assert.Equal(3d, result); // [4, 5, 6]
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayReduceAccumulatesValues()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5];
                                           let sum = numbers.reduce(function(acc, x, i, arr) { return acc + x; }, 0);
                                           sum;

                                           """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayForEachIteratesElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3];
                                           let sum = 0;
                                           numbers.forEach(function(x) { sum = sum + x; });
                                           sum;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayFindReturnsFirstMatch()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5];
                                           let found = numbers.find(function(x) { return x > 3; });
                                           found;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayFindIndexReturnsIndexOfFirstMatch()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5];
                                           let index = numbers.findIndex(function(x) { return x > 3; });
                                           index;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArraySomeReturnsTrueIfAnyMatch()
    {
        await using var engine = new JsEngine();

        var hasLarge = await engine.Evaluate("""

                                             let numbers = [1, 3, 5, 6];
                                             numbers.some(function(x, i, arr) { return x > 5; });

                                             """);
        Assert.True((bool)hasLarge!);

        var hasNegative = await engine.Evaluate("""

                                                let numbers = [1, 2, 3];
                                                numbers.some(function(x, i, arr) { return x < 0; });

                                                """);
        Assert.False((bool)hasNegative!);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ArrayEveryReturnsTrueIfAllMatch()
    {
        await using var engine = new JsEngine();

        var allPositive = await engine.Evaluate("""

                                                let numbers = [1, 2, 3, 4];
                                                numbers.every(function(x, i, arr) { return x > 0; });

                                                """);
        Assert.True((bool)allPositive!);

        var allLarge = await engine.Evaluate("""

                                             let numbers = [2, 3, 4];
                                             numbers.every(function(x, i, arr) { return x > 3; });

                                             """);
        Assert.False((bool)allLarge!);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayJoinConcatenatesElements()
    {
        await using var engine = new JsEngine();

        var withComma = await engine.Evaluate("""

                                              let items = ["a", "b", "c"];
                                              items.join(",");

                                              """);
        Assert.Equal("a,b,c", withComma);

        var withDash = await engine.Evaluate("""

                                             let items = ["x", "y", "z"];
                                             items.join("-");

                                             """);
        Assert.Equal("x-y-z", withDash);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayIncludesChecksForElement()
    {
        await using var engine = new JsEngine();

        var hasTwo = await engine.Evaluate("""

                                           let numbers = [1, 2, 3];
                                           numbers.includes(2);

                                           """);
        Assert.True((bool)hasTwo!);

        var hasFive = await engine.Evaluate("""

                                            let numbers = [1, 2, 3];
                                            numbers.includes(5);

                                            """);
        Assert.False((bool)hasFive!);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayIndexOfFindsElementPosition()
    {
        await using var engine = new JsEngine();

        var index = await engine.Evaluate("""

                                          let items = ["a", "b", "c", "b"];
                                          items.indexOf("b");

                                          """);
        Assert.Equal(1d, index);

        var notFound = await engine.Evaluate("""

                                             let items = ["a", "b", "c"];
                                             items.indexOf("d");

                                             """);
        Assert.Equal(-1d, notFound);
    }

    [Fact(Timeout = 2000)]
    public async Task ArraySliceExtractsSubarray()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""

                                           let numbers = [0, 1, 2, 3, 4];
                                           let slice = numbers.slice(1, 3);
                                           slice[0] + slice[1];

                                           """);
        Assert.Equal(3d, result); // [1, 2]
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayMethodsCanBeChained()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5, 6];
                                           let result = numbers
                                               .filter(function(x, i, arr) { return x > 3; })
                                               .map(function(x, i, arr) { return x * 2; })
                                               .reduce(function(acc, x, i, arr) { return acc + x; }, 0);
                                           result;

                                           """);
        Assert.Equal(30d, result); // filter: [4,5,6], map: [8,10,12], reduce: 30
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayPushAddsElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3];
                                           numbers.push(4);
                                           numbers.push(5);
                                           numbers["length"];

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayPopRemovesLastElement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4];
                                           let last = numbers.pop();
                                           last;

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayShiftRemovesFirstElement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [10, 20, 30];
                                           let first = numbers.shift();
                                           first;

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayUnshiftAddsToBeginning()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [3, 4];
                                           numbers.unshift(1, 2);
                                           numbers[0];

                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArraySpliceRemovesAndInserts()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4, 5];
                                           let removed = numbers.splice(2, 2, 99);
                                           numbers[2];

                                           """);
        Assert.Equal(99d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayConcatCombinesArrays()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let arr1 = [1, 2];
                                           let arr2 = [3, 4];
                                           let combined = arr1.concat(arr2);
                                           combined[3];

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayReverseReversesInPlace()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [1, 2, 3, 4];
                                           numbers.reverse();
                                           numbers[0];

                                           """);
        Assert.Equal(4d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArraySortSortsElements()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let numbers = [3, 1, 4, 1, 5, 9, 2, 6];
                                           numbers.sort(function(a, b) { return a - b; });
                                           numbers[0];

                                           """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DateNowReturnsMilliseconds()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("Date.now();");
        Assert.IsType<double>(result);
        Assert.True((double)result > 0);
    }

    [Fact(Timeout = 2000)]
    public async Task DateConstructorCreatesInstance()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let d = new Date(2024, 0, 15);
                                           d.getFullYear();

                                           """);
        Assert.Equal(2024d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DateGetMonthReturnsZeroIndexed()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let d = new Date(2024, 5, 15);
                                           d.getMonth();

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task DateToIsoStringReturnsFormattedString()
    {
        //TODO: the task here is to make the evaluated result match, without changing the JS code, or the test code.
        //you may only touch the JsEngine and related types .
        //I suspect this is a GMT+1 issue, that on my local machine, we get 2023-12-31T23:00:00.000Z instead of 2024-01-01T00:00:00.000Z

        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let d = new Date(2024, 0, 1);
                                           d.toISOString();

                                           """);
        Assert.IsType<string>(result);
        Assert.Contains("2024", (string)result);
    }

    [Fact(Timeout = 2000)]
    public async Task JsonParseHandlesObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let jsonStr = `{"name":"Alice","age":30}`;
                                           let obj = JSON.parse(jsonStr);
                                           obj.name;

                                           """);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task JsonParseHandlesArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let jsonStr = `[1,2,3,4]`;
                                           let arr = JSON.parse(jsonStr);
                                           arr[2];

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task JsonStringifyHandlesObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let obj = { name: "Bob", age: 25 };
                                           JSON.stringify(obj);

                                           """);
        Assert.IsType<string>(result);
        Assert.Contains("Bob", (string)result);
    }

    [Fact(Timeout = 2000)]
    public async Task JsonStringifyHandlesArray()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                           let arr = [1, 2, 3];
                                           JSON.stringify(arr);

                                           """);
        Assert.Equal("[1,2,3]", result);
    }
}
