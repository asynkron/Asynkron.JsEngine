#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlOptionHelpers
{
    /// <summary>
    /// JsValue overload that avoids boxing.
    /// </summary>
    public static IJsPropertyAccessor? GetOptionsObject(JsValue optionsArg, RealmState realm, string typeName,
        bool useToObject = false)
    {
        if (optionsArg.Kind == JsValueKind.Undefined)
        {
            return null;
        }

        if (optionsArg.Kind == JsValueKind.Null)
        {
            throw StandardLibrary.ThrowTypeError($"Intl.{typeName} options must be an object", realm: realm);
        }

        if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        // Some constructors (e.g., NumberFormat, DateTimeFormat) use ToObject(options)
        // which wraps primitives. Others (e.g., ListFormat) use GetOptionsObject which
        // throws TypeError for non-objects.
        if (useToObject)
        {
            return StandardLibrary.ToObjectPropertyAccessor(optionsArg, $"Intl.{typeName}", realm);
        }

        // Per spec GetOptionsObject: if Type(options) is not Object, throw TypeError.
        throw StandardLibrary.ThrowTypeError($"Intl.{typeName} options must be an object", realm: realm);
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

    /// <summary>
    /// Spec: GetNumberOption(options, property, minimum, maximum, fallback)
    /// Returns null when fallback is null and value is undefined.
    /// </summary>
    public static int? GetNumberOption(
        IJsPropertyAccessor? options,
        string property,
        int minimum,
        int maximum,
        int? fallback,
        RealmState realm,
        string typeName)
    {
        if (options is null || !options.TryGetProperty(property, out var rawValue) ||
            rawValue.IsUndefined)
        {
            return fallback;
        }

        var value = JsOps.ToNumber(rawValue);
        if (double.IsNaN(value) || value < minimum || value > maximum)
        {
            throw StandardLibrary.ThrowRangeError(
                $"Value {value} out of range for Intl.{typeName} option '{property}' [{minimum}, {maximum}]",
                realm: realm);
        }

        return (int)Math.Floor(value);
    }
}
