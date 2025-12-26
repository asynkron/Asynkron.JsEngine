#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncGenerator prototype for async generator functions
/// </summary>
[JsPrototype("AsyncGenerator", ToStringTag = "AsyncGenerator")]
public sealed partial class AsyncGeneratorPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("next", Length = 1d)]
    public JsValue Next(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncGenerator.prototype.next
        // Returns a promise for the next yielded value
        throw new NotImplementedException("AsyncGenerator.prototype.next is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("return", Length = 1d)]
    public JsValue Return(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncGenerator.prototype.return
        // Finishes the async generator and returns a value
        throw new NotImplementedException("AsyncGenerator.prototype.return is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("throw", Length = 1d)]
    public JsValue Throw(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncGenerator.prototype.throw
        // Throws an error into the async generator
        throw new NotImplementedException("AsyncGenerator.prototype.throw is not yet implemented");
    }
}
