using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibPromise)]
[Category(TestCategories.StdLibSymbol)]
[Category(TestCategories.StdLibObject)]
public sealed class AdditionalBuiltinsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Promise_WithResolvers_ReturnsObjectWithPromiseAndFunctions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const obj = Promise.withResolvers();
            obj !== undefined && obj !== null && obj.promise !== undefined && obj.resolve !== undefined && obj.reject !== undefined;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_WithResolvers_PromiseIsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const { promise } = Promise.withResolvers();
            typeof promise;
        ");
        Assert.Equal("object", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Promise_WithResolvers_ResolveFunctionIsCallable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const { resolve } = Promise.withResolvers();
            typeof resolve;
        ");
        Assert.Equal("function", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Symbol_Dispose_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            Symbol.dispose !== undefined;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Symbol_AsyncDispose_Exists()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            Symbol.asyncDispose !== undefined;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeError_InstanceOf_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var e = new TypeError('test');
            e instanceof TypeError;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task TypeError_CalledAsFunction_ReturnsInstance()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var e = TypeError('called as function');
            e instanceof TypeError;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Number_ValueOf_ThrowsTypeError_InstanceOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            try {
                var s = new String();
                s.valueOf = Number.prototype.valueOf;
                s.valueOf();
                false;
            } catch(e) {
                e instanceof TypeError;
            }
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task RangeError_InstanceOf_Works()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var e = new RangeError('test');
            e instanceof RangeError;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ToFixed_Infinity_ThrowsRangeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            try {
                (1).toFixed(Infinity);
                false;
            } catch(e) {
                e instanceof RangeError;
            }
        ");
        Assert.True((bool)result!);
    }
}
