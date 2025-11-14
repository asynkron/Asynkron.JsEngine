using Xunit;

namespace Asynkron.JsEngine.Tests;

public class GetPropertyNameTests
{
    [Fact(Timeout = 2000)]
    public async Task Get_As_Property_Name_In_Object_Literal()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                get: function () {
                    return 42;
                }
            };
            obj.get();
        """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Get_And_Set_As_Property_Names()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                get: 10,
                set: 20
            };
            obj.get + obj.set;
        """);
        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_DefineProperty_With_Get_As_Property_Name()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var n = { x: 1, y: 2 };
            var a = {};
            
            Object.keys(n).forEach(function (k) {
                var d = Object.getOwnPropertyDescriptor(n, k);
                Object.defineProperty(a, k, d.get ? d : {
                    enumerable: true,
                    get: function () {
                        return n[k];
                    }
                });
            });
            
            a.x;
        """);
        Assert.Equal(1.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Real_Getter_Still_Works()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                _value: 100,
                get value() {
                    return this._value;
                }
            };
            obj.value;
        """);
        Assert.Equal(100.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Real_Setter_Still_Works()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                _value: 0,
                set value(v) {
                    this._value = v * 2;
                },
                get value() {
                    return this._value;
                }
            };
            obj.value = 50;
            obj.value;
        """);
        Assert.Equal(100.0, result);
    }
}
