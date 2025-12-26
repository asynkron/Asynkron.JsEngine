#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// ArrayIterator prototype for array iterators
/// </summary>
[JsPrototype("Array Iterator", ToStringTag = "Array Iterator")]
public sealed partial class ArrayIteratorPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("next", Length = 0d)]
    public JsValue Next(JsValue thisValue)
    {
        // TODO: Implement ArrayIterator.prototype.next
        // Returns the next item in the array iteration
        throw new NotImplementedException("ArrayIterator.prototype.next is not yet implemented");
    }
}
