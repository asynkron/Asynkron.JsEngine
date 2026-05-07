#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib.TypedArray;
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
        _ = fromArray;

        var sharedTypedArrayCtor = EnsureTypedArrayIntrinsic(realm);
        var sharedPrototype = realm.TypedArrayPrototype;
        var prototype = new JsObject();

        HostFunction constructor = null!;
        constructor = new HostFunction((thisValue, args) =>
        {
            _ = thisValue;
            _ = args;

            // Calling without new: throw TypeError per spec step 2
            throw ThrowTypeError(constructorName + " is not a function", realm: realm);
        })
        {
            RealmState = realm
        };
        constructor.SetInvokeWithContext((args, _, context, newTarget) =>
        {
            // Per spec: If NewTarget is undefined, throw a TypeError exception.
            // However, internal calls via ctor.Invoke() (e.g., from TypedArray.from/of)
            // pass undefined newTarget through InvokeWithContext. We distinguish these
            // by checking if there's an active evaluation context.
            if (newTarget.IsUndefined)
            {
                // If called from JS code (call expression), context is non-null.
                // If called from internal ctor.Invoke(), context is null and we use
                // the constructor itself as newTarget.
                if (context is not null)
                {
                    throw ThrowTypeError(constructorName + " is not a function", realm: realm);
                }

                newTarget = (JsValue)constructor;
            }

            return JsValue.FromObjectUnsafe(ConstructTypedArray(args, newTarget));
        });

        // BYTES_PER_ELEMENT: { writable: false, enumerable: false, configurable: false }
        constructor.DefineProperty("BYTES_PER_ELEMENT",
            new PropertyDescriptor
            {
                Value = JsValue.FromNumber((double)bytesPerElement),
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        prototype.SetPrototype(realm.ObjectPrototype);
        // constructor on prototype: { writable: true, enumerable: false, configurable: true }
        prototype.DefineProperty("constructor",
            new PropertyDescriptor
            {
                Value = (JsValue)constructor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        // BYTES_PER_ELEMENT on prototype: { writable: false, enumerable: false, configurable: false }
        prototype.DefineProperty("BYTES_PER_ELEMENT",
            new PropertyDescriptor
            {
                Value = JsValue.FromNumber((double)bytesPerElement),
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        constructor.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = constructorName,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        // length property: value 3, { writable: false, enumerable: false, configurable: true }
        constructor.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = JsValue.FromNumber(3d),
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

        // prototype: { writable: false, enumerable: false, configurable: false }
        constructor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = (JsValue)prototype,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        // Per spec, [[Prototype]] of each TypedArray constructor is %TypedArray%.
        // Use the HostFunction itself (not its PropertiesObject) so that
        // Object.getPrototypeOf(Int8Array) === %TypedArray%.
        constructor.Properties.SetPrototype(sharedTypedArrayCtor);

        return constructor;

        IJsPropertyAccessor ResolvePrototype(JsValue newTarget)
        {
            if (newTarget.TryGetObject<IJsCallable>(out var callableNewTarget) &&
                ResolveConstructPrototype(callableNewTarget, constructor, realm) is IJsPropertyAccessor resolvedProto)
            {
                return resolvedProto;
            }

            return prototype;
        }

        T CreateTargetFromLength(int length, JsValue newTarget)
        {
            // Guard against excessive lengths that would overflow when multiplied by bytesPerElement
            if (length > 0 && (long)length * bytesPerElement > int.MaxValue)
            {
                throw ThrowRangeError("Invalid typed array length", realm: realm);
            }

            T target;
            try
            {
                target = fromLength(length, realm);
            }
            catch (OutOfMemoryException)
            {
                // Per spec: If it is not possible to create the Data Block, throw a RangeError.
                throw ThrowRangeError("Invalid typed array length", realm: realm);
            }

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

            // Primitive Symbol/BigInt arguments must fail via ToIndex before any
            // AllocateTypedArray / prototype-resolution work touches newTarget.
            if (firstArg.TryUnwrap<JsSymbol>(out _) ||
                firstArg.TryUnwrap<JsBigInt>(out _) ||
                firstArg.ObjectValue is JsSymbol or JsBigInt)
            {
                _ = ToIndex(firstArg, realm);
            }

            // Per spec: if Type(firstArg) is Object, handle as typed array / array / buffer / object
            if (firstArg.Kind == JsValueKind.Object)
            {
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
                    // Per spec 23.2.5.1.5 InitializeTypedArrayFromArrayBuffer:
                    // Step 2: Let offset be ? ToIndex(byteOffset).
                    var byteOffsetArg = args.Count > 1 ? args[1] : JsValue.Undefined;
                    var offset = ToIndex(byteOffsetArg, realm);

                    // Step 3: If offset modulo elementSize ≠ 0, throw a RangeError.
                    if (offset % bytesPerElement != 0)
                    {
                        throw ThrowRangeError(
                            $"Start offset of {constructorName} should be a multiple of {bytesPerElement}",
                            realm: realm);
                    }

                    // Step 5: If length is not undefined, let newLength be ? ToIndex(length).
                    var lengthArg = args.Count > 2 ? args[2] : JsValue.Undefined;
                    var lengthProvided = !lengthArg.IsUndefined;
                    int newLength = 0;
                    if (lengthProvided)
                    {
                        newLength = ToIndex(lengthArg, realm);
                    }

                    // Step 6: If IsDetachedBuffer(buffer), throw a TypeError.
                    if (buffer.IsDetached)
                    {
                        throw ThrowTypeError("Cannot construct typed array from detached ArrayBuffer",
                            realm: realm);
                    }

                    var bufferByteLength = buffer.ByteLength;
                    var isLengthTracking = buffer.Resizable && !lengthProvided;

                    int byteLength;
                    if (!lengthProvided && !buffer.Resizable)
                    {
                        // Step 9a: length is undefined, fixed-length buffer
                        if (bufferByteLength % bytesPerElement != 0)
                        {
                            throw ThrowRangeError(
                                "Byte length of ArrayBuffer is not a multiple of element size",
                                realm: realm);
                        }

                        byteLength = bufferByteLength - offset;
                        if (byteLength < 0)
                        {
                            throw ThrowRangeError("Start offset is outside the bounds of the buffer",
                                realm: realm);
                        }
                    }
                    else if (lengthProvided)
                    {
                        // Step 9b: length is defined
                        byteLength = newLength * bytesPerElement;
                        if (offset + byteLength > bufferByteLength)
                        {
                            throw ThrowRangeError("Invalid typed array length", realm: realm);
                        }
                    }
                    else
                    {
                        // Resizable, length tracking
                        if (offset > bufferByteLength)
                        {
                            throw ThrowRangeError("Start offset is outside the bounds of the buffer",
                                realm: realm);
                        }

                        byteLength = bufferByteLength - offset;
                    }

                    var length = byteLength / bytesPerElement;
                    var ta = fromBuffer(buffer, offset, length, isLengthTracking, realm);
                    ta.SetPrototype(ResolvePrototype(newTarget));
                    return ta;
                }

                // TypedArray(object) — per spec 23.2.5.1.4 InitializeTypedArrayFromList / 23.2.5.1.3 InitializeTypedArrayFromArrayLike
                // Step 1: Let usingIterator be ? GetMethod(object, @@iterator).
                if (firstArg.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
                {
                    var iteratorKey = Ast.SymbolKeys.Iterator;
                    // GetMethod: Get the property. If it exists and is not null/undefined, it must be callable.
                    if (objAccessor.TryGetProperty(iteratorKey, out var iterMethodVal) &&
                        !iterMethodVal.IsUndefined && !iterMethodVal.IsNull)
                    {
                        // Step 1a: If usingIterator is not callable, throw TypeError.
                        if (!iterMethodVal.TryGetObject<IJsCallable>(out var callableIterator))
                        {
                            throw ThrowTypeError("@@iterator method is not callable", realm: realm);
                        }

                        // Step 1b: Let values be ? IterableToList(object, usingIterator).
                        var iteratorObj = callableIterator.Invoke([], firstArg);
                        if (!iteratorObj.TryGetObject<IJsPropertyAccessor>(out var iteratorAccessor))
                        {
                            throw ThrowTypeError("Iterator method did not return an object", realm: realm);
                        }

                        if (!iteratorAccessor.TryGetProperty("next", out var nextVal) ||
                            !nextVal.TryGetObject<IJsCallable>(out var nextCallable))
                        {
                            throw ThrowTypeError("Iterator result does not expose next", realm: realm);
                        }

                        var collected = new List<JsValue>();
                        while (true)
                        {
                            var nextResult = nextCallable.Invoke([], iteratorObj);
                            if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var nextResultAccessor))
                            {
                                throw ThrowTypeError("Iterator result is not an object", realm: realm);
                            }

                            var done = nextResultAccessor.TryGetProperty("done", out var doneVal) &&
                                       JsOps.ToBoolean(doneVal);
                            if (done)
                            {
                                break;
                            }

                            var value = nextResultAccessor.TryGetProperty("value", out var valueVal)
                                ? valueVal
                                : JsValue.Undefined;
                            collected.Add(value);
                        }

                        // InitializeTypedArrayFromList: create array of collected length, then set values
                        var ta = CreateTargetFromLength(collected.Count, newTarget);
                        for (var i = 0; i < collected.Count; i++)
                        {
                            ta.SetValue(i, collected[i]);
                        }

                        return ta;
                    }

                    // Step 2: No iterator (undefined/null) — use array-like path.
                    // Let arrayLike be object. Let len be ? ToLength(? Get(arrayLike, "length")).
                    if (!objAccessor.TryGetProperty("length", out var lengthVal))
                    {
                        lengthVal = JsValue.Undefined;
                    }

                    // ToLength: ToIntegerOrInfinity then clamp to [0, 2^53-1]
                    var rawLen = JsOps.ToNumber(lengthVal);
                    double lenNumber;
                    if (double.IsNaN(rawLen) || rawLen <= 0)
                    {
                        lenNumber = 0;
                    }
                    else
                    {
                        lenNumber = Math.Min(Math.Floor(rawLen), 9007199254740991.0); // 2^53-1
                    }

                    var arrLen = (int)Math.Min(lenNumber, int.MaxValue);

                    var taFromArrayLike = CreateTargetFromLength(arrLen, newTarget);
                    for (var i = 0; i < arrLen; i++)
                    {
                        var key = i.ToString(CultureInfo.InvariantCulture);
                        var hasElement = objAccessor.TryGetProperty(key, out var element);
                        taFromArrayLike.SetValue(i, hasElement ? element : JsValue.Undefined);
                    }

                    return taFromArrayLike;
                }

                return CreateTargetFromLength(0, newTarget);
            }

            // Type(firstArg) is not Object: treat as length via ToIndex
            var elementLength = ToIndex(firstArg, realm);
            return CreateTargetFromLength(elementLength, newTarget);
        }

        /// <summary>
        /// Implements ToIndex per ES spec 7.1.22.
        /// </summary>
        static int ToIndex(JsValue value, RealmState realm)
        {
            // 1. If value is undefined, return 0
            if (value.IsUndefined)
            {
                return 0;
            }

            // 2. Let integerIndex be ? ToIntegerOrInfinity(value)
            // ToIntegerOrInfinity: ToNumber then truncate
            var context = realm.CreateContext(pushScope: false);
            var number = JsOps.ToNumber(value, context);
            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }
            double integerIndex;
            if (double.IsNaN(number))
            {
                integerIndex = 0;
            }
            else if (number == 0 || double.IsInfinity(number))
            {
                integerIndex = number;
            }
            else
            {
                integerIndex = Math.Truncate(number);
            }

            // 3. If integerIndex < 0, throw a RangeError exception
            if (integerIndex < 0)
            {
                throw ThrowRangeError("Invalid typed array length", realm: realm);
            }

            // 4. Let index = ToLength(integerIndex) = min(max(integerIndex, 0), 2^53 - 1)
            const double maxSafeInteger = 9007199254740991d; // 2^53 - 1
            var index = Math.Min(integerIndex, maxSafeInteger);

            // 5. If SameValue(integerIndex, index) is false, throw a RangeError
            // This catches Infinity: ToLength(Infinity) = 2^53-1, but SameValue(Infinity, 2^53-1) is false
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (integerIndex != index)
            {
                throw ThrowRangeError("Invalid typed array length", realm: realm);
            }

            return (int)Math.Min(index, int.MaxValue);
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
        var constructor = CreateTypedArrayConstructor(
            JsUint8Array.FromLength,
            JsUint8Array.FromArray,
            (buffer, offset, length, isLengthTracking, _) =>
                new JsUint8Array(buffer, offset, length, isLengthTracking),
            JsUint8Array.BYTES_PER_ELEMENT,
            "Uint8Array",
            realm);

        // Register base64/hex methods (ES2025 uint8array-base64 proposal)
        Uint8ArrayBase64.Register(constructor, realm);

        return constructor;
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
