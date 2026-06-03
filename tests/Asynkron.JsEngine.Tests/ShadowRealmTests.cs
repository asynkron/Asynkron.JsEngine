using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibFunction)]
public sealed class ShadowRealmTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task EvaluateWrapsCallableWithCopiedNameAndLengthDescriptors()
    {
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            (() => {
              const realm = new ShadowRealm();
              const wrapped = realm.evaluate("(function add(a, b) { return a + b; })");

              return [
                typeof wrapped,
                wrapped.name,
                wrapped.length,
                wrapped(20, 22),
                Object.getPrototypeOf(wrapped) === Function.prototype,
                Object.getOwnPropertyDescriptor(wrapped, "name").writable,
                Object.getOwnPropertyDescriptor(wrapped, "length").writable
              ];
            })();
        """);

        var values = Assert.IsType<JsArray>(result);
        Assert.Equal("function", values.Items[0].AsString());
        Assert.Equal("add", values.Items[1].AsString());
        Assert.Equal(2d, values.Items[2].NumberValue);
        Assert.Equal(42d, values.Items[3].NumberValue);
        Assert.True(values.Items[4].AsBoolean());
        Assert.False(values.Items[5].AsBoolean());
        Assert.False(values.Items[6].AsBoolean());
    }
}
