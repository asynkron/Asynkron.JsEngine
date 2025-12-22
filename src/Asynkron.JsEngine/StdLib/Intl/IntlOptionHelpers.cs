using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlOptionHelpers
{
    /// <summary>
    /// JsValue overload that avoids boxing.
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

        if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        throw StandardLibrary.ThrowTypeError($"Intl.{typeName} options must be an object", realm: realm);
    }

    public static IJsPropertyAccessor? GetOptionsObject(object? optionsArg, RealmState realm, string typeName)
    {
        if (ReferenceEquals(optionsArg, Symbol.Undefined))
        {
            return null;
        }

        if (optionsArg is null || !StandardLibrary.TryGetObject(optionsArg, realm, out var accessor) ||
            accessor is not IJsPropertyAccessor propertyAccessor)
        {
            throw StandardLibrary.ThrowTypeError($"Intl.{typeName} options must be an object", realm: realm);
        }

        return propertyAccessor;
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
            ReferenceEquals(value, Symbol.Undefined))
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
            ReferenceEquals(value, Symbol.Undefined))
        {
            return defaultValue;
        }

        return JsOps.ToBoolean(value);
    }
}
