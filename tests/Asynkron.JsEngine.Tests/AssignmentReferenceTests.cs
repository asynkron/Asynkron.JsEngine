using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class AssignmentReferenceTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task InheritedNonWritableDataProperty_Sloppy_IgnoresWrite()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
function Foo() {}
Object.defineProperty(Foo.prototype, 'bar', { value: 1, writable: false });
var o = new Foo();
o.bar = 2;
({ own: o.hasOwnProperty('bar'), value: o.bar });
");

        var obj = Assert.IsType<JsTypes.JsObject>(result);
        Assert.True(obj.TryGetProperty("own", out var own));
        Assert.False(own.AsBoolean());
        Assert.True(obj.TryGetProperty("value", out var value));
        Assert.Equal(1d, value.AsDouble());
    }

    [Fact]
    public async Task InheritedNonWritableDataProperty_Strict_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () =>
        {
            await engine.Evaluate(@"
""use strict"";
function Foo() {}
Object.defineProperty(Foo.prototype, 'bar', { value: 1, writable: false });
var o = new Foo();
o.bar = 2;
");
        });

        Assert.IsType<JsTypes.JsObject>(ex.ThrownValue.ToObject());
    }
}

