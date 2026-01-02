using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class ForInParserTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ForInWithoutDeclarationParsesAndExecutes()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            let source = { a: 1, b: 2 };
            let keys = [];
            for (key in source) {
                keys.push(key);
            }
        """);

        var length = await engine.Evaluate("keys.length;");
        Assert.Equal(2.0, length);
    }
}
