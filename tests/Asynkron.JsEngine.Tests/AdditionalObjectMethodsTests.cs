using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibObject)]
public sealed class AdditionalObjectMethodsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyNames_ReturnsAllPropertyNames()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1, b: 2, c: 3 };
                                                       let names = Object.getOwnPropertyNames(obj);
                                                       names.length;

                                           """);
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyNames_IncludesProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 10, y: 20 };
                                                       let names = Object.getOwnPropertyNames(obj);
                                                       names.includes('x') && names.includes('y');

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyNames_WithEmptyObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       let names = Object.getOwnPropertyNames(obj);
                                                       names.length;

                                           """);
        Assert.Equal(0d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_ReturnsDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       let desc = Object.getOwnPropertyDescriptor(obj, 'x');
                                                       desc.value;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_HasWritableProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       let desc = Object.getOwnPropertyDescriptor(obj, 'x');
                                                       desc.writable;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_HasEnumerableProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       let desc = Object.getOwnPropertyDescriptor(obj, 'x');
                                                       desc.enumerable;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_HasConfigurableProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       let desc = Object.getOwnPropertyDescriptor(obj, 'x');
                                                       desc.configurable;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_ForFrozenObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       Object.freeze(obj);
                                                       let desc = Object.getOwnPropertyDescriptor(obj, 'x');
                                                       desc.writable;

                                           """);
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptor_ReturnsUndefinedForNonExistent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 42 };
                                                       Object.getOwnPropertyDescriptor(obj, 'y');

                                           """);
        Assert.True(ReferenceEquals(result, Symbol.Undefined));
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptors_NumberPrimitive_ReturnsEmptyDescriptorObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const descriptors = Object.getOwnPropertyDescriptors(42);
                                                       Object.getOwnPropertyNames(descriptors).length === 0 &&
                                                           !Object.prototype.hasOwnProperty.call(descriptors, '__value__');

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GetOwnPropertyDescriptors_NullAndUndefined_ThrowTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let threwForNull = false;
                                                       let threwForUndefined = false;

                                                       try {
                                                           Object.getOwnPropertyDescriptors(null);
                                                       } catch (e) {
                                                           threwForNull = e instanceof TypeError;
                                                       }

                                                       try {
                                                           Object.getOwnPropertyDescriptors(undefined);
                                                       } catch (e) {
                                                           threwForUndefined = e instanceof TypeError;
                                                       }

                                                       threwForNull && threwForUndefined;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_DefineProperty_DefinesNewProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       Object.defineProperty(obj, 'x', { value: 42 });
                                                       obj.x;

                                           """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_DefineProperty_ReturnsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       let returned = Object.defineProperty(obj, 'x', { value: 42 });
                                                       returned === obj;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_DefineProperty_UpdatesExistingProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 10 };
                                                       Object.defineProperty(obj, 'x', { value: 99 });
                                                       obj.x;

                                           """);
        Assert.Equal(99d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_DefineProperty_WithMultipleProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = {};
                                                       Object.defineProperty(obj, 'a', { value: 1 });
                                                       Object.defineProperty(obj, 'b', { value: 2 });
                                                       obj.a + obj.b;

                                           """);
        Assert.Equal(3d, result);
    }
}
