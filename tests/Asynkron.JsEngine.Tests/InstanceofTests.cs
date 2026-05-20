using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.TypeSystem)]
public sealed class InstanceofTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Instanceof_WithClass_ReturnsTrue()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class MyClass {}
            42 instanceof MyClass;
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_ErrorInIfCondition_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            class CustomTypeError {
                constructor(msg) {
                    this.message = msg;
                }
            }
            let error = new CustomTypeError('test');
            if (error instanceof CustomTypeError) {
                'correct';
            } else {
                'wrong';
            }
        ");
        Assert.Equal("correct", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_CustomSymbolHasInstance_UsesConstructorAsThisAndPassesCandidate()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const rhs = {};
            const lhs = {};
            let seenThis = null;
            let seenArg = null;
            rhs[Symbol.hasInstance] = function(candidate) {
                seenThis = this;
                seenArg = candidate;
                return true;
            };
            lhs instanceof rhs;
            seenThis === rhs && seenArg === lhs;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Instanceof_CustomSymbolHasInstance_ResultUsesBooleanTruthiness()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const rhs = {};
            rhs[Symbol.hasInstance] = () => ({});
            const truthy = ({} instanceof rhs);
            rhs[Symbol.hasInstance] = () => 0;
            const falsy = ({} instanceof rhs);
            truthy === true && falsy === false;
        ");
        Assert.True((bool)result!);
    }
}
