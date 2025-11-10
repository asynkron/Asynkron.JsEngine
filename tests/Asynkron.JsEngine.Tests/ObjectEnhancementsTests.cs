using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class ObjectEnhancementsTests
{
    // Object property shorthand tests
    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyShorthand()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let name = 'Alice';
                                                       let age = 30;
                                                       let person = { name, age };
                                                       person.name;
                                                   
                                           """);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyShorthandAge()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let name = 'Alice';
                                                       let age = 30;
                                                       let person = { name, age };
                                                       person.age;
                                                   
                                           """);
        Assert.Equal(30d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectPropertyShorthandMixed()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let x = 10;
                                                       let obj = { x, y: 20, z: 30 };
                                                       obj.x + obj.y + obj.z;
                                                   
                                           """);
        Assert.Equal(60d, result);
    }

    // Object method shorthand tests
    [Fact(Timeout = 2000)]
    public async Task ObjectMethodShorthand()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let person = {
                                                           name: 'Alice',
                                                           greet() {
                                                               return 'Hello, ' + this.name;
                                                           }
                                                       };
                                                       person.greet();
                                                   
                                           """);
        Assert.Equal("Hello, Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectMethodShorthandMultiple()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let calculator = {
                                                           add(a, b) {
                                                               return a + b;
                                                           },
                                                           multiply(a, b) {
                                                               return a * b;
                                                           }
                                                       };
                                                       calculator.add(5, 3) + calculator.multiply(4, 2);
                                                   
                                           """);
        Assert.Equal(16d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectMethodShorthandWithThis()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let counter = {
                                                           count: 0,
                                                           increment() {
                                                               this.count = this.count + 1;
                                                               return this.count;
                                                           },
                                                           getValue() {
                                                               return this.count;
                                                           }
                                                       };
                                                       counter.increment();
                                                       counter.increment();
                                                       counter.getValue();
                                                   
                                           """);
        Assert.Equal(2d, result);
    }

    // Computed property names tests
    [Fact(Timeout = 2000)]
    public async Task ComputedPropertyName()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let propName = 'dynamicKey';
                                                       let obj = {
                                                           [propName]: 'value'
                                                       };
                                                       obj.dynamicKey;
                                                   
                                           """);
        Assert.Equal("value", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedPropertyNameExpression()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {
                                                           ['computed' + 'Key']: 123
                                                       };
                                                       obj.computedKey;
                                                   
                                           """);
        Assert.Equal(123d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedPropertyNameMixed()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let key1 = 'a';
                                                       let key2 = 'b';
                                                       let obj = {
                                                           [key1]: 1,
                                                           normalKey: 2,
                                                           [key2]: 3
                                                       };
                                                       obj.a + obj.normalKey + obj.b;
                                                   
                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedPropertyNameWithMethod()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let methodName = 'greet';
                                                       let person = {
                                                           name: 'Bob',
                                                           [methodName]() {
                                                               return 'Hi, ' + this.name;
                                                           }
                                                       };
                                                       person.greet();
                                                   
                                           """);
        Assert.Equal("Hi, Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedPropertyNameNumber()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let index = 0;
                                                       let obj = {
                                                           [index]: 'zero',
                                                           [index + 1]: 'one'
                                                       };
                                                       obj[0] + ' ' + obj[1];
                                                   
                                           """);
        Assert.Equal("zero one", result);
    }

    // Combined features
    [Fact(Timeout = 2000)]
    public async Task CombinedShorthandAndComputed()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let name = 'Alice';
                                                       let key = 'age';
                                                       let person = {
                                                           name,
                                                           [key]: 30,
                                                           greet() {
                                                               return this.name;
                                                           }
                                                       };
                                                       person.greet() + ' is ' + person.age;
                                                   
                                           """);
        Assert.Equal("Alice is 30", result);
    }

    // Object spread in object literals
    [Fact(Timeout = 2000)]
    public async Task ObjectSpreadInLiteral()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj1 = { a: 1, b: 2 };
                                                       let obj2 = { c: 3, d: 4 };
                                                       let merged = { ...obj1, ...obj2, e: 5 };
                                                       merged.a + merged.b + merged.c + merged.d + merged.e;
                                                   
                                           """);
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectSpreadOverwrites()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj1 = { a: 1, b: 2 };
                                                       let obj2 = { b: 20, c: 3 };
                                                       let merged = { ...obj1, ...obj2 };
                                                       merged.a + merged.b + merged.c;
                                                   
                                           """);
        Assert.Equal(24d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectSpreadWithRegularProperties()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let base = { x: 1, y: 2 };
                                                       let extended = { ...base, z: 3, w: 4 };
                                                       extended.x + extended.y + extended.z + extended.w;
                                                   
                                           """);
        Assert.Equal(10d, result);
    }
}
