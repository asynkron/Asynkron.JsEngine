namespace Asynkron.JsEngine.Tests;

public class ForInParserTests
{
    [Fact(Timeout = 2000)]
    public async Task ForInWithoutDeclarationParsesAndExecutes()
    {
        await using var engine = new JsEngine();

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

