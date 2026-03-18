#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlOptionHelpers
{
    /// <summary>
    /// JsValue overload that avoids boxing. Per spec, uses ToObject on non-undefined values.
    /// </summary>
    public static IJsPropertyAccessor? GetOptionsObject(JsValue optionsArg, RealmState realm, string typeName)
    {
        if (optionsArg.Kind == JsValueKind.Undefined)
        {
            return null;
        }

        if (optionsArg.Kind == JsValueKind.Null)
        {
            throw StandardLibrary.ThrowTypeError($"Intl.{typeName} options must be an object", realm: realm);
        }

        // Per spec: Let options be ? ToObject(options).
        // ToObject wraps primitives (boolean, number, string, symbol) in their wrapper objects.
        if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        // For primitive types (boolean, number, string, symbol), ToObject wraps them.
        return StandardLibrary.ToObjectPropertyAccessor(optionsArg, $"Intl.{typeName}", realm);
    }

    public static string GetStringOption(
        IJsPropertyAccessor? options,
        string property,
        RealmState realm,
        string typeName,
        IReadOnlyList<string>? allowedValues,
        string? defaultValue,
        bool required = false)
    {
        if (options is null || !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            if (required)
            {
                throw StandardLibrary.ThrowTypeError(
                    $"Intl.{typeName} requires a {property} option", realm: realm);
            }

            return defaultValue ?? string.Empty;
        }

        var stringValue = StandardLibrary.JsValueToString(value, realm);
        if (allowedValues?.Contains(stringValue, StringComparer.Ordinal) == false)
        {
            throw StandardLibrary.ThrowRangeError(
                $"Invalid value '{stringValue}' for option '{property}' on Intl.{typeName}", realm: realm);
        }

        return stringValue;
    }

    public static bool? GetBooleanOption(
        IJsPropertyAccessor? options,
        string property,
        bool? defaultValue = null)
    {
        if (options is null || !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return defaultValue;
        }

        return JsOps.ToBoolean(value);
    }
}
