using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Xunit;

namespace Asynkron.JsEngine.Tests.StdLib;

public class BigIntLocaleTests
{
    [Fact]
    public async Task ToLocaleStringPrototypeIsUndefined()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("BigInt.prototype.toLocaleString.prototype");
        Assert.Same(Symbol.Undefined, result);
    }

    [Fact]
    public async Task ToLocaleStringInheritsFromFunctionPrototype()
    {
        await using var engine = new JsEngine();
        var result =
            await engine.Evaluate("Object.getPrototypeOf(BigInt.prototype.toLocaleString) === Function.prototype");
        Assert.True(result is bool { } flag && flag);
    }
}
