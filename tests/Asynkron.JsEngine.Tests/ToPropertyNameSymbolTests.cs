using System.Threading.Tasks;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class ToPropertyNameSymbolTests
{
    [Fact]
    public void ThrowsWhenSymbolToPrimitiveIsNotCallable()
    {
        var symbolKey = JsOps.ToPropertyName(TypedAstSymbol.For("Symbol.toPrimitive"))
                       ?? throw new InvalidOperationException("Symbol key should not be null");

        var obj = new JsObject();
        obj.DefineProperty(symbolKey, new PropertyDescriptor
        {
            Value = 42,
            Writable = true,
            Enumerable = true,
            Configurable = true
        });

        Assert.Throws<ThrowSignal>(() => JsOps.ToPropertyName(obj));
    }

    [Fact]
    public async Task ObjectLiteralStoresWellKnownSymbolKeys()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var obj = { [Symbol.toPrimitive]: 1 }; obj;");
        var obj = Assert.IsType<JsObject>(result);

        var symbolKey = JsOps.ToPropertyName(TypedAstSymbol.For("Symbol.toPrimitive"))
                       ?? throw new InvalidOperationException("Symbol key should not be null");

        Assert.Contains(symbolKey, obj.Keys);
    }

    [Fact]
    public async Task ObjectLiteralWithExoticToPrimitiveFailsConversion()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("var obj = { [Symbol.toPrimitive]: 42 }; obj;");
        var obj = Assert.IsType<JsObject>(result);

        Assert.Throws<ThrowSignal>(() => JsOps.ToPropertyName(obj));
    }

    [Theory]
    [InlineData("var obj = { [Symbol.toPrimitive]: 42 }; class C { [obj] = 0; };")]
    [InlineData("var obj = { [Symbol.toPrimitive]: {} }; class C { [obj] = 0; };")]
    public async Task ClassComputedNamesRespectExoticToPrimitiveErrors(string source)
    {
        await using var engine = new JsEngine();
        await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate(source));
    }
}
