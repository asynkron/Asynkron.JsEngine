using Xunit;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class TestLetDeclaration
{
    [Fact]
    public async Task LetWithoutInitializer_ShouldWork()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            let x;
            x;
        ");
        Assert.Equal(JsSymbols.Undefined, result);
    }
}
