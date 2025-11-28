using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Tests;

public class TypedArrayResizableTests
{
    [Fact]
    public void LastIndexOfReturnsMinusOneWhenFixedLengthViewShrinksOutOfBounds()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(4, 8, realm); // resizable buffer
        var fixedLength = new JsInt8Array(rab, 0, 4);

        var beforeShrink = TypedArrayBase.LastIndexOfInternal(fixedLength, new List<object?> { 0d });
        Assert.Equal(3d, beforeShrink);

        rab.Resize(2);

        Assert.Throws<ThrowSignal>(() =>
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<object?> { 0d, 2d }));
    }

    [Fact]
    public void BigIntLastIndexOfReturnsMinusOneWhenFixedLengthViewShrinksOutOfBounds()
    {
        var realm = new RealmState();
        var rab = new JsArrayBuffer(32, 64, realm);
        var fixedLength = new JsBigInt64Array(rab, 0, 4);
        fixedLength.SetElement(0, new JsBigInt(0));

        var beforeShrink =
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<object?> { new JsBigInt(0) });
        Assert.Equal(3d, beforeShrink);

        rab.Resize(16);

        Assert.Throws<ThrowSignal>(() =>
            TypedArrayBase.LastIndexOfInternal(fixedLength, new List<object?> { new JsBigInt(0), 2d }));
    }
}
