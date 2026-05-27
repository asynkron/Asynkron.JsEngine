using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class PropertyAccessFastPathTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Own_Data_Property_Read_Still_Shadows_Prototype_Getter()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var proto = {
                get value() {
                    return 1;
                }
            };
            var obj = Object.create(proto);
            Object.defineProperty(obj, "value", { value: 42 });
            obj.value;
        """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Prototype_Getter_Read_Still_Uses_Receiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var proto = {
                get value() {
                    return this.base + 1;
                }
            };
            var obj = Object.create(proto);
            obj.base = 41;
            obj.value;
        """);

        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Receiver_Data_Property_Read_Still_Stops_At_Max_Prototype_Depth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            var reachable = { value: 42 };
            for (var i = 0; i < {{JsEngineConstants.MaxPrototypeChainDepth - 1}}; i++) {
                reachable = Object.create(reachable);
            }

            var tooDeep = { value: 13 };
            for (var j = 0; j < {{JsEngineConstants.MaxPrototypeChainDepth}}; j++) {
                tooDeep = Object.create(tooDeep);
            }

            reachable.value + ":" + (tooDeep.value === undefined);
        """);

        Assert.Equal("42:true", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Compound_Add_With_Property_Rhs_Evaluates_Getter_Once()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var reads = 0;
            var obj = {
                get value() {
                    reads++;
                    return 2;
                }
            };
            var sum = 1;
            sum += obj.value;
            (sum * 10) + reads;
        """);

        Assert.Equal(31.0, result);
    }
}
