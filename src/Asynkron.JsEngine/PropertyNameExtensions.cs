namespace Asynkron.JsEngine;

internal static class PropertyNameExtensions
{
    public static bool IsPrivateName(this string propertyName)
    {
        return !string.IsNullOrEmpty(propertyName) && propertyName[0] == '#';
    }
}
