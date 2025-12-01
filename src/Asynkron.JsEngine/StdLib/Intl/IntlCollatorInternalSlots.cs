using System.Globalization;

namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class IntlCollatorInternalSlots
{
    public required string Locale { get; init; }
    public required string Usage { get; init; }
    public required string Sensitivity { get; init; }
    public required bool IgnorePunctuation { get; init; }
    public required bool Numeric { get; init; }
    public required string CaseFirst { get; init; }
    public required string Collation { get; init; }
    public string LocaleMatcher { get; init; } = "best fit";
    public CompareInfo CompareInfo { get; init; } = CultureInfo.InvariantCulture.CompareInfo;
}
