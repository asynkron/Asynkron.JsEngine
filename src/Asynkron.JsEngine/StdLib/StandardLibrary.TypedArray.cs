using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateArrayBufferConstructor()
    {
        var constructor = new HostFunction((thisValue, args) =>
        {
            var length = args.Count > 0 ? args[0] : 0d;
            var byteLength = length switch
            {
                double d => (int)d,
                int i => i,
                _ => 0
            };

            int? maxByteLength = null;
            if (args.Count > 1 && args[1] is JsObject opts)
            {
                if (opts.TryGetProperty("maxByteLength", out var maxVal) && maxVal is double maxD)
                {
                    maxByteLength = (int)maxD;
                }
            }

            return new JsArrayBuffer(byteLength, maxByteLength);
        });

        constructor.SetProperty("isView", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            return args[0] is TypedArrayBase || args[0] is JsDataView;
        }));

        return constructor;
    }

    /// <summary>
    /// Creates the DataView constructor.
    /// </summary>
    public static HostFunction CreateDataViewConstructor()
    {
        return new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is not JsArrayBuffer buffer)
            {
                throw new InvalidOperationException("DataView requires an ArrayBuffer");
            }

            var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            int? byteLength = args.Count > 2 && args[2] is double d2 ? (int)d2 : null;

            return new JsDataView(buffer, byteOffset, byteLength);
        });
    }

    /// <summary>
    /// Creates a typed array constructor.
    /// </summary>
    private static HostFunction CreateTypedArrayConstructor<T>(
        Func<int, T> fromLength,
        Func<JsArray, T> fromArray,
        Func<JsArrayBuffer, int, int, T> fromBuffer,
        int bytesPerElement) where T : TypedArrayBase
    {
        var prototype = new JsObject();
        var constructor = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0)
            {
                var ta = fromLength(0);
                ta.SetPrototype(prototype);
                return ta;
            }

            var firstArg = args[0];

            // TypedArray(length)
            if (firstArg is double d)
            {
                var ta = fromLength((int)d);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(array)
            if (firstArg is JsArray array)
            {
                var ta = fromArray(array);
                ta.SetPrototype(prototype);
                return ta;
            }

            // TypedArray(buffer, byteOffset, length)
            if (firstArg is JsArrayBuffer buffer)
            {
                var byteOffset = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;

                int length;
                if (args.Count > 2 && args[2] is double d2)
                {
                    length = (int)d2;
                }
                else
                {
                    // Calculate length from remaining buffer
                    var remainingBytes = buffer.ByteLength - byteOffset;
                    length = remainingBytes / bytesPerElement;
                }

                var ta = fromBuffer(buffer, byteOffset, length);
                ta.SetPrototype(prototype);
                return ta;
            }

            var fallback = fromLength(0);
            fallback.SetPrototype(prototype);
            return fallback;
        });

        constructor.SetProperty("BYTES_PER_ELEMENT", (double)bytesPerElement);
        prototype.SetPrototype(ObjectPrototype);
        prototype.SetProperty("constructor", constructor);
        constructor.DefineProperty("of", new PropertyDescriptor
        {
            Value = new HostFunction((thisValue, args) =>
            {
                if (thisValue is not HostFunction ctor)
                {
                    throw ThrowTypeError("%TypedArray%.of called on incompatible receiver");
                }

                var length = args.Count;
                // Invoke the constructor with the desired length.
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
            }) { IsConstructor = false },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        constructor.DefineProperty("from", new PropertyDescriptor
        {
            Value = new HostFunction((thisValue, args) =>
            {
                IJsCallable? ResolveTypeErrorCtor(JsEnvironment? env)
                {
                    if (env is not null &&
                        env.TryGet(Symbol.Intern("TypeError"), out var typeErrorVal) &&
                        typeErrorVal is IJsCallable typeErrorFromEnv)
                    {
                        return typeErrorFromEnv;
                    }

                    return TypeErrorConstructor;
                }

                object WrapTypeError(string message, JsEnvironment? env)
                {
                    var typeErrorCtor = ResolveTypeErrorCtor(env);
                    if (typeErrorCtor is not null)
                    {
                        var errorValue = typeErrorCtor.Invoke([message], null);
                        if (errorValue is JsObject errorObj)
                        {
                            errorObj.SetProperty("constructor", typeErrorCtor);
                        }

                        return errorValue ?? new InvalidOperationException(message);
                    }

                    return new InvalidOperationException(message);
                }

                var callingEnv = (thisValue as HostFunction)?.CallingJsEnvironment;
                IJsCallable? mapFn = null;
                object? mapThis = Symbols.Undefined;

                TypedArrayBase CreateTarget(int length)
                {
                    var target = fromLength(length);
                    target.SetPrototype(prototype);
                    return target;
                }

                object? ApplyMap(int index, object? value)
                {
                    return mapFn is null ? value : mapFn.Invoke([value, (double)index], mapThis);
                }

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
                if (source is JsArray jsArray)
                {
                    var target = CreateTarget(jsArray.Items.Count);
                    for (var i = 0; i < jsArray.Items.Count; i++)
                    {
                        target.SetValue(i, ApplyMap(i, jsArray.Items[i]));
                    }

                    return target;
                }

                if (source is TypedArrayBase typedSource)
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
                        var key = i.ToString();
                        var hasElement = arrayLike.TryGetProperty(key, out var element);
                        target.SetValue(i, ApplyMap(i, hasElement ? element : Symbols.Undefined));
                    }

                    return target;
                }

                return CreateTarget(0);
            }) { IsConstructor = false },
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        prototype.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not TypedArrayBase typed)
            {
                throw ThrowTypeError("TypedArray.prototype.indexOf called on incompatible receiver");
            }

            return TypedArrayBase.IndexOfInternal(typed, args);
        }));
        constructor.SetProperty("prototype", prototype);

        return constructor;
    }

    public static HostFunction CreateInt8ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt8Array.FromLength,
            JsInt8Array.FromArray,
            (buffer, offset, length) => new JsInt8Array(buffer, offset, length),
            JsInt8Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint8ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint8Array.FromLength,
            JsUint8Array.FromArray,
            (buffer, offset, length) => new JsUint8Array(buffer, offset, length),
            JsUint8Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint8ClampedArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint8ClampedArray.FromLength,
            JsUint8ClampedArray.FromArray,
            (buffer, offset, length) => new JsUint8ClampedArray(buffer, offset, length),
            JsUint8ClampedArray.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateInt16ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt16Array.FromLength,
            JsInt16Array.FromArray,
            (buffer, offset, length) => new JsInt16Array(buffer, offset, length),
            JsInt16Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint16ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint16Array.FromLength,
            JsUint16Array.FromArray,
            (buffer, offset, length) => new JsUint16Array(buffer, offset, length),
            JsUint16Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateInt32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsInt32Array.FromLength,
            JsInt32Array.FromArray,
            (buffer, offset, length) => new JsInt32Array(buffer, offset, length),
            JsInt32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateUint32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsUint32Array.FromLength,
            JsUint32Array.FromArray,
            (buffer, offset, length) => new JsUint32Array(buffer, offset, length),
            JsUint32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateFloat32ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsFloat32Array.FromLength,
            JsFloat32Array.FromArray,
            (buffer, offset, length) => new JsFloat32Array(buffer, offset, length),
            JsFloat32Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateFloat64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsFloat64Array.FromLength,
            JsFloat64Array.FromArray,
            (buffer, offset, length) => new JsFloat64Array(buffer, offset, length),
            JsFloat64Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateBigInt64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsBigInt64Array.FromLength,
            JsBigInt64Array.FromArray,
            (buffer, offset, length) => new JsBigInt64Array(buffer, offset, length),
            JsBigInt64Array.BYTES_PER_ELEMENT);
    }

    public static HostFunction CreateBigUint64ArrayConstructor()
    {
        return CreateTypedArrayConstructor(
            JsBigUint64Array.FromLength,
            JsBigUint64Array.FromArray,
            (buffer, offset, length) => new JsBigUint64Array(buffer, offset, length),
            JsBigUint64Array.BYTES_PER_ELEMENT);
    }
}
