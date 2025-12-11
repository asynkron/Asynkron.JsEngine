using System;
using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("ArrayBuffer", ToStringTag = "ArrayBuffer")]
public sealed partial class ArrayBufferPrototype : JsPrototype
{
    [JsHostMethod("slice", Length = 2d)]
    public object? Slice(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        var len = buffer.ByteLength;

        var begin = args.Count > 0 ? JsOps.ToNumber(args[0]) : 0d;
        var end = args.Count > 1 ? JsOps.ToNumber(args[1]) : len;

        var first = begin < 0 ? Math.Max(len + (int)begin, 0) : Math.Min((int)begin, len);
        var final = end < 0 ? Math.Max(len + (int)end, 0) : Math.Min((int)end, len);
        var newLen = Math.Max(final - first, 0);

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.ArrayBufferConstructor!);

        object? newBuffer;
        if (JsOps.IsConstructor(speciesConstructor) && speciesConstructor is IJsCallable speciesCtor)
        {
            newBuffer = Construct(speciesCtor, [(double)newLen], speciesCtor, Realm);
        }
        else
        {
            newBuffer = new JsArrayBuffer(newLen, null, Realm);
        }

        var targetBuffer = RequireArrayBuffer(newBuffer, Realm);
        if (newLen > 0)
        {
            Array.Copy(buffer.Buffer, first, targetBuffer.Buffer, 0, newLen);
        }

        return newBuffer;
    }

    [JsHostMethod("resize", Length = 1d)]
    public object? Resize(object? thisValue, IReadOnlyList<object?> args)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);

        if (!buffer.Resizable)
        {
            throw ThrowTypeError("ArrayBuffer is not resizable", realm: Realm);
        }

        var arg = args.GetArgument(0);
        if (arg is not double d)
        {
            throw ThrowTypeError("resize requires a new length", realm: Realm);
        }

        buffer.Resize((int)d);
        return Symbol.Undefined;
    }

    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        return (double)buffer.ByteLength;
    }

    [JsHostGetter("maxByteLength")]
    public object MaxByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        return (double)buffer.MaxByteLength;
    }

    [JsHostGetter("resizable")]
    public object Resizable(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        return buffer.Resizable;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.ArrayBufferPrototype ??= Prototype as JsObject;
    }
}
