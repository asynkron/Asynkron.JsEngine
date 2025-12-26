namespace Asynkron.JsParser;

/// <summary>
/// String extensions for parser operations.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Returns true if the string is a private name (starts with #).
    /// </summary>
    public static bool IsPrivateName(this string s)
    {
        return s.Length > 0 && s[0] == '#';
    }
}
