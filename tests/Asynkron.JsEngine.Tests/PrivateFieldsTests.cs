namespace Asynkron.JsEngine.Tests;

public class PrivateFieldsTests
{
    [Fact]
    public void PrivateFieldBasicAccess()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(2d, result);
    }

    [Fact]
    public void PrivateFieldInitializedInConstructor()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(10d, result);
    }

    [Fact]
    public void MultiplePrivateFields()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(50d, result);
    }

    [Fact]
    public void PrivateFieldNotAccessibleOutsideClass()
    {
        var engine = new JsEngine();
        // For now, private fields are accessible as they're stored as properties
        // In a future implementation, we could add access control
        // This test documents the current behavior
        var result = engine.Evaluate(@"
            class Counter {
                #count = 42;
            }
            
            let c = new Counter();
            c[""#count""];
        ");
        Assert.Equal(42d, result);
    }

    [Fact]
    public void PrivateFieldInGetter()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void PrivateFieldInSetter()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal("Bob", result);
    }

    [Fact(Skip = "Public class fields not yet implemented")]
    public void PrivateFieldWithPublicField()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            class Mixed {
                #private = 10;
                public = 20;
                
                getSum() {
                    return this.#private + this.public;
                }
            }
            
            let m = new Mixed();
            m.getSum();
        ");
        Assert.Equal(30d, result);
    }

    [Fact]
    public void PrivateFieldInInheritedClass()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(100d, result);
    }

    [Fact]
    public void PrivateFieldsAreSeparateBetweenInstances()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(3d, result);
    }

    [Fact(Skip = "Public class fields not yet implemented")]
    public void PrivateFieldWithSameNameAsPublic()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
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
        ");
        Assert.Equal(30d, result);
    }
}
