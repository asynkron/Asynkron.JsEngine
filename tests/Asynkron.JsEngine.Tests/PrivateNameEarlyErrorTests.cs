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

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralPrivateNameKey_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var o = {#x:1}; Object.keys(o)[0];");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralPrivateNameShorthand_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var o = {#x};");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralPrivateNameMethod_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var o = {#x(){}};");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectLiteralPrivateNameGetter_ThrowsParseException()
    {
        await using var engine = CreateEngine();

        await Assert.ThrowsAsync<ParseException>(async () =>
        {
            await engine.Evaluate("var o = {get #x(){ return 1; }};");
        });
    }

    [Fact(Timeout = 2000)]
    public async Task OrdinaryObjectLiteral_RemainsValid()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate(
            "var k = 'c'; var o = {a:1, [k]:2, m(){ return 3; }, get g(){ return 4; }, 'str':5, 6:7}; " +
            "o.a + o.c + o.m() + o.g + o.str + o[6];");

        Assert.Equal(22.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task PrivateNameBrandCheckInExpression_RemainsValid()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
                                           class C {
                                               #x = 1;
                                               static has(obj) { return #x in obj; }
                                           }
                                           C.has(new C()) && !C.has({});
                                           """);

        Assert.True((bool)result!);
    }
}
