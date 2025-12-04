using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static HostFunction EnsureTypedArrayIntrinsic(RealmState realm)
    {
        if (realm.TypedArrayPrototype is null)
        {
            var proto = new JsObject(realm.ObjectPrototype);

            var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
            proto.DefineProperty(tagKey,
                new PropertyDescriptor
                {
                    Value = "TypedArray", Writable = false, Enumerable = false, Configurable = true
                });

            proto.SetHostedProperty("reduce",
                (thisValue, reduceArgs, realmState) =>
                    TypedArrayReduce(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduce", false),
                realm);
            proto.SetHostedProperty("reduceRight",
                (thisValue, reduceArgs, realmState) =>
                    TypedArrayReduce(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduceRight", true),
                realm);
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

            var valuesIterator = new HostFunction((thisValue, _) =>
            {
                var typedArray = ValidateTypedArrayReceiver(thisValue, "%TypedArray%.prototype.values", realm);
                return CreateArrayIteratorObject(typedArray, idx => typedArray.GetValueForIndex((int)idx), realm);
            }, realm, isConstructor: false);
            valuesIterator.DefineProperty("name",
                new PropertyDescriptor { Value = "values", Writable = false, Enumerable = false, Configurable = true });
            valuesIterator.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });

            var keysIterator = new HostFunction((thisValue, _) =>
            {
                var typedArray = ValidateTypedArrayReceiver(thisValue, "%TypedArray%.prototype.keys", realm);
                return CreateArrayIteratorObject(typedArray, idx => (double)idx, realm);
            }, realm, isConstructor: false);
            keysIterator.DefineProperty("name",
                new PropertyDescriptor { Value = "keys", Writable = false, Enumerable = false, Configurable = true });
            keysIterator.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });

            var entriesIterator = new HostFunction((thisValue, _) =>
            {
                var typedArray = ValidateTypedArrayReceiver(thisValue, "%TypedArray%.prototype.entries", realm);
                return CreateArrayIteratorObject(
                    typedArray,
                    idx =>
                    {
                        var pair = new JsArray(realm);
                        pair.Push((double)idx);
                        pair.Push(typedArray.GetValueForIndex((int)idx));
                        return pair;
                    },
                    realm);
            }, realm, isConstructor: false);
            entriesIterator.DefineProperty("name",
                new PropertyDescriptor { Value = "entries", Writable = false, Enumerable = false, Configurable = true });
            entriesIterator.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });

            proto.DefineProperty(iteratorKey,
                new PropertyDescriptor
                {
                    Value = valuesIterator,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
            proto.DefineProperty("values",
                new PropertyDescriptor { Value = valuesIterator, Writable = true, Enumerable = false, Configurable = true });
            proto.DefineProperty("keys",
                new PropertyDescriptor { Value = keysIterator, Writable = true, Enumerable = false, Configurable = true });
            proto.DefineProperty("entries",
                new PropertyDescriptor { Value = entriesIterator, Writable = true, Enumerable = false, Configurable = true });
            DefineTypedArrayFunction(proto, "map", 1d, TypedArrayMap, realm);
            DefineTypedArrayFunction(proto, "filter", 1d, TypedArrayFilter, realm);
            DefineTypedArrayFunction(proto, "every", 1d, TypedArrayEvery, realm);
            DefineTypedArrayFunction(proto, "find", 1d, TypedArrayFind, realm);
            DefineTypedArrayFunction(proto, "findIndex", 1d, TypedArrayFindIndex, realm);
            DefineTypedArrayFunction(proto, "findLast", 1d, TypedArrayFindLast, realm);
            DefineTypedArrayFunction(proto, "findLastIndex", 1d, TypedArrayFindLastIndex, realm);
            DefineTypedArrayFunction(proto, "forEach", 1d, TypedArrayForEach, realm);
            DefineTypedArrayFunction(proto, "fill", 1d, TypedArrayFill, realm);
            DefineTypedArrayFunction(proto, "copyWithin", 2d, TypedArrayCopyWithin, realm);
            DefineTypedArrayFunction(proto, "reverse", 0d, TypedArrayReverse, realm);
            DefineTypedArrayFunction(proto, "toReversed", 0d, TypedArrayToReversed, realm);
            DefineTypedArrayFunction(proto, "toSorted", 1d, TypedArrayToSorted, realm);
            DefineTypedArrayFunction(proto, "toSpliced", 2d, TypedArrayToSpliced, realm);
            DefineTypedArrayFunction(proto, "with", 2d, TypedArrayWith, realm);
            proto.SetHostedProperty("indexOf", TypedArrayIndexOf, realm);
            proto.SetHostedProperty("lastIndexOf", TypedArrayLastIndexOf, realm);
            proto.SetHostedProperty("includes", TypedArrayIncludes, realm);
            proto.SetHostedProperty("some",
                (thisValue, someArgs, realmState) =>
                    SomeLike(thisValue, someArgs, realmState, "%TypedArray%.prototype.some"), realm);

            realm.TypedArrayPrototype = proto;
        }

        if (realm.TypedArrayConstructor is null)
        {
            var ctor = new HostFunction((_, _) => throw ThrowTypeError("TypedArray is not a constructor", realm: realm),
                realm) { IsConstructor = true };
            ctor.DefineProperty("prototype",
                new PropertyDescriptor
                {
                    Value = realm.TypedArrayPrototype!, Writable = false, Enumerable = false, Configurable = false
                });
            realm.TypedArrayPrototype!.DefineProperty("constructor",
                new PropertyDescriptor { Value = ctor, Writable = true, Enumerable = false, Configurable = true });
            realm.TypedArrayConstructor = ctor;
        }

        return realm.TypedArrayConstructor!;
    }

    public static HostFunction CreateArrayBufferConstructor(RealmState realm)
    {
        var prototype = new JsObject(realm.ObjectPrototype);

        var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        prototype.DefineProperty(tagKey,
            new PropertyDescriptor
            {
                Value = "ArrayBuffer", Writable = false, Enumerable = false, Configurable = true
            });

        var constructor = new HostFunction(ArrayBufferCtor, realm) { IsConstructor = true };
        constructor.DefineProperty("prototype",
            new PropertyDescriptor { Value = prototype, Writable = false, Enumerable = false, Configurable = false });
        prototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = constructor, Writable = true, Enumerable = false, Configurable = true });
        realm.ArrayBufferPrototype ??= prototype;
        realm.ArrayBufferConstructor ??= constructor;

        constructor.SetHostedProperty("isView", ArrayBufferIsView);
        prototype.SetHostedProperty("slice",
            (thisValue, args) =>
            {
                if (thisValue is not JsArrayBuffer buffer)
                {
                    throw ThrowTypeError("ArrayBuffer.prototype.slice called on incompatible receiver", realm: realm);
                }

                var begin = args.Count > 0 && args[0] is double d1 ? (int)d1 : 0;
                var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : buffer.ByteLength;
                return buffer.Slice(begin, end);
            });
        prototype.SetHostedProperty("resize",
            (thisValue, args) =>
            {
                if (thisValue is not JsArrayBuffer buffer)
                {
                    throw ThrowTypeError("ArrayBuffer.prototype.resize called on incompatible receiver", realm: realm);
                }

                if (!buffer.Resizable)
                {
                    throw new ThrowSignal(buffer.CreateTypeError("ArrayBuffer is not resizable"));
                }

                if (args.Count == 0 || args[0] is not double d)
                {
                    throw ThrowTypeError("resize requires a new length", realm: realm);
                }

                buffer.Resize((int)d);
                return Symbol.Undefined;
            });

        return constructor;

        object? ArrayBufferCtor(object? _, IReadOnlyList<object?> args)
        {
            var length = args.Count > 0 ? args[0] : 0d;
            var byteLength = length switch
            {
                double d => (int)d,
                int i => i,
                _ => 0
            };

            int? maxByteLength = null;
            if (args.Count <= 1 || args[1] is not JsObject opts)
            {
                return new JsArrayBuffer(byteLength, maxByteLength, realm);
            }

            if (opts.TryGetProperty("maxByteLength", out var maxVal) && maxVal is double maxD)
            {
                maxByteLength = (int)maxD;
            }

            return new JsArrayBuffer(byteLength, maxByteLength, realm);
        }
    }

    /// <summary>
    ///     Creates the DataView constructor.
    /// </summary>
    public static HostFunction CreateDataViewConstructor(RealmState realm)
    {
        var constructor = new HostFunction(DataViewCtor);
        constructor.RealmState = realm;
        return constructor;

        object? DataViewCtor(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0 || args[0] is not JsArrayBuffer buffer)
            {
                throw new InvalidOperationException("DataView requires an ArrayBuffer");
            }

            var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            int? byteLength = args.Count > 2 && args[2] is double d2 ? (int)d2 : null;

            return new JsDataView(buffer, byteOffset, byteLength);
        }
    }

    /// <summary>
    ///     Creates a typed array constructor.
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
        constructor = new HostFunction((thisValue, args) => ConstructTypedArray(args, thisValue ?? constructor));
        constructor.RealmState = realm;
        constructor.SetInvokeWithContext(
            (args, _, _, newTarget) => ConstructTypedArray(args, newTarget ?? constructor));

        constructor.SetProperty("BYTES_PER_ELEMENT", (double)bytesPerElement);
        prototype.SetPrototype(realm.ObjectPrototype);
        prototype.SetProperty("constructor", constructor);
        constructor.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = constructorName, Writable = false, Enumerable = false, Configurable = true
            });
        constructor.DefineProperty("of",
            new PropertyDescriptor
            {
                Value = new HostFunction(TypedArrayOf, isConstructor: false),
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        constructor.DefineProperty("from",
            new PropertyDescriptor
            {
                Value = new HostFunction(TypedArrayFrom, isConstructor: false),
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        prototype.SetHostedProperty("reduce",
            (thisValue, reduceArgs, realmState) =>
                TypedArrayReduce(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduce", false),
            realm);
        prototype.SetHostedProperty("reduceRight",
            (thisValue, reduceArgs, realmState) =>
                TypedArrayReduce(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduceRight",
                    true),
            realm);
        if (sharedPrototype is not null)
        {
            prototype.SetPrototype(sharedPrototype);
        }

        // Ensure per-constructor prototypes do not own shared methods that should
        // live on %TypedArray%.prototype.
        prototype.DeleteOwnProperty("indexOf");
        prototype.DeleteOwnProperty("lastIndexOf");
        prototype.DeleteOwnProperty("includes");

        constructor.SetProperty("prototype", prototype);
        constructor.Properties.SetPrototype(sharedTypedArrayCtor.PropertiesObject);

        return constructor;

        IJsPropertyAccessor ResolvePrototype(object? newTarget)
        {
            if (newTarget is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("prototype", out var protoVal) &&
                protoVal is IJsPropertyAccessor protoObj)
            {
                return protoObj;
            }

            if (newTarget is not null &&
                TryGetRealmInfo(newTarget, out var newTargetRealmState, out var newTargetRealmObject))
            {
                var realmGlobal = newTargetRealmObject ?? newTargetRealmState?.Engine?.GlobalObject;
                if (realmGlobal is not null &&
                    realmGlobal.TryGetProperty(constructorName, out var realmCtor) &&
                    realmCtor is IJsPropertyAccessor realmCtorAccessor &&
                    realmCtorAccessor.TryGetProperty("prototype", out var realmProtoVal) &&
                    realmProtoVal is IJsPropertyAccessor realmProto)
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

        T CreateTargetFromLength(int length, object? newTarget)
        {
            var target = fromLength(length, realm);
            target.SetPrototype(ResolvePrototype(newTarget));
            return target;
        }

        object? ConstructTypedArray(IReadOnlyList<object?> args, object? newTarget)
        {
            if (args.Count == 0)
            {
                return CreateTargetFromLength(0, newTarget);
            }

            var firstArg = args[0];
            realm.Logger?.LogInformation(
                "TypedArray ctor entry {Ctor} arg0Type={ArgType} newTargetType={NewTargetType}",
                constructorName,
                firstArg?.GetType().Name ?? "null",
                newTarget?.GetType().Name ?? "null");

            // TypedArray(length)
            if (firstArg is double d)
            {
                return CreateTargetFromLength((int)d, newTarget);
            }

            // TypedArray(array)
            if (firstArg is JsArray array)
            {
                var ta = fromArray(array, realm);
                ta.SetPrototype(ResolvePrototype(newTarget));
                return ta;
            }

            // TypedArray(typedArray)
            if (firstArg is TypedArrayBase srcTypedArray)
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
                realm.Logger?.LogInformation("TypedArray ctor target length={Length} newTarget={NewTargetType}", ta.Length,
                    newTarget?.GetType().Name ?? "null");
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
                        JsBigInt64Array bi64 => bi64.GetBigIntElement(i),
                        JsBigUint64Array bu64 => bu64.GetBigIntElement(i),
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
            if (firstArg is JsArrayBuffer buffer)
            {
                var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;

                var lengthProvided = args.Count > 2 && args[2] is double;
                var length = lengthProvided
                    ? (int)(double)args[2]!
                    : (buffer.ByteLength - byteOffset) / bytesPerElement;
                var isLengthTracking = buffer.Resizable && !lengthProvided;

                var ta = fromBuffer(buffer, byteOffset, length, isLengthTracking, realm);
                ta.SetPrototype(ResolvePrototype(newTarget));
                return ta;
            }

            return CreateTargetFromLength(0, newTarget);
        }

        object? TypedArrayOf(object? thisValue, IReadOnlyList<object?> args)
        {
            if (thisValue is not HostFunction ctor)
            {
                throw ThrowTypeError("%TypedArray%.of called on incompatible receiver");
            }

            var length = args.Count;
            var taObj = ctor.Invoke([(double)length], ctor);
            if (taObj is not TypedArrayBase typed)
            {
                throw ThrowTypeError("%TypedArray%.of constructor did not return a typed array");
            }

            for (var i = 0; i < length; i++)
            {
                typed.SetValue(i, args[i]);
            }

            return typed;
        }

        object? TypedArrayFrom(object? thisValue, IReadOnlyList<object?> args)
        {
            var callingEnv = (thisValue as HostFunction)?.CallingJsEnvironment;
            var targetProtoSource = thisValue ?? constructor;
            IJsCallable? mapFn = null;
            object? mapThis = Symbol.Undefined;

            if (args.Count == 0)
            {
                return CreateTarget(0);
            }

            if (args.Count > 1 && !ReferenceEquals(args[1], Symbol.Undefined))
            {
                if (args[1] is not IJsCallable callableMap)
                {
                    throw new ThrowSignal(WrapTypeError("mapfn is not callable", callingEnv));
                }

                mapFn = callableMap;
                mapThis = args.GetArgument(2);
            }

            var source = args[0];
            switch (source)
            {
                case JsArray jsArray:
                {
                    var target = CreateTarget(jsArray.Items.Count);
                    for (var i = 0; i < jsArray.Items.Count; i++)
                    {
                        target.SetValue(i, ApplyMap(i, jsArray.Items[i]));
                    }

                    return target;
                }
                case TypedArrayBase typedSource:
                {
                    var target = CreateTarget(typedSource.Length);
                    for (var i = 0; i < typedSource.Length; i++)
                    {
                        object? value = typedSource switch
                        {
                            JsBigInt64Array bi64 => bi64.GetBigIntElement(i),
                            JsBigUint64Array bu64 => bu64.GetBigIntElement(i),
                            _ => typedSource.GetElement(i)
                        };
                        target.SetValue(i, ApplyMap(i, value));
                    }

                    return target;
                }
            }

            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
            if (source is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty(iteratorKey, out var methodVal) &&
                !ReferenceEquals(methodVal, Symbol.Undefined))
            {
                if (methodVal is not IJsCallable callableIterator)
                {
                    throw new ThrowSignal(WrapTypeError("Iterator method is not callable", callingEnv));
                }

                var iteratorObj = callableIterator.Invoke([], source);
                if (iteratorObj is not IJsPropertyAccessor iteratorAccessor)
                {
                    throw new ThrowSignal(WrapTypeError("Iterator method did not return an object", callingEnv));
                }

                if (!iteratorAccessor.TryGetProperty("next", out var nextVal) ||
                    nextVal is not IJsCallable nextCallable)
                {
                    throw new ThrowSignal(WrapTypeError("Iterator result does not expose next", callingEnv));
                }

                var collected = new List<object?>();
                while (true)
                {
                    var nextResult = nextCallable.Invoke([], iteratorObj);
                    if (nextResult is not IJsPropertyAccessor nextResultAccessor)
                    {
                        throw new ThrowSignal(WrapTypeError("Iterator result is not an object", callingEnv));
                    }

                    var done = nextResultAccessor.TryGetProperty("done", out var doneVal) &&
                               JsOps.ToBoolean(doneVal);
                    if (done)
                    {
                        var target = CreateTarget(collected.Count);
                        for (var i = 0; i < collected.Count; i++)
                        {
                            target.SetValue(i, ApplyMap(i, collected[i]));
                        }

                        return target;
                    }

                    var value = nextResultAccessor.TryGetProperty("value", out var valueVal)
                        ? valueVal
                        : Symbol.Undefined;
                    collected.Add(value);
                }
            }

            if (source is IJsPropertyAccessor arrayLike &&
                arrayLike.TryGetProperty("length", out var lengthVal))
            {
                var lenNumber = JsOps.ToNumberWithContext(lengthVal);
                var length = double.IsNaN(lenNumber) || lenNumber < 0
                    ? 0
                    : (int)Math.Min(lenNumber, int.MaxValue);
                var target = CreateTarget(length);
                for (var i = 0; i < length; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    var hasElement = arrayLike.TryGetProperty(key, out var element);
                    target.SetValue(i, ApplyMap(i, hasElement ? element : Symbol.Undefined));
                }

                return target;
            }

            return CreateTarget(0);

            IJsCallable? ResolveTypeErrorCtor(JsEnvironment? env)
            {
                if (env is not null &&
                    env.TryGet(Symbol.TypeErrorIdentifier, out var typeErrorVal) &&
                    typeErrorVal is IJsCallable typeErrorFromEnv)
                {
                    return typeErrorFromEnv;
                }

                return realm.TypeErrorConstructor;
            }

            object WrapTypeError(string message, JsEnvironment? env)
            {
                var typeErrorCtor = ResolveTypeErrorCtor(env);
                if (typeErrorCtor is null)
                {
                    return new InvalidOperationException(message);
                }

                var errorValue = typeErrorCtor.Invoke([message], null);
                if (errorValue is JsObject errorObj)
                {
                    errorObj.SetProperty("constructor", typeErrorCtor);
                }

                return errorValue ?? new InvalidOperationException(message);
            }

            TypedArrayBase CreateTarget(int length)
            {
                var target = fromLength(length, realm);
                target.SetPrototype(ResolvePrototype(targetProtoSource));
                return target;
            }

            object? ApplyMap(int index, object? value)
            {
                return mapFn is null ? value : mapFn.Invoke([value, (double)index], mapThis);
            }
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

    private static object? TypedArrayMap(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.map called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.map expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var result = TypedArraySpeciesCreate(typedArray, length, realm);
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            var mapped = callback.Invoke([value, (double)k, typedArray], thisArg);
            result.SetValue(k, mapped);
        }

        return result;
    }

    private static object? TypedArrayFilter(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.filter called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.filter expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var kept = new List<object?>();
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (IsTruthy(result))
            {
                kept.Add(value);
            }
        }

        var filtered = TypedArraySpeciesCreate(typedArray, kept.Count, realm);
        for (var i = 0; i < kept.Count; i++)
        {
            filtered.SetValue(i, kept[i]);
        }

        return filtered;
    }

    private static object? TypedArrayReduce(object? thisValue, IReadOnlyList<object?> args, RealmState? realm,
        string methodName, bool fromRight)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} requires a callable accumulator", realm: realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var step = fromRight ? -1 : 1;
        var k = fromRight ? length - 1 : 0;

        object? accumulator = Symbol.Undefined;
        var hasAccumulator = false;
        if (args.Count > 1 && !ReferenceEquals(args[1], Symbol.Undefined))
        {
            accumulator = args[1];
            hasAccumulator = true;
        }

        var visited = 0;

        while (k >= 0 && k < length)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            visited++;

            if (!hasAccumulator)
            {
                accumulator = value;
                hasAccumulator = true;
            }
            else
            {
                accumulator = callback.Invoke([accumulator, value, (double)k, typedArray], Symbol.Undefined);
            }

            k += step;
        }

        if (!hasAccumulator)
        {
            throw ThrowTypeError($"{methodName} requires at least one element", realm: realm);
        }

        return accumulator;
    }

    private static object? TypedArrayEvery(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.every called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.every expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (realm?.Logger is { } logger)
        {
            var strictField = callback.GetType().GetField("_isStrict", BindingFlags.NonPublic | BindingFlags.Instance);
            var strictValue = strictField?.GetValue(callback);
            var thisArgKind = thisArg is Symbol sym && ReferenceEquals(sym, Symbol.Undefined)
                ? "undefined"
                : thisArg?.GetType().Name ?? "null";
            logger.LogInformation(
                "TypedArray.every callback type={Type} strict={Strict} thisArg={ThisArg}",
                callback.GetType().Name,
                strictValue ?? "null",
                thisArgKind);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (!IsTruthy(result))
            {
                return false;
            }
        }

        return true;
    }

    private static object? TypedArrayFind(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.find called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.find expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, typedArray, out var candidate) ? candidate : Symbol.Undefined;
            var match = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    private static object? TypedArrayFindIndex(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findIndex called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.findIndex expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, typedArray, out var candidate) ? candidate : Symbol.Undefined;
            var match = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    private static object? TypedArrayFindLast(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findLast called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.findLast expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, typedArray, out var candidate) ? candidate : Symbol.Undefined;
            var match = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    private static object? TypedArrayFindLastIndex(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findLastIndex called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.findLastIndex expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, typedArray, out var candidate) ? candidate : Symbol.Undefined;
            var match = callback.Invoke([value, (double)k, typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    private static object? TypedArrayForEach(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.forEach called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("TypedArray.prototype.forEach expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            callback.Invoke([value, (double)k, typedArray], thisArg);
        }

        return Symbol.Undefined;
    }

    private static object? TypedArrayFill(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.fill called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var value = args.GetArgument(0);
        var startIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], realm?.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], realm?.CreateContext()) : length;

        var start = (int)ClampRelativeIndex(startIndex, length);
        var end = (int)ClampRelativeIndex(endIndex, length);

        for (var k = start; k < end; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            typedArray.SetValue(k, value);
        }

        return typedArray;
    }

    private static object? TypedArrayCopyWithin(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.copyWithin called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var toIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], realm?.CreateContext()) : 0;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], realm?.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], realm?.CreateContext()) : length;

        var to = (int)ClampRelativeIndex(toIndex, length);
        var from = (int)ClampRelativeIndex(fromIndex, length);
        var final = (int)ClampRelativeIndex(endIndex, length);

        var count = Math.Min(final - from, length - to);
        if (count <= 0)
        {
            return typedArray;
        }

        var direction = 1;
        if (from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }

        for (var i = 0; i < count; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var currentLength = typedArray.Length;
            if (from < 0 || from >= currentLength || to < 0 || to >= currentLength)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(from);
            typedArray.SetValue(to, value);

            from += direction;
            to += direction;
        }

        return typedArray;
    }

    private static object? TypedArrayReverse(object? thisValue, IReadOnlyList<object?> _, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.reverse called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var middle = length / 2;

        for (var lower = 0; lower < middle; lower++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var upper = length - lower - 1;
            var lowerValue = typedArray.GetValueForIndex(lower);

            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var upperValue = typedArray.GetValueForIndex(upper);
            typedArray.SetValue(lower, upperValue);
            typedArray.SetValue(upper, lowerValue);
        }

        return typedArray;
    }

    private static object? TypedArrayToReversed(object? thisValue, IReadOnlyList<object?> _, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toReversed called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var result = TypedArraySpeciesCreate(typedArray, length, realm);
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = typedArray.GetValueForIndex(length - 1 - k);
            result.SetValue(k, value);
        }

        return result;
    }

    private static object? TypedArrayToSorted(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toSorted called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        IJsCallable? compareFn = null;
        if (args.Count > 0 && !ReferenceEquals(args[0], Symbol.Undefined))
        {
            if (args[0] is not IJsCallable callable)
            {
                throw ThrowTypeError("TypedArray.prototype.toSorted comparator must be callable", realm: realm);
            }

            compareFn = callable;
        }

        var length = typedArray.Length;
        var values = new List<object?>(length);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            values.Add(typedArray.GetValueForIndex(i));
        }

        Comparison<object?> comparer = (left, right) =>
        {
            if (compareFn is not null)
            {
                var result = compareFn.Invoke([left, right], Symbol.Undefined);
                var numeric = JsOps.ToNumber(result);
                return numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
            }

            if (typedArray.IsBigIntArray)
            {
                var leftBig = left as JsBigInt ?? StandardLibrary.ToBigInt(left, realmState: realm);
                var rightBig = right as JsBigInt ?? StandardLibrary.ToBigInt(right, realmState: realm);
                return leftBig.Value.CompareTo(rightBig.Value);
            }

            var leftNum = JsOps.ToNumber(left);
            var rightNum = JsOps.ToNumber(right);
            if (double.IsNaN(leftNum))
            {
                return double.IsNaN(rightNum) ? 0 : 1;
            }

            if (double.IsNaN(rightNum))
            {
                return -1;
            }

            return leftNum.CompareTo(rightNum);
        };

        values.Sort(comparer);

        var result = TypedArraySpeciesCreate(typedArray, length, realm);
        for (var i = 0; i < values.Count; i++)
        {
            result.SetValue(i, values[i]);
        }

        return result;
    }

    private static object? TypedArrayToSpliced(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toSpliced called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var start = args.Count > 0 ? ToIntegerOrInfinity(args[0], realm?.CreateContext()) : 0;
        var actualStart = ClampRelativeIndex(start, length);

        var deleteCountIsUndefined = args.Count <= 1 || ReferenceEquals(args[1], Symbol.Undefined);
        int actualDeleteCount;
        if (deleteCountIsUndefined)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCount = ToIntegerOrInfinity(args[1], realm?.CreateContext());
            if (double.IsPositiveInfinity(deleteCount))
            {
                actualDeleteCount = length - actualStart;
            }
            else
            {
                var bounded = Math.Max(deleteCount, 0);
                bounded = Math.Min(bounded, length - actualStart);
                actualDeleteCount = (int)bounded;
            }
        }
        var insertCount = Math.Max(args.Count - 2, 0);
        var newLength = length - actualDeleteCount + insertCount;

        var result = TypedArraySpeciesCreate(typedArray, newLength, realm);
        var targetIndex = 0;

        for (var i = 0; i < actualStart; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            result.SetValue(targetIndex++, typedArray.GetValueForIndex(i));
        }

        for (var i = 0; i < insertCount; i++)
        {
            result.SetValue(targetIndex++, args[i + 2]);
        }

        for (var i = actualStart + actualDeleteCount; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            result.SetValue(targetIndex++, typedArray.GetValueForIndex(i));
        }

        return result;
    }

    private static object? TypedArrayWith(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.with called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        if (args.Count < 2)
        {
            throw ThrowTypeError("TypedArray.prototype.with requires index and value arguments", realm: realm);
        }

        var length = typedArray.Length;
        var indexNumber = ToIntegerOrInfinity(args[0], realm?.CreateContext());
        int actualIndex;
        if (double.IsPositiveInfinity(indexNumber) || double.IsNegativeInfinity(indexNumber))
        {
            actualIndex = indexNumber > 0 ? length : -1;
        }
        else
        {
            var truncated = (int)Math.Truncate(indexNumber);
            actualIndex = truncated < 0 ? length + truncated : truncated;
        }

        if (actualIndex < 0 || actualIndex >= length)
        {
            throw ThrowRangeError("Index out of range", realm: realm);
        }

        var result = TypedArraySpeciesCreate(typedArray, length, realm);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = i == actualIndex ? args[1] : typedArray.GetValueForIndex(i);
            result.SetValue(i, value);
        }

        return result;
    }

    private static void DefineTypedArrayFunction(JsObject target, string name, double length,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState realm)
    {
        var fn = new HostFunction(handler, realm, isConstructor: false);
        fn.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });
        fn.DefineProperty("length",
            new PropertyDescriptor { Value = length, Writable = false, Enumerable = false, Configurable = true });

        target.DefineProperty(name,
            new PropertyDescriptor { Value = fn, Writable = true, Enumerable = false, Configurable = true });
    }

    private static object? ArrayBufferIsView(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        return args[0] is TypedArrayBase || args[0] is JsDataView;
    }

    private static object? TypedArrayIndexOf(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.indexOf called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.IndexOfInternal(typed, args);
    }

    private static object? TypedArrayLastIndexOf(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.lastIndexOf called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.LastIndexOfInternal(typed, args);
    }

    private static object? TypedArrayIncludes(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.includes called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.IncludesInternal(typed, args);
    }

    private static TypedArrayBase ValidateTypedArrayReceiver(object? thisValue, string methodName, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        return typedArray;
    }

    private static TypedArrayBase TypedArraySpeciesCreate(TypedArrayBase exemplar, int length, RealmState? realm)
    {
        length = Math.Max(length, 0);
        object? constructorValue = null;

        if (exemplar.TryGetProperty("constructor", exemplar, out var ctorValue))
        {
            constructorValue = ctorValue;
        }

        if (constructorValue is IJsPropertyAccessor ctorAccessor &&
            ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
        {
            constructorValue = speciesValue;
        }

        if (constructorValue is null || ReferenceEquals(constructorValue, Symbol.Undefined))
        {
            return CreateDefaultTypedArray(exemplar, length);
        }

        if (!JsOps.IsConstructor(constructorValue) || constructorValue is not IJsCallable callable)
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: realm);
        }

        var constructed = callable.Invoke([(double)length], null);
        if (constructed is not TypedArrayBase typedResult)
        {
            throw ThrowTypeError("TypedArray species constructor did not return a TypedArray instance", realm: realm);
        }

        if (typedResult.Length < length)
        {
            throw ThrowTypeError("TypedArray species constructor result has insufficient length", realm: realm);
        }

        return typedResult;

        static TypedArrayBase CreateDefaultTypedArray(TypedArrayBase exemplarArray, int len)
        {
            var fallback = exemplarArray.CreateSpeciesDefault(len);
            if (exemplarArray.Prototype is not null)
            {
                fallback.SetPrototype(exemplarArray.Prototype);
            }

            return fallback;
        }
    }

    private static int ClampRelativeIndex(double index, int length)
    {
        if (double.IsNegativeInfinity(index))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(index))
        {
            return length;
        }

        var integer = (int)Math.Truncate(index);
        if (integer < 0)
        {
            var relative = length + integer;
            return relative < 0 ? 0 : relative;
        }

        return integer > length ? length : integer;
    }
}
