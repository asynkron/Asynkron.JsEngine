#region

using System.Globalization;

#endregion

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
    public string UseGrouping { get; init; } = "auto";
    public string Notation { get; init; } = "standard";
    public string? CompactDisplay { get; init; }
    public string SignDisplay { get; init; } = "auto";
    public int RoundingIncrement { get; init; } = 1;
    public string RoundingMode { get; init; } = "halfExpand";
    public string RoundingPriority { get; init; } = "auto";
    public string TrailingZeroDisplay { get; init; } = "auto";
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>The rounding type derived from digit options resolution.</summary>
    public string RoundingType { get; init; } = "fractionDigits";

    public bool UseSignificantDigits =>
        MinimumSignificantDigits.HasValue && MaximumSignificantDigits.HasValue;

    public bool ShouldUseGrouping =>
        !string.Equals(UseGrouping, "false", StringComparison.Ordinal) &&
        !string.Equals(UseGrouping, "never", StringComparison.Ordinal);
}
