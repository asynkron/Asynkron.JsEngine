using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class DerivedConstructorReturnOverrideTestBomb(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task H1_ReturnStringAfterSuper_ThrowsTypeError()
    {
        await AssertDerivedConstructorResultAsync("\"\"");
    }

    [Fact(Timeout = 2000)]
    public async Task H2_ReturnSymbolAfterSuper_ThrowsTypeError()
    {
        await AssertDerivedConstructorResultAsync("Symbol()");
    }

    [Fact(Timeout = 2000)]
    public async Task H3_ReturnBooleanAfterSuper_ThrowsTypeError()
    {
        await AssertDerivedConstructorResultAsync("true");
    }

    [Fact(Timeout = 2000)]
    public async Task H4_ReturnNullAfterSuper_ThrowsTypeError()
    {
        await AssertDerivedConstructorResultAsync("null");
    }

    [Fact(Timeout = 2000)]
    public async Task H5_ReturnUndefinedWithoutSuper_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
              constructor() {}
            }

            class Derived extends Base {
              constructor() {
                return;
              }
            }

            try {
              new Derived();
              return "no-throw";
            } catch (e) {
              return e.name;
            }
            """);

        Assert.Equal("ReferenceError", result);
    }

    private async Task AssertDerivedConstructorResultAsync(string expression)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            class Base {
              constructor() {}
            }

            class Derived extends Base {
              constructor() {
                super();
                return {{expression}};
              }
            }

            try {
              new Derived();
              return "no-throw";
            } catch (e) {
              return e.name;
            }
            """);

        Assert.Equal("TypeError", result);
    }
}
