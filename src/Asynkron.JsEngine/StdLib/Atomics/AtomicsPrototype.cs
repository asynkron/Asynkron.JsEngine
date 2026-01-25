#region

using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Atomics object provides atomic operations on SharedArrayBuffer and ArrayBuffer
/// </summary>
[JsPrototype("Atomics", ToStringTag = "Atomics", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class AtomicsPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("add", Length = 3d)]
    public JsValue Add(IReadOnlyList<JsValue> args) =>
        AtomicArithmeticOperation(args, nameof(Add), (a, b) => a + b, (a, b) => a + b);

    /* FLAKY */
    [JsHostMethod("and", Length = 3d)]
    public JsValue And(IReadOnlyList<JsValue> args) =>
        AtomicBitwiseOperation(args, nameof(And), (a, b) => a & b, (a, b) => a & b);

    /* FLAKY */
    [JsHostMethod("compareExchange", Length = 4d)]
    public JsValue CompareExchange(IReadOnlyList<JsValue> args)
    {
        // Atomically replaces a value if it matches an expected value.
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), nameof(CompareExchange), out var isBigInt);
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var expectedArg = args.GetArgument(2);
        var replacementArg = args.GetArgument(3);
        if (isBigInt)
        {
            var expected = CoerceAtomicBigInt(typedArray, ToBigInt(expectedArg, realmState: Realm));
            var replacement = CoerceAtomicBigInt(typedArray, ToBigInt(replacementArg, realmState: Realm));

            lock (typedArray.Buffer)
            {
                var oldValue = ReadBigIntElement(typedArray, index);
                if (JsOps.SameValue(oldValue, expected))
                {
                    WriteBigIntElement(typedArray, index, replacement);
                }

                return oldValue;
            }
        }

        var expectedNumber = CoerceAtomicNumber(typedArray, JsOps.ToNumber(expectedArg));
        var replacementNumber = CoerceAtomicNumber(typedArray, JsOps.ToNumber(replacementArg));

        lock (typedArray.Buffer)
        {
            var oldNumber = typedArray.GetElement(index);
            if (JsOps.SameValue(oldNumber, expectedNumber))
            {
                typedArray.SetElement(index, replacementNumber);
            }

            return oldNumber;
        }
    }

    /* FLAKY */
    [JsHostMethod("exchange", Length = 3d)]
    public JsValue Exchange(IReadOnlyList<JsValue> args) =>
        AtomicArithmeticOperation(args, nameof(Exchange), (_, b) => b, (_, b) => b);

    /* FLAKY */
    [JsHostMethod("isLockFree", Length = 1d)]
    public JsValue IsLockFree(IReadOnlyList<JsValue> args)
    {
        // Returns true if atomic operations on the given size are lock-free.
        var size = JsOps.ToNumber(args.GetArgument(0));
        if (double.IsNaN(size) || double.IsInfinity(size))
        {
            return false;
        }

        var sizeInt = (int)size;
        return sizeInt is 1 or 2 or 4 or 8;
    }

    /* FLAKY */
    [JsHostMethod("load", Length = 2d)]
    public JsValue Load(IReadOnlyList<JsValue> args)
    {
        // Atomically loads a value from an array.
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), nameof(Load), out _);
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        lock (typedArray.Buffer)
        {
            return typedArray.GetValueForIndex(index);
        }
    }

    /* FLAKY */
    [JsHostMethod("or", Length = 3d)]
    public JsValue Or(IReadOnlyList<JsValue> args) =>
        AtomicBitwiseOperation(args, nameof(Or), (a, b) => a | b, (a, b) => a | b);

    /* FLAKY */
    [JsHostMethod("store", Length = 3d)]
    public JsValue Store(IReadOnlyList<JsValue> args)
    {
        // Atomically stores a value in an array.
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), nameof(Store), out var isBigInt);
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var valueArg = args.GetArgument(2);
        lock (typedArray.Buffer)
        {
            if (isBigInt)
            {
                var value = ToBigInt(valueArg, realmState: Realm);
                WriteBigIntElement(typedArray, index, value);
            }
            else
            {
                typedArray.SetElement(index, JsOps.ToNumber(valueArg));
            }

            // Return the value as stored in the typed array.
            return typedArray.GetValueForIndex(index);
        }
    }

    /* FLAKY */
    [JsHostMethod("sub", Length = 3d)]
    public JsValue Sub(IReadOnlyList<JsValue> args) =>
        AtomicArithmeticOperation(args, nameof(Sub), (a, b) => a - b, (a, b) => a - b);

    /* FLAKY */
    [JsHostMethod("wait", Length = 4d)]
    public JsValue Wait(IReadOnlyList<JsValue> args)
    {
        // Waits until notified or times out.
        var typedArray = RequireWaitableTypedArray(args.GetArgument(0), nameof(Wait));
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var expectedArg = args.GetArgument(2);
        var expectedValue = typedArray.IsBigIntArray
            ? (JsValue)ToBigInt(expectedArg, realmState: Realm)
            : (JsValue)JsOps.ToNumber(expectedArg);

        var timeoutArg = args.GetArgument(3);
        var timeout = double.PositiveInfinity;
        if (!timeoutArg.IsUndefined)
        {
            timeout = JsOps.ToNumber(timeoutArg);
            if (double.IsNaN(timeout))
            {
                timeout = double.PositiveInfinity;
            }
            else if (timeout < 0)
            {
                timeout = 0;
            }
        }

        var buffer = typedArray.Buffer;
        var byteOffset = typedArray.ByteOffset + index * typedArray.BytesPerElement;
        AtomicsWaiterManager.Waiter? waiter = null;
        lock (buffer)
        {
            var current = typedArray.GetValueForIndex(index);
            if (!JsOps.SameValue(current, expectedValue))
            {
                return "not-equal";
            }

            if (timeout == 0)
            {
                return "timed-out";
            }

            waiter = AtomicsWaiterManager.EnqueueWaiter(buffer, byteOffset);
        }

        using var waiterLifetime = waiter;

        var notified = double.IsInfinity(timeout)
            ? waiterLifetime!.Event.Wait(Timeout.InfiniteTimeSpan)
            : waiterLifetime!.Event.Wait(TimeSpan.FromMilliseconds(timeout));

        if (notified)
        {
            return "ok";
        }

        lock (buffer)
        {
            if (waiterLifetime!.IsNotified)
            {
                return "ok";
            }

            AtomicsWaiterManager.DequeueWaiter(buffer, byteOffset, waiterLifetime);
        }

        return "timed-out";
    }

    /* FLAKY */
    [JsHostMethod("waitAsync", Length = 4d)]
    public JsValue WaitAsync(IReadOnlyList<JsValue> args)
    {
        // Asynchronously waits until notified or times out.
        // We only return a synchronous result object in this runtime.
        var typedArray = RequireWaitableTypedArray(args.GetArgument(0), nameof(WaitAsync));
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var expectedArg = args.GetArgument(2);
        var expectedValue = typedArray.IsBigIntArray
            ? (JsValue)ToBigInt(expectedArg, realmState: Realm)
            : (JsValue)JsOps.ToNumber(expectedArg);

        var timeoutValue = args.GetArgument(3);
        if (!timeoutValue.IsUndefined)
        {
            _ = JsOps.ToNumber(timeoutValue);
        }

        JsValue status;
        lock (typedArray.Buffer)
        {
            var current = typedArray.GetValueForIndex(index);
            status = JsOps.SameValue(current, expectedValue) ? "timed-out" : "not-equal";
        }

        return CreateWaitAsyncResult(status);
    }

    /* FLAKY */
    [JsHostMethod("notify", Length = 3d)]
    public JsValue Notify(IReadOnlyList<JsValue> args)
    {
        // Atomics.notify evaluates index/count before checking for SharedArrayBuffer.
        // If TA.buffer is not a SharedArrayBuffer, it returns 0.
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), nameof(Notify), out var isBigInt);
        if (typedArray is not JsInt32Array && !(isBigInt && typedArray is JsBigInt64Array))
        {
            throw ThrowTypeError("Atomics.wait/notify require Int32Array or BigInt64Array", realm: Realm);
        }

        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var countArg = args.GetArgument(2);
        var count = double.PositiveInfinity;
        if (!countArg.IsUndefined)
        {
            count = JsOps.ToNumber(countArg);
            if (double.IsNaN(count))
            {
                count = 0;
            }
        }

        count = Math.Truncate(count);
        if (count < 0)
        {
            count = 0;
        }

        if (!typedArray.Buffer.IsShared)
        {
            return 0;
        }

        var byteOffset = typedArray.ByteOffset + index * typedArray.BytesPerElement;
        var max = double.IsInfinity(count) || count > int.MaxValue ? int.MaxValue : (int)count;

        return AtomicsWaiterManager.Notify(typedArray.Buffer, byteOffset, max);
    }

    /* FLAKY */
    [JsHostMethod("xor", Length = 3d)]
    public JsValue Xor(IReadOnlyList<JsValue> args) =>
        AtomicBitwiseOperation(args, nameof(Xor), (a, b) => a ^ b, (a, b) => a ^ b);

    private TypedArrayBase RequireAtomicTypedArray(JsValue value, string methodName, out bool isBigInt)
    {
        if (!value.TryGetObject<TypedArrayBase>(out var typedArray))
        {
            throw ThrowTypeError($"Atomics.{methodName} requires a TypedArray", realm: Realm);
        }

        // Atomics only allow integer typed arrays (including BigInt variants).
        // We validate the view type before any index/value coercion.
        switch (typedArray)
        {
            case JsInt8Array:
            case JsUint8Array:
            case JsInt16Array:
            case JsUint16Array:
            case JsInt32Array:
            case JsUint32Array:
                isBigInt = false;
                break;
            case JsBigInt64Array:
            case JsBigUint64Array:
                isBigInt = true;
                break;
            case JsFloat32Array:
            case JsFloat64Array:
                throw ThrowTypeError("Atomics operations are not supported on floating point typed arrays", realm: Realm);
            case JsUint8ClampedArray:
                throw ThrowTypeError("Atomics operations are not supported on clamped typed arrays", realm: Realm);
            default:
                throw ThrowTypeError("Atomics operations are only supported on integer typed arrays", realm: Realm);
        }

        if (typedArray.Buffer.IsDetached)
        {
            throw ThrowTypeError("Atomics operations are not allowed on detached buffers", realm: Realm);
        }

        // Note: Atomics operations work on both SharedArrayBuffer and ArrayBuffer.
        // Only wait/waitAsync/notify require SharedArrayBuffer.

        return typedArray;
    }

    private TypedArrayBase RequireWaitableTypedArray(JsValue value, string methodName)
    {
        var typedArray = RequireAtomicTypedArray(value, methodName, out var isBigInt);
        if (typedArray is not JsInt32Array && !(isBigInt && typedArray is JsBigInt64Array))
        {
            throw ThrowTypeError("Atomics.wait/notify require Int32Array or BigInt64Array", realm: Realm);
        }

        // wait/waitAsync/notify require SharedArrayBuffer
        if (!typedArray.Buffer.IsShared)
        {
            throw ThrowTypeError("Atomics.wait/notify require a SharedArrayBuffer", realm: Realm);
        }

        return typedArray;
    }

    private int RequireAtomicIndex(TypedArrayBase typedArray, JsValue indexArg)
    {
        // Atomics must validate the index after the typed array type check.
        // Length must be retrieved before index coercion (ToIndex) because the coercion
        // can resize/detach the underlying buffer for non-shared views (Test262).
        var length = typedArray.Length;
        var index = ToIndex(indexArg, Realm);
        if (index < 0 || index >= length)
        {
            throw ThrowRangeError("Atomics index out of range", realm: Realm);
        }

        return index;
    }

    /// <summary>
    /// Helper for atomic bitwise operations (And, Or, Xor) that share the same pattern.
    /// </summary>
    private JsValue AtomicBitwiseOperation(
        IReadOnlyList<JsValue> args,
        string methodName,
        Func<JsBigInt, JsBigInt, JsBigInt> bigIntOp,
        Func<int, int, int> intOp)
    {
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), methodName, out var isBigInt);
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));
        var valueArg = args.GetArgument(2);

        lock (typedArray.Buffer)
        {
            if (isBigInt)
            {
                var oldValue = ReadBigIntElement(typedArray, index);
                var mask = ToBigInt(valueArg, realmState: Realm);
                WriteBigIntElement(typedArray, index, bigIntOp(oldValue, mask));
                return oldValue;
            }

            var oldNumber = typedArray.GetElement(index);
            var maskNumber = JsOps.ToNumber(valueArg);
            var result = intOp(JsNumericConversions.ToInt32(oldNumber), JsNumericConversions.ToInt32(maskNumber));
            typedArray.SetElement(index, result);
            return oldNumber;
        }
    }

    /// <summary>
    /// Helper for atomic arithmetic operations (Add, Sub, Exchange) that share the same pattern.
    /// </summary>
    private JsValue AtomicArithmeticOperation(
        IReadOnlyList<JsValue> args,
        string methodName,
        Func<JsBigInt, JsBigInt, JsBigInt> bigIntOp,
        Func<double, double, double> numberOp)
    {
        var typedArray = RequireAtomicTypedArray(args.GetArgument(0), methodName, out var isBigInt);
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));
        var valueArg = args.GetArgument(2);

        lock (typedArray.Buffer)
        {
            if (isBigInt)
            {
                var oldValue = ReadBigIntElement(typedArray, index);
                var operand = ToBigInt(valueArg, realmState: Realm);
                WriteBigIntElement(typedArray, index, bigIntOp(oldValue, operand));
                return oldValue;
            }

            var oldNumber = typedArray.GetElement(index);
            var operandNumber = JsOps.ToNumber(valueArg);
            typedArray.SetElement(index, numberOp(oldNumber, operandNumber));
            return oldNumber;
        }
    }

    private static JsBigInt ReadBigIntElement(TypedArrayBase typedArray, int index)
    {
        return typedArray switch
        {
            JsBigInt64Array bi64 => bi64.GetBigIntElement(index),
            JsBigUint64Array bu64 => bu64.GetBigIntElement(index),
            _ => throw new ArgumentException("TypedArray does not store BigInt values.", nameof(typedArray))
        };
    }

    private static void WriteBigIntElement(TypedArrayBase typedArray, int index, JsBigInt value)
    {
        switch (typedArray)
        {
            case JsBigInt64Array bi64:
                bi64.SetElement(index, value);
                return;
            case JsBigUint64Array bu64:
                bu64.SetElement(index, value);
                return;
            default:
                throw new ArgumentException("TypedArray does not store BigInt values.", nameof(typedArray));
        }
    }

    private static JsBigInt CoerceAtomicBigInt(TypedArrayBase typedArray, JsBigInt value)
    {
        return typedArray switch
        {
            JsBigInt64Array => new JsBigInt(ToBigInt64(value.Value)),
            JsBigUint64Array => new JsBigInt(new System.Numerics.BigInteger(ToBigUint64(value.Value))),
            _ => value
        };
    }

    private static double CoerceAtomicNumber(TypedArrayBase typedArray, double value)
    {
        var intValue = double.IsNaN(value) ? 0 : (int)value;
        var longValue = double.IsNaN(value) ? 0 : (long)value;

        return typedArray switch
        {
            JsInt8Array => (sbyte)intValue,
            JsUint8Array => (byte)intValue,
            JsInt16Array => (short)intValue,
            JsUint16Array => (ushort)intValue,
            JsInt32Array => intValue,
            JsUint32Array => (uint)longValue,
            _ => value
        };
    }

    private JsValue CreateWaitAsyncResult(JsValue status)
    {
        var result = new JsObject { RealmState = Realm, ["async"] = false, ["value"] = status };
        return result;
    }
}
