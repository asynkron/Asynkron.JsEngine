using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class AdditionalBuiltinsTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
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
}

public class AdditionalBuiltinsTests(ITestOutputHelper output) : AdditionalBuiltinsTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}
