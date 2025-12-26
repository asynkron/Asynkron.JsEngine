namespace Asynkron.JsParser;

public readonly record struct DecodedString(
    string? Value,
    bool HasLegacyOctal,
    bool HasInvalidEscape = false,
    bool HasLegacyNonOctalEscape = false)
{
    /// <summary>
    /// Returns true if the cooked value is undefined (null) due to invalid escape sequences.
    /// Per ES2018 Tagged Template Literal Revision, invalid escape sequences make the cooked value undefined.
    /// </summary>
    public bool IsUndefined => HasInvalidEscape;
}
