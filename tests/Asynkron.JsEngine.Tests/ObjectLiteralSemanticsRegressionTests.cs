using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class ObjectLiteralSemanticsRegressionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ComputedPropertyName_IsConvertedBeforeValueExpressionRuns()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var value = "bad";

            var key = {
              toString() {
                value = "ok";
                return "p";
              }
            };

            var obj = {
              [key]: value
            };

            obj.p;
            """);

        Assert.Equal("ok", Assert.IsType<string>(result));
    }

    [Fact]
    public async Task ObjectLiteral_AssignsNameToAnonymousArrowFunctions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var namedSym = Symbol('test262');
            var anonSym = Symbol();
            var o = {
              id: () => {},
              [anonSym]: () => {},
              [namedSym]: () => {}
            };

            [
              o.id.name,
              Object.getOwnPropertyDescriptor(o.id, "name").writable,
              Object.getOwnPropertyDescriptor(o.id, "name").enumerable,
              Object.getOwnPropertyDescriptor(o.id, "name").configurable,
              o[anonSym].name,
              o[namedSym].name
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("id", array.GetElement(0).AsString());
        Assert.False(array.GetElement(1).AsBoolean());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.True(array.GetElement(3).AsBoolean());
        Assert.Equal(string.Empty, array.GetElement(4).AsString());
        Assert.Equal("[test262]", array.GetElement(5).AsString());
    }

    [Fact]
    public async Task ObjectLiteral_AssignsNameToAnonymousClassExpressions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var namedSym = Symbol('test262');
            var anonSym = Symbol();
            var o = {
              xId: class x {},
              id: class {},
              [anonSym]: class {},
              [namedSym]: class {}
            };

            [
              o.xId.name,
              o.id.name,
              Object.getOwnPropertyDescriptor(o.id, "name").writable,
              Object.getOwnPropertyDescriptor(o.id, "name").enumerable,
              Object.getOwnPropertyDescriptor(o.id, "name").configurable,
              o[anonSym].name,
              o[namedSym].name
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("x", array.GetElement(0).AsString());
        Assert.Equal("id", array.GetElement(1).AsString());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.False(array.GetElement(3).AsBoolean());
        Assert.True(array.GetElement(4).AsBoolean());
        Assert.Equal(string.Empty, array.GetElement(5).AsString());
        Assert.Equal("[test262]", array.GetElement(6).AsString());
    }

    [Fact]
    public async Task ObjectLiteral_AssignsNameToAnonymousFunctionExpressions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var namedSym = Symbol('test262');
            var anonSym = Symbol();
            var o = {
              xId: function x() {},
              id: function() {},
              [anonSym]: function() {},
              [namedSym]: function() {}
            };

            [
              o.xId.name,
              o.id.name,
              Object.getOwnPropertyDescriptor(o.id, "name").writable,
              Object.getOwnPropertyDescriptor(o.id, "name").enumerable,
              Object.getOwnPropertyDescriptor(o.id, "name").configurable,
              o[anonSym].name,
              o[namedSym].name
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("x", array.GetElement(0).AsString());
        Assert.Equal("id", array.GetElement(1).AsString());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.False(array.GetElement(3).AsBoolean());
        Assert.True(array.GetElement(4).AsBoolean());
        Assert.Equal(string.Empty, array.GetElement(5).AsString());
        Assert.Equal("[test262]", array.GetElement(6).AsString());
    }

    [Fact]
    public async Task ObjectLiteral_AssignsNameToAnonymousGeneratorExpressions()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var namedSym = Symbol('test262');
            var anonSym = Symbol();
            var o = {
              xId: function* x() {},
              id: function*() {},
              [anonSym]: function*() {},
              [namedSym]: function*() {}
            };

            [
              o.xId.name,
              o.id.name,
              Object.getOwnPropertyDescriptor(o.id, "name").writable,
              Object.getOwnPropertyDescriptor(o.id, "name").enumerable,
              Object.getOwnPropertyDescriptor(o.id, "name").configurable,
              o[anonSym].name,
              o[namedSym].name
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("x", array.GetElement(0).AsString());
        Assert.Equal("id", array.GetElement(1).AsString());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.False(array.GetElement(3).AsBoolean());
        Assert.True(array.GetElement(4).AsBoolean());
        Assert.Equal(string.Empty, array.GetElement(5).AsString());
        Assert.Equal("[test262]", array.GetElement(6).AsString());
    }

    [Fact]
    public async Task Assignment_AssignsNameToParenthesizedAnonymousFunction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var xCover;
            var cover;

            xCover = (0, function() {});
            cover = (function() {});

            [
              xCover.name,
              cover.name,
              Object.getOwnPropertyDescriptor(cover, "name").writable,
              Object.getOwnPropertyDescriptor(cover, "name").enumerable,
              Object.getOwnPropertyDescriptor(cover, "name").configurable
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.NotEqual("xCover", array.GetElement(0).AsString());
        Assert.Equal("cover", array.GetElement(1).AsString());
        Assert.False(array.GetElement(2).AsBoolean());
        Assert.False(array.GetElement(3).AsBoolean());
        Assert.True(array.GetElement(4).AsBoolean());
    }

    [Fact]
    public async Task BigIntLiteralPropertyNames_AreLoweredToStringPropertyKeys()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let o = { 999999999999999999n: true };
            let methodHolder = { 1n() { return "bar"; } };

            class C {
              1n() { return "baz"; }
            }

            let { 1n: a } = { "1": "foo" };

            [
              o["999999999999999999"],
              methodHolder["1"](),
              new C()["1"](),
              a
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.True(array.GetElement(0).AsBoolean());
        Assert.Equal("bar", array.GetElement(1).AsString());
        Assert.Equal("baz", array.GetElement(2).AsString());
        Assert.Equal("foo", array.GetElement(3).AsString());
    }

    [Fact]
    public async Task ComputedObjectLiteralSetter_DefinesSetterDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var key = "value";
            var seen = "unset";
            var obj = {
              set [key](next) {
                seen = next;
              }
            };

            var descriptor = Object.getOwnPropertyDescriptor(obj, "value");
            obj.value = "called";

            [
              typeof descriptor.set,
              descriptor.get === undefined,
              descriptor.enumerable,
              descriptor.configurable,
              seen
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("function", array.GetElement(0).AsString());
        Assert.True(array.GetElement(1).AsBoolean());
        Assert.True(array.GetElement(2).AsBoolean());
        Assert.True(array.GetElement(3).AsBoolean());
        Assert.Equal("called", array.GetElement(4).AsString());
    }
}
