using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class ClassComputedAccessorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task ComputedAccessorAllowsInExpressions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       var empty = Object.create(null);
                                                       var value;
                                                       var C;

                                                       for (C = class { get ['x' in empty]() { return 'via get'; } }; ; ) {
                                                           value = C.prototype.false;
                                                           break;
                                                       }

                                                       if (value !== 'via get') {
                                                           throw 'getter failed';
                                                       }

                                                       for (C = class { set ['x' in empty](param) { value = param; } }; ; ) {
                                                           C.prototype.false = 'via set';
                                                           break;
                                                       }

                                                       if (value !== 'via set') {
                                                           throw 'setter failed';
                                                       }

                                                       value;

                                           """);

        Assert.Equal("via set", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ComputedAccessorAllowsYieldExpressions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var yieldSet, C, iter;
            function* g() {
                C = class {
                    get [yield]() { return 'get yield'; }
                    set [yield](param) { yieldSet = param; }
                };
            }

            iter = g();
            iter.next();
            iter.next('first');
            iter.next('second');
            var getterValue = C.prototype.first;
            C.prototype.second = 'set yield';
            [getterValue, yieldSet];
            """);

        var array = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal("get yield", array.GetElement(0).AsString());
        Assert.Equal("set yield", array.GetElement(1).AsString());
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_ClassDeclarationComputedMethodNameCanAwait()
    {
        await using var engine = CreateEngine();
        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];

            async function run() {
                class Box {
                    [await __delay(1, "value")]() {
                        return "method";
                    }
                }

                log.push(new Box().value());
            }

            run();
            """);

        var result = await engine.Evaluate("log.join(',');");
        Assert.Equal("method", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_ClassDeclarationComputedAccessorNamesCanAwait()
    {
        await using var engine = CreateEngine();
        AsyncTestHelpers.RegisterDelayHelper(engine);

        await engine.Evaluate("""
            let log = [];
            let stored = "";

            async function run() {
                class Box {
                    get [await __delay(1, "value")]() {
                        return "get";
                    }

                    set [await __delay(1, "value")](input) {
                        stored = input;
                    }
                }

                let box = new Box();
                log.push(box.value);
                box.value = "set";
                log.push(stored);
            }

            run();
            """);

        var result = await engine.Evaluate("log.join(',');");
        Assert.Equal("get,set", result);
    }

}
