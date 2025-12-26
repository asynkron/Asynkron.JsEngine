#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Generator prototype for generator functions
/// </summary>
[JsPrototype("Generator", ToStringTag = "Generator")]
public sealed partial class GeneratorPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("next", Length = 1d)]
    public JsValue Next(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Generator.prototype.next
        // Returns the next yielded value
        throw new NotImplementedException("Generator.prototype.next is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("return", Length = 1d)]
    public JsValue Return(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Generator.prototype.return
        // Finishes the generator and returns a value
        throw new NotImplementedException("Generator.prototype.return is not yet implemented");
    }

    /* FLAKY */
    [JsHostMethod("throw", Length = 1d)]
    public JsValue Throw(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement Generator.prototype.throw
        // Throws an error into the generator
        throw new NotImplementedException("Generator.prototype.throw is not yet implemented");
    }
}
