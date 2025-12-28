using Asynkron.JsEngine.Ast;
using Microsoft.Extensions.Logging.Testing;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class PrivateFieldsTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task PrivateFieldBasicAccess()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       #count = 0;

                                                       increment() {
                                                           this.#count = this.#count + 1;
                                                       }

                                                       getValue() {
                                                           return this.#count;
                                                       }
                                                   }

                                                   let c = new Counter();
                                                   c.increment();
                                                   c.increment();
                                                   c.getValue();

                                       """);
        Assert.Equal(2d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldInitializedInConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       #count;

                                                       constructor(initial) {
                                                           this.#count = initial;
                                                       }

                                                       getValue() {
                                                           return this.#count;
                                                       }
                                                   }

                                                   let c = new Counter(10);
                                                   c.getValue();

                                       """);
        Assert.Equal(10d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task MultiplePrivateFields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Rectangle {
                                                       #width = 0;
                                                       #height = 0;

                                                       constructor(w, h) {
                                                           this.#width = w;
                                                           this.#height = h;
                                                       }

                                                       getArea() {
                                                           return this.#width * this.#height;
                                                       }
                                                   }

                                                   let rect = new Rectangle(5, 10);
                                                   rect.getArea();

                                       """);
        Assert.Equal(50d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldNotAccessibleOutsideClass()
    {
        await using var engine = CreateEngine();
        // c["#count"] accesses a regular property named "#count" (the string), not the private field
        // This returns undefined, not an error - private fields are only accessible with dot notation
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       #count = 42;
                                                   }

                                                   let c = new Counter();
                                                   c["#count"];

                                       """);
        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldInGetter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Person {
                                                       #name;

                                                       constructor(name) {
                                                           this.#name = name;
                                                       }

                                                       get name() {
                                                           return this.#name;
                                                       }
                                                   }

                                                   let p = new Person('Alice');
                                                   p.name;

                                       """);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldInSetter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Person {
                                                       #name;

                                                       get name() {
                                                           return this.#name;
                                                       }

                                                       set name(value) {
                                                           this.#name = value;
                                                       }
                                                   }

                                                   let p = new Person();
                                                   p.name = 'Bob';
                                                   p.name;

                                       """);
        Assert.Equal("Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldWithPublicField()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Mixed {
                                                       #private = 10;
                                                       public = 20;

                                                       getSum() {
                                                           return this.#private + this.public;
                                                       }
                                                   }

                                                   let m = new Mixed();
                                                   m.getSum();

                                       """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldInInheritedClass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Base {
                                                       #secret = 100;

                                                       getSecret() {
                                                           return this.#secret;
                                                       }
                                                   }

                                                   class Derived extends Base {
                                                       getValue() {
                                                           return this.getSecret();
                                                       }
                                                   }

                                                   let d = new Derived();
                                                   d.getValue();

                                       """);
        Assert.Equal(100d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldsAreSeparateBetweenInstances()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Counter {
                                                       #count = 0;

                                                       increment() {
                                                           this.#count = this.#count + 1;
                                                       }

                                                       getValue() {
                                                           return this.#count;
                                                       }
                                                   }

                                                   let c1 = new Counter();
                                                   let c2 = new Counter();
                                                   c1.increment();
                                                   c1.increment();
                                                   c2.increment();

                                                   c1.getValue() + c2.getValue();

                                       """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateFieldWithSameNameAsPublic()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                   class Test {
                                                       #value = 10;
                                                       value = 20;

                                                       getPrivate() {
                                                           return this.#value;
                                                       }

                                                       getPublic() {
                                                           return this.value;
                                                       }
                                                   }

                                                   let t = new Test();
                                                   t.getPrivate() + t.getPublic();

                                       """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateField_AccessibleFromArrowFunction()
    {
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

    [Fact(Timeout = 2000)]
    public async Task PrivateField_ArrowFunction_DebugTest()
    {
        var fakeLogger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = fakeLogger });

        // First, verify this binding works with PUBLIC field
        var thisResult = await engine.Evaluate("""
            class Counter1 {
                count = 0;
                increment() {
                    const inc = () => {
                        return this;
                    };
                    return inc();
                }
            }
            const c1 = new Counter1();
            typeof c1.increment();
            """);
        output.WriteLine($"Case 1 - typeof this in arrow (public field class): {thisResult}");
        Assert.Equal("object", thisResult);

        // Now verify private field access directly in method (no arrow function)
        var directResult = await engine.Evaluate("""
            class Counter2 {
                #count = 0;
                increment() {
                    this.#count++;
                    return this.#count;
                }
            }
            const c2 = new Counter2();
            c2.increment();
            """);
        output.WriteLine($"Case 2 - Direct private access: {directResult}");
        Assert.Equal(1d, directResult);

        // Case 2b: Check if 'this' is captured correctly in arrow when class has private fields
        // First, check that 'this' is accessible directly in the method
        var thisInMethodResult = await engine.Evaluate("""
            class Counter2a {
                #count = 42;
                getThis() {
                    return this;
                }
            }
            const c2a = new Counter2a();
            typeof c2a.getThis();
            """);
        output.WriteLine($"Case 2a - typeof this in method (PRIVATE field class): {thisInMethodResult}");
        Assert.Equal("object", thisInMethodResult);

        // Now check if arrow can capture 'this'
        var thisInPrivateClassResult = await engine.Evaluate("""
            class Counter2b {
                #count = 42;
                getCount() {
                    const getIt = () => {
                        return this;
                    };
                    return getIt();
                }
            }
            const c2b = new Counter2b();
            typeof c2b.getCount();
            """);
        output.WriteLine($"Case 2b - typeof this in arrow (PRIVATE field class): {thisInPrivateClassResult}");

        // Print debug logs for arrow this capture
        foreach (var record in fakeLogger.Collector.Snapshot())
        {
            if (record.Message.Contains("Arrow this capture"))
            {
                output.WriteLine($"DEBUG: {record.Message}");
            }
        }

        Assert.Equal("object", thisInPrivateClassResult);

        // Case 3: Just reading private field from arrow function (no increment)
        var arrowReadResult = await engine.Evaluate("""
            class Counter3 {
                #count = 42;
                getCount() {
                    const getIt = () => {
                        return this.#count;
                    };
                    return getIt();
                }
            }
            const c3 = new Counter3();
            c3.getCount();
            """);
        output.WriteLine($"Case 3 - Arrow private read: {arrowReadResult}");
        Assert.Equal(42d, arrowReadResult);

        // Case 4: Increment private field from arrow function
        var arrowResult = await engine.Evaluate("""
            class Counter4 {
                #count = 0;
                increment() {
                    const inc = () => {
                        this.#count++;
                        return this.#count;
                    };
                    return inc();
                }
            }
            const c4 = new Counter4();
            c4.increment();
            """);
        output.WriteLine($"Case 4 - Arrow private access: {arrowResult}");
        Assert.Equal(1d, arrowResult);
    }
}
