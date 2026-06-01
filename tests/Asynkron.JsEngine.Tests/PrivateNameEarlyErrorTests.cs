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

    [Fact(Timeout = 2000)]
    public async Task DirectEvalInsideClassMethod_InheritsPrivateNameScope()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
                                           class C {
                                               #x = 42;
                                               getViaEval() {
                                                   return eval("this.#x");
                                               }
                                           }
                                           new C().getViaEval();
                                           """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateNamedPropertyDelete_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("""
                                  class C {
                                      #x = 42;
                                      remove() {
                                          return delete this.#x;
                                      }
                                  }
                                  """);
        });

        Assert.Contains("Private field '#x' cannot be deleted", ex.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 2000)]
    public async Task DeletePublicPropertyReadThroughPrivateField_RemainsValid()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
                                           class C {
                                               #box = { value: 42 };
                                               remove() {
                                                   const deleted = delete this.#box.value;
                                                   return deleted && this.#box.value === undefined;
                                               }
                                           }
                                           new C().remove();
                                           """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task DirectEvalInsideClassMethod_PrivateNamedPropertyDelete_ThrowsSyntaxError()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
                                           class C {
                                               #x = 42;
                                               removeViaEval() {
                                                   try {
                                                       eval("delete this.#x");
                                                       return false;
                                                   } catch (e) {
                                                       return e instanceof SyntaxError &&
                                                           e.message.indexOf("Private field '#x' cannot be deleted") >= 0;
                                                   }
                                               }
                                           }
                                           new C().removeViaEval();
                                           """);

        Assert.True((bool)result!);
    }
}
