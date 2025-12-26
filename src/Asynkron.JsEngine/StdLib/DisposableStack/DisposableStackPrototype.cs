#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("DisposableStack", ToStringTag = "DisposableStack")]
public sealed partial class DisposableStackPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("use", Length = 1d)]
    public JsValue Use(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DisposableStack.prototype.use
        // Adds a disposable resource to the stack
        throw new NotImplementedException("DisposableStack.prototype.use is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("adopt", Length = 2d)]
    public JsValue Adopt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DisposableStack.prototype.adopt
        // Adopts a value and a disposal callback
        throw new NotImplementedException("DisposableStack.prototype.adopt is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("defer", Length = 1d)]
    public JsValue Defer(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DisposableStack.prototype.defer
        // Defers execution of a callback until disposal
        throw new NotImplementedException("DisposableStack.prototype.defer is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("move", Length = 0d)]
    public JsValue Move(JsValue thisValue)
    {
        // TODO: Implement DisposableStack.prototype.move
        // Moves the stack to a new DisposableStack
        throw new NotImplementedException("DisposableStack.prototype.move is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("dispose", Length = 0d)]
    public JsValue Dispose(JsValue thisValue)
    {
        // TODO: Implement DisposableStack.prototype.dispose
        // Disposes all resources in the stack
        throw new NotImplementedException("DisposableStack.prototype.dispose is not yet implemented");
    }

    /* FLAKY */
    [JsHostGetter("disposed")]
    public JsValue Disposed(JsValue thisValue)
    {
        // TODO: Implement DisposableStack.prototype.disposed getter
        // Returns true if the stack has been disposed
        throw new NotImplementedException("DisposableStack.prototype.disposed is not yet implemented");
    }
}
