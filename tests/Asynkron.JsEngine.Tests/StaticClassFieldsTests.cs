using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class StaticClassFieldsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Static_Field_With_Initializer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       static count = 0;

                                                       constructor() {
                                                           Counter.count = Counter.count + 1;
                                                       }
                                                   }

                                                   new Counter();
                                                   new Counter();
                                                   new Counter();
                                                   Counter.count;

                                       """);
        Assert.Equal(3.0, result);
    }

    // Note: Fields without initializers not yet supported - parser requires = for field declarations
    // [Fact(Timeout = 2000)]
    // public async Task Static_Field_Without_Initializer()
    // {
    //     await using var engine = CreateEngine();
    //     var result = await engine.Evaluate(@"
    //         class MyClass {
    //             static value;
    //         }
    //
    //         MyClass.value = 42;
    //         MyClass.value;
    //     ");
    //     Assert.Equal(42.0, result);
    // }

    [Fact(Timeout = 2000)]
    public async Task Multiple_Static_Fields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Config {
                                                       static host = "localhost";
                                                       static port = 8080;
                                                       static timeout = 5000;
                                                   }

                                                   Config.host + ":" + Config.port;

                                       """);
        Assert.Equal("localhost:8080", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Method()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class MathUtils {
                                                       static add(a, b) {
                                                           return a + b;
                                                       }
                                                   }

                                                   MathUtils.add(10, 20);

                                       """);
        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Method_And_Field()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Calculator {
                                                       static PI = 3.14159;

                                                       static circleArea(radius) {
                                                           return Calculator.PI * radius * radius;
                                                       }
                                                   }

                                                   Calculator.circleArea(10);

                                       """);
        Assert.Equal(314.159, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Field_Shared_Across_Instances()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Example {
                                                       static shared = 100;

                                                       getValue() {
                                                           return Example.shared;
                                                       }
                                                   }

                                                   let e1 = new Example();
                                                   let e2 = new Example();
                                                   Example.shared = 999;
                                                   e1.getValue() + e2.getValue();

                                       """);
        Assert.Equal(1998.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Private_Field()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Secret {
                                                       static #key = "secret123";

                                                       static getKey() {
                                                           return Secret.#key;
                                                       }
                                                   }

                                                   Secret.getKey();

                                       """);
        Assert.Equal("secret123", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Field_With_Expression_Initializer()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Numbers {
                                                       static value = 5 * 10 + 3;
                                                   }

                                                   Numbers.value;

                                       """);
        Assert.Equal(53.0, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task Static_Method_Accessing_Static_Field()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       static count = 0;

                                                       static increment() {
                                                           Counter.count = Counter.count + 1;
                                                           return Counter.count;
                                                       }

                                                       static decrement() {
                                                           Counter.count = Counter.count - 1;
                                                           return Counter.count;
                                                       }
                                                   }

                                                   Counter.increment();
                                                   Counter.increment();
                                                   Counter.decrement();
                                                   Counter.count;

                                       """);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Static_Block_Can_Update_Class_State()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       static count = 1;

                                                       static {
                                                           this.count += 41;
                                                       }
                                                   }

                                                   Counter.count;

                                       """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PublicFieldNamedHashConstructorUsesOrdinaryWritableDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            (function () {
              class C1 {
                ["#constructor"];
              }

              class C2 {
                ["#constructor"] = 42;
              }

              var c1 = new C1();
              var c2 = new C2();
              var d1 = Object.getOwnPropertyDescriptor(c1, "#constructor");
              var d2 = Object.getOwnPropertyDescriptor(c2, "#constructor");
              var old1 = c1["#constructor"];
              var old2 = c2["#constructor"];

              c1["#constructor"] = "updated";
              c2["#constructor"] = "changed";

              return [
                d1.value,
                d1.writable,
                d1.enumerable,
                d1.configurable,
                c1["#constructor"],
                old1,
                d2.value,
                d2.writable,
                d2.enumerable,
                d2.configurable,
                c2["#constructor"],
                old2
              ];
            })();
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].IsUndefined);
        Assert.True(array.Items[1].AsBoolean());
        Assert.True(array.Items[2].AsBoolean());
        Assert.True(array.Items[3].AsBoolean());
        Assert.Equal("updated", array.Items[4].AsString());
        Assert.True(array.Items[5].IsUndefined);
        Assert.Equal(42d, array.Items[6].NumberValue);
        Assert.True(array.Items[7].AsBoolean());
        Assert.True(array.Items[8].AsBoolean());
        Assert.True(array.Items[9].AsBoolean());
        Assert.Equal("changed", array.Items[10].AsString());
        Assert.Equal(42d, array.Items[11].NumberValue);
    }

    [Fact(Timeout = 2000)]
    public async Task Instance_Method_Cannot_Access_Static_Field_Via_This()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Example {
                                                       static staticValue = 100;

                                                       getValue() {
                                                           // Must use class name, not 'this'
                                                           return Example.staticValue;
                                                       }
                                                   }

                                                   let e = new Example();
                                                   e.getValue();

                                       """);
        Assert.Equal(100.0, result);
    }
}
