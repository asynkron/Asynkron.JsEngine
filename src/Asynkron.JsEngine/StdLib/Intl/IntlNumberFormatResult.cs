namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class IntlNumberFormatResult
{
    public string Formatted { get; set; } = string.Empty;
    public List<NumberFormatPart>? Parts { get; set; }

    public static IntlNumberFormatResult FromParts(string value, List<NumberFormatPart> parts)
    {
        return new IntlNumberFormatResult { Formatted = value, Parts = parts };
    }
}
