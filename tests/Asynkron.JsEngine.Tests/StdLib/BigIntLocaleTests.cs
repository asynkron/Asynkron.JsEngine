using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.StdLib;

public sealed class BigIntLocaleTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ToLocaleStringPrototypeIsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("BigInt.prototype.toLocaleString.prototype");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact]
    public async Task ToLocaleStringInheritsFromFunctionPrototype()
    {
        await using var engine = CreateEngine();
        var result =
            await engine.Evaluate("Object.getPrototypeOf(BigInt.prototype.toLocaleString) === Function.prototype");
        Assert.True(result is bool { } flag && flag);
    }
}
