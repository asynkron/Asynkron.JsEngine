#region

using System;
using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
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
        lock (typedArray.Buffer)
        {
            if (isBigInt)
            {
                var oldValue = ReadBigIntElement(typedArray, index);
                var expected = ToBigInt(expectedArg, realmState: Realm);
                if (JsOps.SameValue(oldValue, expected))
                {
                    var replacement = ToBigInt(replacementArg, realmState: Realm);
                    WriteBigIntElement(typedArray, index, replacement);
                }

                return oldValue;
            }

            var oldNumber = typedArray.GetElement(index);
            var expectedNumber = JsOps.ToNumber(expectedArg);
            if (JsOps.SameValue(oldNumber, expectedNumber))
            {
                var replacementNumber = JsOps.ToNumber(replacementArg);
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
        // Waits until notified or times out. In this runtime, we avoid blocking,
        // so we return a synchronous status string.
        var typedArray = RequireWaitableTypedArray(args.GetArgument(0), nameof(Wait));
        var index = RequireAtomicIndex(typedArray, args.GetArgument(1));

        var expectedArg = args.GetArgument(2);
        var expectedValue = typedArray.IsBigIntArray
            ? (JsValue)ToBigInt(expectedArg, realmState: Realm)
            : (JsValue)JsOps.ToNumber(expectedArg);

        lock (typedArray.Buffer)
        {
            var current = typedArray.GetValueForIndex(index);
            if (!JsOps.SameValue(current, expectedValue))
            {
                return "not-equal";
            }
        }

        // Treat missing or non-positive timeouts as immediate timeouts.
        var timeoutValue = args.GetArgument(3);
        if (!timeoutValue.IsUndefined)
        {
            _ = JsOps.ToNumber(timeoutValue);
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
        // Wakes up waiting agents. This runtime does not track waiters,
        // so we report that no agents were notified.
        var typedArray = RequireWaitableTypedArray(args.GetArgument(0), nameof(Notify));
        _ = RequireAtomicIndex(typedArray, args.GetArgument(1));
        var countArg = args.GetArgument(2);
        if (!countArg.IsUndefined)
        {
            _ = JsOps.ToNumber(countArg);
        }

        return 0;
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
        if (typedArray is JsFloat32Array or JsFloat64Array)
        {
            throw ThrowTypeError("Atomics operations are not supported on floating point typed arrays", realm: Realm);
        }

        if (typedArray.Buffer.IsDetached)
        {
            throw ThrowTypeError("Atomics operations are not allowed on detached buffers", realm: Realm);
        }

        if (!typedArray.Buffer.IsShared)
        {
            throw ThrowTypeError("Atomics operations require a SharedArrayBuffer", realm: Realm);
        }

        isBigInt = typedArray.IsBigIntArray;
        return typedArray;
    }

    private TypedArrayBase RequireWaitableTypedArray(JsValue value, string methodName)
    {
        var typedArray = RequireAtomicTypedArray(value, methodName, out var isBigInt);
        if (typedArray is not JsInt32Array && !(isBigInt && typedArray is JsBigInt64Array))
        {
            throw ThrowTypeError("Atomics.wait/notify require Int32Array or BigInt64Array", realm: Realm);
        }

        return typedArray;
    }

    private int RequireAtomicIndex(TypedArrayBase typedArray, JsValue indexArg)
    {
        // Atomics must validate the index after the typed array type check.
        var index = ToIndex(indexArg, Realm);
        if (index < 0 || index >= typedArray.Length)
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
            var result = intOp((int)oldNumber, (int)maskNumber);
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

    private JsValue CreateWaitAsyncResult(JsValue status)
    {
        var result = new JsObject { RealmState = Realm, ["async"] = false, ["value"] = status };
        return result;
    }
}
