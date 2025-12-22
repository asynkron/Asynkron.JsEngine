using System.Globalization;

namespace Asynkron.JsEngine.StdLib.Intl;

internal sealed class IntlNumberFormatInternalSlots
{
    public required string Locale { get; init; }
    public required string NumberingSystem { get; init; }
    public required string Style { get; init; }
    public string? Currency { get; init; }
    public string CurrencyDisplay { get; init; } = "symbol";
    public string CurrencySign { get; init; } = "standard";
    public string? Unit { get; init; }
    public string UnitDisplay { get; init; } = "short";
    public int MinimumIntegerDigits { get; init; } = 1;
    public int MinimumFractionDigits { get; init; }
    public int MaximumFractionDigits { get; init; } = 3;
    public int? MinimumSignificantDigits { get; init; }
    public int? MaximumSignificantDigits { get; init; }
    public bool UseGrouping { get; init; } = true;
    public string Notation { get; init; } = "standard";
    public string SignDisplay { get; init; } = "auto";
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public bool UseSignificantDigits =>
        MinimumSignificantDigits.HasValue && MaximumSignificantDigits.HasValue;
}
