using Xunit;

namespace Asynkron.JsEngine.Tests;

public class GetPropertyNameTests
{
    [Fact(Timeout = 2000)]
    public async Task Get_As_Property_Name_In_Object_Literal()
    {
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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
        await using var engine = new JsEngine();
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

    [Fact(Timeout = 2000)]
    public async Task Get_As_Named_Function_Expression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                get: function get() {
                    return 42;
                }
            };
            obj.get();
        """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_As_Named_Function_Expression()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                set: function set(val) {
                    return val * 2;
                }
            };
            obj.set(21);
        """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Get_As_Function_Name_With_Different_Property_Name()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                foo: function get() {
                    return 42;
                }
            };
            obj.foo();
        """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_As_Function_Name_With_Different_Property_Name()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = {
                bar: function set() {
                    return 99;
                }
            };
            obj.bar();
        """);
        Assert.Equal(99.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Async_Function_With_Get_As_Name()
    {
        await using var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("capture", args =>
        {
            result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""
            var obj = {
                get: async function get() {
                    return 42;
                }
            };
            obj.get().then(function(val) { capture(val); });
        """);
        Assert.Equal("42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task Async_Function_With_Set_As_Name()
    {
        await using var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("capture", args =>
        {
            result = args[0]?.ToString() ?? "";
            return null;
        });

        await engine.Run("""
            var obj = {
                set: async function set(val) {
                    return val * 2;
                }
            };
            obj.set(21).then(function(val) { capture(val); });
        """);
        Assert.Equal("42", result);
    }
}
