using Asynkron.JsEngine.JsTypes;
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

    [Fact(Timeout = 2000)]
    public async Task Property_Add_Chain_Preserves_Getter_Order()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var reads = [];
            var obj = {
                get x() { reads.push("x"); return 1; },
                get y() { reads.push("y"); return 2; },
                get z() { reads.push("z"); return 3; }
            };
            var sum = obj.x + obj.y + obj.z;
            sum + ":" + reads.join(",");
        """);

        Assert.Equal("6:x,y,z", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Property_Add_Chain_Preserves_String_Addition_Semantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var obj = {
                x: "a",
                y: 1,
                z: 2
            };
            obj.x + obj.y + obj.z;
        """);

        Assert.Equal("a12", result);
    }

    [Fact]
    public void TrySetExistingJsValue_Updates_Writable_Data_Descriptor_In_Place()
    {
        var obj = new JsObject();
        var descriptor = new PropertyDescriptor
        {
            JsValue = JsValue.FromDouble(1),
            Writable = true,
            Enumerable = false,
            Configurable = false
        };

        obj.DefineProperty("value", descriptor);
        var stored = obj.GetOwnPropertyDescriptor("value");

        Assert.True(obj.TrySetExistingJsValue("value", JsValue.FromDouble(9)));

        var updated = obj.GetOwnPropertyDescriptor("value");
        Assert.Same(stored, updated);
        Assert.Equal(9, updated!.JsValue.AsDouble());
        Assert.True(updated.Writable);
        Assert.False(updated.Enumerable);
        Assert.False(updated.Configurable);
    }

    [Fact]
    public void TrySetExistingJsValue_Rejects_Descriptor_Sensitive_Writes()
    {
        var obj = new JsObject();
        obj.DefineProperty("locked", new PropertyDescriptor
        {
            JsValue = JsValue.FromDouble(1),
            Writable = false,
            Enumerable = true,
            Configurable = true
        });

        Assert.False(obj.TrySetExistingJsValue("missing", JsValue.FromDouble(2)));
        Assert.False(obj.TrySetExistingJsValue("locked", JsValue.FromDouble(2)));

        var locked = obj.GetOwnPropertyDescriptor("locked");
        Assert.Equal(1, locked!.JsValue.AsDouble());
        Assert.False(locked.Writable);
    }
}
