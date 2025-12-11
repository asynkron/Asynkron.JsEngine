using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakRef", ToStringTag = "WeakRef")]
public sealed partial class WeakRefPrototype : JsPrototype
{
    [JsHostMethod("deref", Length = 0d)]
    public object? Deref(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is JsObject obj && obj.TryGetProperty("_target", out var stored))
        {
            return stored;
        }

        return Symbol.Undefined;
    }
}
