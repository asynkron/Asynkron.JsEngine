using System;
using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("SharedArrayBuffer", ToStringTag = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferPrototype : JsPrototype
{
    [JsHostGetter("byteLength")]
    public object ByteLength(object? thisValue)
    {
        var buffer = RequireArrayBuffer(thisValue, Realm);
        return (double)buffer.ByteLength;
    }

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

        var speciesConstructor = ArrayBufferSpeciesCreate(thisValue, Realm, Realm.SharedArrayBufferConstructor!);

        object? newBuffer;
        if (JsOps.IsConstructor(speciesConstructor) && speciesConstructor is IJsCallable speciesCtor)
        {
            newBuffer = Construct(speciesCtor, [(double)newLen], speciesCtor, Realm);
        }
        else
        {
            var created = new JsArrayBuffer(newLen, null, Realm);
            created.SetPrototype(Realm.SharedArrayBufferPrototype);
            newBuffer = created;
        }

        var targetBuffer = RequireArrayBuffer(newBuffer, Realm);
        if (newLen > 0)
        {
            Array.Copy(buffer.Buffer, first, targetBuffer.Buffer, 0, newLen);
        }

        return newBuffer;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.SharedArrayBufferPrototype ??= Prototype as JsObject;
    }
}
