#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static class TypedArrayHelper
{
    internal static HostFunction EnsureTypedArrayIntrinsic(RealmState realm)
    {
        if (realm.TypedArrayPrototype is null || realm.TypedArrayConstructor is null)
        {
            return TypedArrayConstructor.CreateConstructor(realm);
        }

        return realm.TypedArrayConstructor!;
    }

    /// <summary>
    ///     Creates a typed array constructor for a specific element type.
    /// </summary>
    private static HostFunction CreateTypedArrayConstructor<T>(
        Func<int, RealmState?, T> fromLength,
        Func<JsArray, RealmState?, T> fromArray,
        Func<JsArrayBuffer, int, int, bool, RealmState?, T> fromBuffer,
        int bytesPerElement,
        string constructorName,
        RealmState realm) where T : TypedArrayBase
    {
        var sharedTypedArrayCtor = EnsureTypedArrayIntrinsic(realm);
        var sharedPrototype = realm.TypedArrayPrototype;
        var prototype = new JsObject();

        HostFunction constructor = null!;
        constructor = new HostFunction((thisValue, args) =>
            JsValue.FromObjectUnsafe(ConstructTypedArray(args, thisValue.IsNullish ? (JsValue)constructor : thisValue)))
        {
            RealmState = realm
        };
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            JsValue.FromObjectUnsafe(ConstructTypedArray(args,
                newTarget.IsNullish ? (JsValue)constructor : newTarget)));

        constructor.SetProperty("BYTES_PER_ELEMENT", JsValue.FromNumber((double)bytesPerElement));
        prototype.SetPrototype(realm.ObjectPrototype);
        prototype.SetProperty("constructor", (JsValue)constructor);
        var toStringTagKey = SymbolKeys.ToStringTag;
        prototype.DefineProperty(toStringTagKey,
            new PropertyDescriptor
            {
                Value = constructorName,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        constructor.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = constructorName,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

        if (sharedPrototype is not null)
        {
            prototype.SetPrototype(sharedPrototype);
        }

        // Ensure per-constructor prototypes do not own shared methods that should
        // live on %TypedArray%.prototype.
        prototype.DeleteOwnProperty("indexOf");
        prototype.DeleteOwnProperty("lastIndexOf");
        prototype.DeleteOwnProperty("includes");

        constructor.SetProperty("prototype", (JsValue)prototype);
        // Set the [[Prototype]] of each concrete TypedArray constructor to %TypedArray%
        // so that Object.getPrototypeOf(Int8Array) === %TypedArray%.
        constructor.SetPrototype(sharedTypedArrayCtor);

        return constructor;

        IJsPropertyAccessor ResolvePrototype(JsValue newTarget)
        {
            if (newTarget.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                accessor.TryGetProperty("prototype", out var protoVal) &&
                protoVal.TryGetObject<IJsPropertyAccessor>(out var protoObj))
            {
                return protoObj;
            }

            if (!newTarget.IsNullish &&
                TryGetRealmInfo(newTarget, out var newTargetRealmState, out var newTargetRealmObject))
            {
                var realmGlobal = newTargetRealmObject ?? newTargetRealmState?.Engine?.GlobalObject;
                if (realmGlobal is not null &&
                    realmGlobal.TryGetProperty(constructorName, out var realmCtor) &&
                    realmCtor.TryGetObject<IJsPropertyAccessor>(out var realmCtorAccessor) &&
                    realmCtorAccessor.TryGetProperty("prototype", out var realmProtoVal) &&
                    realmProtoVal.TryGetObject<IJsPropertyAccessor>(out var realmProto))
                {
                    return realmProto;
                }

                if (newTargetRealmState?.TypedArrayPrototype is IJsPropertyAccessor typedProto)
                {
                    return typedProto;
                }
            }

            return prototype;
        }

        T CreateTargetFromLength(int length, JsValue newTarget)
        {
            var target = fromLength(length, realm);
            target.SetPrototype(ResolvePrototype(newTarget));
            return target;
        }

        object? ConstructTypedArray(IReadOnlyList<JsValue> args, JsValue newTarget)
        {
            if (args.Count == 0)
            {
                return CreateTargetFromLength(0, newTarget);
            }

            var firstArg = args[0];
            realm.Logger?.LogInformation(
                "TypedArray ctor entry {Ctor} arg0Type={ArgType} newTargetType={NewTargetType}",
                constructorName,
                firstArg.GetType().Name,
                newTarget.GetType().Name);

            // TypedArray(length)
            if (firstArg.TryGetDouble(out var d))
            {
                return CreateTargetFromLength((int)d, newTarget);
            }

            // TypedArray(array)
            if (firstArg.TryGetObject<JsArray>(out var array))
            {
                var ta = fromArray(array, realm);
                ta.SetPrototype(ResolvePrototype(newTarget));
                return ta;
            }

            // TypedArray(typedArray)
            if (firstArg.TryGetObject<TypedArrayBase>(out var srcTypedArray))
            {
                if (srcTypedArray.IsDetachedOrOutOfBounds())
                {
                    throw srcTypedArray.CreateOutOfBoundsTypeError();
                }

                var length = srcTypedArray.Length;
                realm.Logger?.LogInformation(
                    "TypedArray ctor from typed array: srcLength={Length} srcType={Type} bufferLength={BufferLength} offset={Offset} tracking={Tracking} resizable={Resizable}",
                    length,
                    srcTypedArray.GetType().Name,
                    srcTypedArray.Buffer.ByteLength,
                    srcTypedArray.ByteOffset,
                    srcTypedArray.IsLengthTracking,
                    srcTypedArray.Buffer.Resizable);
                var ta = CreateTargetFromLength(length, newTarget);
                realm.Logger?.LogInformation("TypedArray ctor target length={Length} newTarget={NewTargetType}",
                    ta.Length,
                    newTarget.GetType().Name);
                if (length == 0)
                {
                    realm.Logger?.LogInformation(
                        "TypedArray ctor from typed array zero-length source type={Type} bufferLength={BufferLength} offset={Offset} resizable={Resizable}",
                        srcTypedArray.GetType().Name,
                        srcTypedArray.Buffer.ByteLength,
                        srcTypedArray.ByteOffset,
                        srcTypedArray.Buffer.Resizable);
                }

                for (var i = 0; i < length; i++)
                {
                    var value = srcTypedArray switch
                    {
                        JsBigInt64Array bi64 => (JsValue)bi64.GetBigIntElement(i),
                        JsBigUint64Array bu64 => (JsValue)bu64.GetBigIntElement(i),
                        _ => srcTypedArray.GetValueForIndex(i)
                    };
                    if (i < 8)
                    {
                        realm.Logger?.LogInformation("TypedArray ctor copy [{Index}]={Value}", i, value);
                    }

                    ta.SetValue(i, value);
                }

                return ta;
            }

            // TypedArray(buffer, byteOffset, length)
            if (firstArg.TryGetObject<JsArrayBuffer>(out var buffer) ||
                (firstArg.TryGetObject<IJsPropertyAccessor>(out var bufferAccessor) &&
                 bufferAccessor.TryGetProperty("_internalArrayBuffer", out var internalBufferVal) &&
                 internalBufferVal.TryGetObject<JsArrayBuffer>(out buffer)))
            {
                var byteOffset = args.Count > 1 && args[1].TryGetDouble(out var d1) ? (int)d1 : 0;

                var lengthProvided = args.Count > 2 && args[2].TryGetDouble(out _);
                var length = lengthProvided
                    ? (int)args[2].ToNumber()
                    : (buffer.ByteLength - byteOffset) / bytesPerElement;
                var isLengthTracking = buffer.Resizable && !lengthProvided;

                var ta = fromBuffer(buffer, byteOffset, length, isLengthTracking, realm);
                ta.SetPrototype(ResolvePrototype(newTarget));
                return ta;
            }

            return CreateTargetFromLength(0, newTarget);
        }
    }

    public static HostFunction CreateInt8ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsInt8Array.FromLength,
            JsInt8Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) => new JsInt8Array(buffer, offset, length, isLengthTracking),
            JsInt8Array.BYTES_PER_ELEMENT,
            "Int8Array",
            realm);
    }

    public static HostFunction CreateUint8ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsUint8Array.FromLength,
            JsUint8Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsUint8Array(buffer, offset, length, isLengthTracking),
            JsUint8Array.BYTES_PER_ELEMENT,
            "Uint8Array",
            realm);
    }

    public static HostFunction CreateUint8ClampedArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsUint8ClampedArray.FromLength,
            JsUint8ClampedArray.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsUint8ClampedArray(buffer, offset, length, isLengthTracking),
            JsUint8ClampedArray.BYTES_PER_ELEMENT,
            "Uint8ClampedArray",
            realm);
    }

    public static HostFunction CreateInt16ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsInt16Array.FromLength,
            JsInt16Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsInt16Array(buffer, offset, length, isLengthTracking),
            JsInt16Array.BYTES_PER_ELEMENT,
            "Int16Array",
            realm);
    }

    public static HostFunction CreateUint16ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsUint16Array.FromLength,
            JsUint16Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsUint16Array(buffer, offset, length, isLengthTracking),
            JsUint16Array.BYTES_PER_ELEMENT,
            "Uint16Array",
            realm);
    }

    public static HostFunction CreateInt32ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsInt32Array.FromLength,
            JsInt32Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsInt32Array(buffer, offset, length, isLengthTracking),
            JsInt32Array.BYTES_PER_ELEMENT,
            "Int32Array",
            realm);
    }

    public static HostFunction CreateUint32ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsUint32Array.FromLength,
            JsUint32Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsUint32Array(buffer, offset, length, isLengthTracking),
            JsUint32Array.BYTES_PER_ELEMENT,
            "Uint32Array",
            realm);
    }

    public static HostFunction CreateFloat32ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsFloat32Array.FromLength,
            JsFloat32Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsFloat32Array(buffer, offset, length, isLengthTracking),
            JsFloat32Array.BYTES_PER_ELEMENT,
            "Float32Array",
            realm);
    }

    public static HostFunction CreateFloat64ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsFloat64Array.FromLength,
            JsFloat64Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsFloat64Array(buffer, offset, length, isLengthTracking),
            JsFloat64Array.BYTES_PER_ELEMENT,
            "Float64Array",
            realm);
    }

    public static HostFunction CreateBigInt64ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsBigInt64Array.FromLength,
            JsBigInt64Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsBigInt64Array(buffer, offset, length, isLengthTracking),
            JsBigInt64Array.BYTES_PER_ELEMENT,
            "BigInt64Array",
            realm);
    }

    public static HostFunction CreateBigUint64ArrayConstructor(RealmState realm)
    {
        return CreateTypedArrayConstructor(
            JsBigUint64Array.FromLength,
            JsBigUint64Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsBigUint64Array(buffer, offset, length, isLengthTracking),
            JsBigUint64Array.BYTES_PER_ELEMENT,
            "BigUint64Array",
            realm);
    }
}
