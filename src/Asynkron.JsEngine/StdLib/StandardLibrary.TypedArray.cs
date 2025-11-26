using System.Globalization;
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
            var proto = new JsObject();
            if (realm.ObjectPrototype is not null)
            {
                proto.SetPrototype(realm.ObjectPrototype);
            }

            var tagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
            proto.DefineProperty(tagKey,
                new PropertyDescriptor
                {
                    Value = "TypedArray", Writable = false, Enumerable = false, Configurable = true
                });

            proto.SetHostedProperty("reduce",
                (thisValue, reduceArgs, realmState) =>
                    ReduceLike(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduce", false), realm);
            proto.SetHostedProperty("reduceRight",
                (thisValue, reduceArgs, realmState) =>
                    ReduceLike(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduceRight", true), realm);
            proto.SetHostedProperty("indexOf", TypedArrayIndexOf);
            proto.SetHostedProperty("lastIndexOf", TypedArrayLastIndexOf);
            proto.SetHostedProperty("includes", TypedArrayIncludes);

            realm.TypedArrayPrototype = proto;
        }

        if (realm.TypedArrayConstructor is null)
        {
            var ctor = new HostFunction((_, _) => throw ThrowTypeError("TypedArray is not a constructor", realm: realm),
                realm)
            {
                IsConstructor = true
            };
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
        var prototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            prototype.SetPrototype(realm.ObjectPrototype);
        }

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
                return Symbols.Undefined;
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
        RealmState realm) where T : TypedArrayBase
    {
        var sharedTypedArrayCtor = EnsureTypedArrayIntrinsic(realm);
        var sharedPrototype = realm.TypedArrayPrototype;
        var prototype = new JsObject();

        var constructor = new HostFunction(TypedArrayCtor);
        constructor.RealmState = realm;

        constructor.SetProperty("BYTES_PER_ELEMENT", (double)bytesPerElement);
        prototype.SetPrototype(realm.ObjectPrototype);
        prototype.SetProperty("constructor", constructor);
        constructor.DefineProperty("of",
            new PropertyDescriptor
            {
                Value = new HostFunction(TypedArrayOf) { IsConstructor = false },
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        constructor.DefineProperty("from",
            new PropertyDescriptor
            {
                Value = new HostFunction(TypedArrayFrom) { IsConstructor = false },
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        prototype.SetHostedProperty("reduce",
            (thisValue, reduceArgs, realmState) =>
                StandardLibrary.ReduceLike(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduce", false),
            realm);
        prototype.SetHostedProperty("reduceRight",
            (thisValue, reduceArgs, realmState) =>
                StandardLibrary.ReduceLike(thisValue, reduceArgs, realmState, "%TypedArray%.prototype.reduceRight",
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

        object? TypedArrayCtor(object? _, IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                var ta = fromLength(0, realm);
                ta.SetPrototype(prototype);
                return ta;
            }

            var firstArg = args[0];

            // TypedArray(length)
            if (firstArg is double d)
            {
                var ta = fromLength((int)d, realm);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(array)
            if (firstArg is JsArray array)
            {
                var ta = fromArray(array, realm);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(buffer, byteOffset, length)
            if (firstArg is JsArrayBuffer buffer)
            {
                var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;

                var lengthProvided = args.Count > 2 && args[2] is double;
                var length = lengthProvided ? (int)(double)args[2]! : (buffer.ByteLength - byteOffset) / bytesPerElement;
                var isLengthTracking = buffer.Resizable && !lengthProvided;

                var ta = fromBuffer(buffer, byteOffset, length, isLengthTracking, realm);
                ta.SetPrototype(prototype);
                return ta;
            }

            var fallback = fromLength(0, realm);
            fallback.SetPrototype(prototype);
            return fallback;
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
            IJsCallable? mapFn = null;
            object? mapThis = Symbols.Undefined;

            if (args.Count == 0)
            {
                return CreateTarget(0);
            }

            if (args.Count > 1 && !ReferenceEquals(args[1], Symbols.Undefined))
            {
                if (args[1] is not IJsCallable callableMap)
                {
                    throw new ThrowSignal(WrapTypeError("mapfn is not callable", callingEnv));
                }

                mapFn = callableMap;
                mapThis = args.Count > 2 ? args[2] : Symbols.Undefined;
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
                !ReferenceEquals(methodVal, Symbols.Undefined))
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
                        : Symbols.Undefined;
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
                    target.SetValue(i, ApplyMap(i, hasElement ? element : Symbols.Undefined));
                }

                return target;
            }

            return CreateTarget(0);

            IJsCallable? ResolveTypeErrorCtor(JsEnvironment? env)
            {
                if (env is not null &&
                    env.TryGet(Symbol.Intern("TypeError"), out var typeErrorVal) &&
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
                target.SetPrototype(prototype);
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
            realm);
    }

    private static object? ArrayBufferIsView(object? _, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        return args[0] is TypedArrayBase || args[0] is JsDataView;
    }

    private static object? TypedArrayIndexOf(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.indexOf called on incompatible receiver");
        }

        return TypedArrayBase.IndexOfInternal(typed, args);
    }

    private static object? TypedArrayLastIndexOf(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.lastIndexOf called on incompatible receiver");
        }

        return TypedArrayBase.LastIndexOfInternal(typed, args);
    }

    private static object? TypedArrayIncludes(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.includes called on incompatible receiver");
        }

        return TypedArrayBase.IncludesInternal(typed, args);
    }
}
