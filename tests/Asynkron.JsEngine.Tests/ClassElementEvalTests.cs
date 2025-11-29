namespace Asynkron.JsEngine.Tests;

public class ClassElementEvalTests
{
    [Fact(Timeout = 2000)]
    public async Task InstanceFieldEvalCanAccessSuperProperty()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
}
