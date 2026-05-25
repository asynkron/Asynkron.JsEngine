using Asynkron.JsEngine.Parser;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Parser)]
public sealed class PrivateNameEarlyErrorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task TopLevelBlockMemberPrivateName_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("{ this.#x }");
        });

        Assert.Contains("must be declared in an enclosing class", ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task DeclaredClassPrivateName_RemainsValid()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
                                           class C {
                                               #x = 42;
                                               getX() { return this.#x; }
                                           }
                                           new C().getX();
                                           """);

        Assert.Equal(42.0, result);
    }
}
