using System;

namespace Asynkron.JsEngine;

internal static class PropertyNameExtensions
{
    public static bool IsPrivateName(this string propertyName)
    {
        return !string.IsNullOrEmpty(propertyName) && propertyName[0] == '#';
    }

    public static bool IsPrivateSlotName(this string propertyName)
    {
        return propertyName.IsPrivateName() && propertyName.Contains('@', StringComparison.Ordinal);
    }
}
