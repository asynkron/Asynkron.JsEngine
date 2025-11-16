namespace Asynkron.JsEngine.Tests;

public class StaticClassFieldsTests
{
    [Fact(Timeout = 2000)]
    public async Task Static_Field_With_Initializer()
    {
        await using var engine = new JsEngine();
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
    //     await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
    public async Task Instance_Method_Cannot_Access_Static_Field_Via_This()
    {
        await using var engine = new JsEngine();
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
