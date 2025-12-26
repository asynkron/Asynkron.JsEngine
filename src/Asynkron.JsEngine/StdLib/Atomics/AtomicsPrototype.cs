#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

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
    public JsValue Add(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.add
        // Atomically adds a value to an element and returns the old value
        throw new NotImplementedException("Atomics.add is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("and", Length = 3d)]
    public JsValue And(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.and
        // Atomically computes bitwise AND and returns the old value
        throw new NotImplementedException("Atomics.and is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("compareExchange", Length = 4d)]
    public JsValue CompareExchange(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.compareExchange
        // Atomically replaces a value if it matches an expected value
        throw new NotImplementedException("Atomics.compareExchange is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("exchange", Length = 3d)]
    public JsValue Exchange(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.exchange
        // Atomically replaces a value and returns the old value
        throw new NotImplementedException("Atomics.exchange is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("isLockFree", Length = 1d)]
    public JsValue IsLockFree(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.isLockFree
        // Returns true if atomic operations on the given size are lock-free
        throw new NotImplementedException("Atomics.isLockFree is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("load", Length = 2d)]
    public JsValue Load(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.load
        // Atomically loads a value from an array
        throw new NotImplementedException("Atomics.load is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("or", Length = 3d)]
    public JsValue Or(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.or
        // Atomically computes bitwise OR and returns the old value
        throw new NotImplementedException("Atomics.or is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("store", Length = 3d)]
    public JsValue Store(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.store
        // Atomically stores a value in an array
        throw new NotImplementedException("Atomics.store is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("sub", Length = 3d)]
    public JsValue Sub(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.sub
        // Atomically subtracts a value and returns the old value
        throw new NotImplementedException("Atomics.sub is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("wait", Length = 4d)]
    public JsValue Wait(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.wait
        // Waits until notified or times out
        throw new NotImplementedException("Atomics.wait is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("waitAsync", Length = 4d)]
    public JsValue WaitAsync(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.waitAsync
        // Asynchronously waits until notified or times out
        throw new NotImplementedException("Atomics.waitAsync is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("notify", Length = 3d)]
    public JsValue Notify(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.notify
        // Wakes up waiting agents
        throw new NotImplementedException("Atomics.notify is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("xor", Length = 3d)]
    public JsValue Xor(IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Atomics.xor
        // Atomically computes bitwise XOR and returns the old value
        throw new NotImplementedException("Atomics.xor is not yet implemented");
    }
}
