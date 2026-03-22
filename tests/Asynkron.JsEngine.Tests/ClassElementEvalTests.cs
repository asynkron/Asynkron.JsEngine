using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class ClassElementEvalTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task DirectEvalReturnsFinalArrowExpressionCompletionValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                               var fn = eval('() => 7;');
                                               fn();

                                       """);

        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassFieldEvalProducedArrowFunctionWithoutSuperWorks()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                               class Derived {
                                                   field = eval('() => 9;');
                                               }

                                               var instance = new Derived();
                                               instance.field();

                                       """);

        Assert.Equal(9d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task InstanceFieldEvalCanAccessSuperProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       var executed = false;
                                                       class Base {
                                                           get value() {
                                                               return 123;
                                                           }
                                                       }

                                                       class Derived extends Base {
                                                           field = eval('executed = true; super.value;');
                                                       }

                                                       var instance = new Derived();
                                                       executed && instance.field === 123;

                                           """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task StaticFieldEvalCanAccessSuperProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       var executed = false;
                                                       class Base {
                                                           static get value() {
                                                               return 456;
                                                           }
                                                       }

                                                       class Derived extends Base {
                                                           static field = eval('executed = true; super.value;');
                                                       }

                                                       executed && Derived.field === 456;

                                           """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task EvalProducedArrowFunctionCanUseSuper()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       var executed = false;
                                                       class Base {
                                                           method() {
                                                               return 7;
                                                           }
                                                       }

                                                       class Derived extends Base {
                                                           field = eval('executed = true; () => super.method();');
                                                       }

                                                       var instance = new Derived();
                                                       var arrow = instance.field;
                                                       executed && arrow() === 7;

                                           """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task EvalArrowSuperMissingPropertyReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       class Base {}

                                                       class Derived extends Base {
                                                           field = eval('() => super.missing;');
                                                       }

                                                       var instance = new Derived();
                                                       typeof instance.field() === "undefined";

                                           """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ClassFieldEvalArrowWithSuperProducesFunctionValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                               class Base {}

                                               class Derived extends Base {
                                                   kind = typeof eval('() => super.missing;');
                                               }

                                               new Derived().kind;

                                       """);

        Assert.Equal("function", result);
    }

    [Fact(Timeout = 2000)]
    public async Task DirectEvalInPublicFieldInitializer_AllowsNewTargetAndReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                               var executed = false;
                                               class C {
                                                   field = eval('executed = true; new.target;');
                                               }

                                               var instance = new C();
                                               [executed, typeof instance.field];

                                       """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.Equal("undefined", array.Items[1].AsString());
    }

    [Fact(Timeout = 2000)]
    public async Task DirectEvalInPrivateFieldInitializer_AllowsNewTargetAndReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                               var executed = false;
                                               class C {
                                                   #field = eval('executed = true; new.target;');
                                                   read() { return this.#field; }
                                               }

                                               var instance = new C();
                                               [executed, typeof instance.read()];

                                       """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(array.Items[0].AsBoolean());
        Assert.Equal("undefined", array.Items[1].AsString());
    }
}
