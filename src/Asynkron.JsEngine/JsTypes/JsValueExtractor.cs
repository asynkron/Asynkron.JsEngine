#region

using System;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Helper class to extract underlying values from JsValue for storage in collections.
/// </summary>
internal static class JsValueExtractor
{
    /// <summary>
    /// Extracts the underlying object from a JsValue for HashSet/Dictionary storage.
    /// For primitives, returns a boxed value. For objects, returns the object reference.
    /// </summary>
    public static object Extract(JsValue jsValue)
    {
        return jsValue.Kind switch
        {
            JsValueKind.Boolean => jsValue.NumberValue != 0, // Box boolean
            JsValueKind.Number => jsValue.NumberValue, // Box number
            JsValueKind.String => jsValue.ObjectValue ?? string.Empty,
            JsValueKind.Symbol => jsValue.ObjectValue ?? throw new InvalidOperationException("Symbol value cannot be null"),
            JsValueKind.BigInt => jsValue.ObjectValue ?? throw new InvalidOperationException("BigInt value cannot be null"),
            JsValueKind.Object => jsValue.ObjectValue ?? throw new InvalidOperationException("Object value cannot be null"),
            _ => throw new InvalidOperationException($"Unexpected value kind: {jsValue.Kind}")
        };
    }
}
