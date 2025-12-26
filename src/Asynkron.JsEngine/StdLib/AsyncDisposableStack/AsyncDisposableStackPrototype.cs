#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("AsyncDisposableStack", ToStringTag = "AsyncDisposableStack")]
public sealed partial class AsyncDisposableStackPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("use", Length = 1d)]
    public JsValue Use(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncDisposableStack.prototype.use
        // Adds a disposable resource to the async stack
        throw new NotImplementedException("AsyncDisposableStack.prototype.use is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("adopt", Length = 2d)]
    public JsValue Adopt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncDisposableStack.prototype.adopt
        // Adopts a value and an async disposal callback
        throw new NotImplementedException("AsyncDisposableStack.prototype.adopt is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("defer", Length = 1d)]
    public JsValue Defer(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncDisposableStack.prototype.defer
        // Defers execution of an async callback until disposal
        throw new NotImplementedException("AsyncDisposableStack.prototype.defer is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("move", Length = 0d)]
    public JsValue Move(JsValue thisValue)
    {
        // TODO: Implement AsyncDisposableStack.prototype.move
        // Moves the stack to a new AsyncDisposableStack
        throw new NotImplementedException("AsyncDisposableStack.prototype.move is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("disposeAsync", Length = 0d)]
    public JsValue DisposeAsync(JsValue thisValue)
    {
        // TODO: Implement AsyncDisposableStack.prototype.disposeAsync
        // Asynchronously disposes all resources in the stack
        throw new NotImplementedException("AsyncDisposableStack.prototype.disposeAsync is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("disposed")]
    public JsValue Disposed(JsValue thisValue)
    {
        // TODO: Implement AsyncDisposableStack.prototype.disposed getter
        // Returns true if the stack has been disposed
        throw new NotImplementedException("AsyncDisposableStack.prototype.disposed is not yet implemented");
    }
}
