using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Parser)]
public sealed class IdentifierUnicodeLexerTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task RawSupplementaryPlaneIdentifierStart_IsAccepted()
    {
        await using var engine = CreateEngine();
        var supplementaryIdentifier = char.ConvertFromUtf32(0x10400);

        var result = await engine.Evaluate($"""
            var {supplementaryIdentifier} = 41;
            {supplementaryIdentifier} + 1;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task EscapedSupplementaryPlaneIdentifierStart_IsAccepted()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var \u{10400} = 39;
            \u{10400} + 3;
            """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateIdentifierWithSupplementaryPlaneStart_IsAccepted()
    {
        await using var engine = CreateEngine();
        var supplementaryIdentifier = char.ConvertFromUtf32(0x10400);

        var script = "class Foo {\n" +
                     $"  #{supplementaryIdentifier} = 42;\n" +
                     $"  read() {{ return this.#{supplementaryIdentifier}; }}\n" +
                     "}\n" +
                     "new Foo().read();";
        var result = await engine.Evaluate(script);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task InvalidEscapedIdentifierStart_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var \\u{30} = 1;");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task InvalidEscapedIdentifierContinuation_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var a\\u{20} = 1;");
        });
    }
}
