using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class InstanceofTests
{
    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithClass_ReturnsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            class MyClass {}
            let obj = new MyClass();
            obj instanceof MyClass;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithDifferentClass_ReturnsFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            class MyClass {}
            class OtherClass {}
            let obj = new MyClass();
            obj instanceof OtherClass;
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithFunction_ReturnsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function MyConstructor() {}
            let obj = new MyConstructor();
            obj instanceof MyConstructor;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithInheritance_ReturnsTrue()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            class Base {}
            class Derived extends Base {}
            let obj = new Derived();
            obj instanceof Base;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithNonObject_ReturnsFalse()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            class MyClass {}
            42 instanceof MyClass;
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_ErrorInIfCondition_Works()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            class TypeError {
                constructor(msg) {
                    this.message = msg;
                }
            }
            let error = new TypeError('test');
            if (error instanceof TypeError) {
                'correct';
            } else {
                'wrong';
            }
        ");
        Assert.Equal("correct", result);
    }
}
