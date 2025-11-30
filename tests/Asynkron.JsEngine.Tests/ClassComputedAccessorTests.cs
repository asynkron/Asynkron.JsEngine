namespace Asynkron.JsEngine.Tests;

public class ClassComputedAccessorTests
{
    [Fact(Timeout = 2000)]
    public async Task ComputedAccessorAllowsInExpressions()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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

        var array = Assert.IsType<Asynkron.JsEngine.JsTypes.JsArray>(result);
        Assert.Equal("get yield", array.Items[0]?.ToString());
        Assert.Equal("set yield", array.Items[1]?.ToString());
    }

}
