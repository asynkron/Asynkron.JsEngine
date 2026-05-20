#region

using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DurationFormat", ToStringTag = "Intl.DurationFormat")]
public sealed partial class IntlDurationFormatPrototype
{
    private const string BrandKey = "__durationFormat__";
    private const string LocaleSlot = "__locale__";
    private const string NumberingSystemSlot = "__numberingSystem__";
    private const string StyleSlot = "__style__";
    private const string FractionalDigitsSlot = "__fractionalDigits__";

    // Unit names in spec order
    private static readonly string[] UnitNames =
        ["years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds", "nanoseconds"];

    // Singular unit names for NumberFormat
    private static readonly string[] SingularUnitNames =
        ["year", "month", "week", "day", "hour", "minute", "second", "millisecond", "microsecond", "nanosecond"];

    internal static void InitializeInternalSlots(
        JsObject instance, string locale, string numberingSystem, string style,
        string[] unitStyles, string[] unitDisplays, int? fractionalDigits)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(LocaleSlot, locale);
        instance.SetProperty(NumberingSystemSlot, numberingSystem);
        instance.SetProperty(StyleSlot, style);

        for (var i = 0; i < UnitNames.Length; i++)
        {
            instance.SetProperty($"__{UnitNames[i]}Style__", unitStyles[i]);
            instance.SetProperty($"__{UnitNames[i]}Display__", unitDisplays[i]);
        }

        if (fractionalDigits.HasValue)
        {
            instance.SetProperty(FractionalDigitsSlot, (double)fractionalDigits.Value);
        }
        else
        {
            instance.SetProperty(FractionalDigitsSlot, JsValue.Undefined);
        }
    }

    [JsHostMethod("format", Length = 1d)]
    private JsValue Format(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var durationArg = args.GetArgument(0);
        var record = ToDurationRecord(durationArg);
        var opts = ReadResolvedOptionsFromSlots(instance);
        var result = FormatDuration(record, opts);
        return new JsValue(result);
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var durationArg = args.GetArgument(0);
        var record = ToDurationRecord(durationArg);
        var opts = ReadResolvedOptionsFromSlots(instance);
        var parts = FormatDurationToParts(record, opts);
        return BuildPartsArray(parts);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.DurationFormat.prototype.resolvedOptions";

        var locale = GetSlotString(instance, LocaleSlot, "en");
        CreateDataPropertyOrThrow(obj, "locale", locale, Realm, operation);

        var numberingSystem = GetSlotString(instance, NumberingSystemSlot, "latn");
        CreateDataPropertyOrThrow(obj, "numberingSystem", numberingSystem, Realm, operation);

        var style = GetSlotString(instance, StyleSlot, "short");
        CreateDataPropertyOrThrow(obj, "style", style, Realm, operation);

        for (var i = 0; i < UnitNames.Length; i++)
        {
            var unitStyle = GetSlotString(instance, $"__{UnitNames[i]}Style__", "short");
            CreateDataPropertyOrThrow(obj, UnitNames[i], unitStyle, Realm, operation);

            var unitDisplay = GetSlotString(instance, $"__{UnitNames[i]}Display__", "auto");
            CreateDataPropertyOrThrow(obj, UnitNames[i] + "Display", unitDisplay, Realm, operation);
        }

        // fractionalDigits: undefined or integer
        if (instance.TryGetProperty(FractionalDigitsSlot, out var fdValue) && !fdValue.IsUndefined)
        {
            CreateDataPropertyOrThrow(obj, "fractionalDigits", fdValue, Realm, operation);
        }

        return new JsValue(obj);
    }

    #region Duration Record

    private readonly record struct DurationRecord(
        double Years, double Months, double Weeks, double Days,
        double Hours, double Minutes, double Seconds,
        double Milliseconds, double Microseconds, double Nanoseconds);

    private DurationRecord ToDurationRecord(JsValue durationArg)
    {
        // Step 1: If Type(duration) is not Object, throw a TypeError
        if (durationArg.IsUndefined || durationArg.Kind == JsValueKind.Null ||
            durationArg.Kind == JsValueKind.Boolean || durationArg.Kind == JsValueKind.Symbol)
        {
            throw ThrowTypeError("Duration must be an object", realm: Realm);
        }

        // Handle string input - try Temporal.Duration.from parsing
        if (durationArg.TryGetString(out var durationStr))
        {
            return ParseDurationString(durationStr);
        }

        // Numbers and BigInts are not valid
        if (durationArg.Kind == JsValueKind.Number || durationArg.Kind == JsValueKind.BigInt)
        {
            throw ThrowTypeError("Duration must be an object", realm: Realm);
        }

        if (!durationArg.TryGetObject<IJsPropertyAccessor>(out var durationObj))
        {
            throw ThrowTypeError("Duration must be an object", realm: Realm);
        }

        if (durationObj.TryGetProperty("[[TemporalDuration]]", out var temporalDurationSlot) &&
            temporalDurationSlot.TryGetObject<JsTemporalDuration>(out var temporalDuration))
        {
            return new DurationRecord(
                temporalDuration.Years,
                temporalDuration.Months,
                temporalDuration.Weeks,
                temporalDuration.Days,
                temporalDuration.Hours,
                temporalDuration.Minutes,
                temporalDuration.Seconds,
                temporalDuration.Milliseconds,
                temporalDuration.Microseconds,
                temporalDuration.Nanoseconds);
        }

        // Read all unit properties
        var years = ReadDurationProperty(durationObj, "years");
        var months = ReadDurationProperty(durationObj, "months");
        var weeks = ReadDurationProperty(durationObj, "weeks");
        var days = ReadDurationProperty(durationObj, "days");
        var hours = ReadDurationProperty(durationObj, "hours");
        var minutes = ReadDurationProperty(durationObj, "minutes");
        var seconds = ReadDurationProperty(durationObj, "seconds");
        var milliseconds = ReadDurationProperty(durationObj, "milliseconds");
        var microseconds = ReadDurationProperty(durationObj, "microseconds");
        var nanoseconds = ReadDurationProperty(durationObj, "nanoseconds");

        // Check that at least one property is defined (not all undefined)
        if (double.IsNaN(years) && double.IsNaN(months) && double.IsNaN(weeks) && double.IsNaN(days) &&
            double.IsNaN(hours) && double.IsNaN(minutes) && double.IsNaN(seconds) &&
            double.IsNaN(milliseconds) && double.IsNaN(microseconds) && double.IsNaN(nanoseconds))
        {
            throw ThrowTypeError("Duration object must have at least one temporal property", realm: Realm);
        }

        // Replace NaN (undefined) with 0
        years = double.IsNaN(years) ? 0 : years;
        months = double.IsNaN(months) ? 0 : months;
        weeks = double.IsNaN(weeks) ? 0 : weeks;
        days = double.IsNaN(days) ? 0 : days;
        hours = double.IsNaN(hours) ? 0 : hours;
        minutes = double.IsNaN(minutes) ? 0 : minutes;
        seconds = double.IsNaN(seconds) ? 0 : seconds;
        milliseconds = double.IsNaN(milliseconds) ? 0 : milliseconds;
        microseconds = double.IsNaN(microseconds) ? 0 : microseconds;
        nanoseconds = double.IsNaN(nanoseconds) ? 0 : nanoseconds;

        // Validate: IsValidDurationRecord
        ValidateDurationRecord(years, months, weeks, days, hours, minutes, seconds,
            milliseconds, microseconds, nanoseconds);

        return new DurationRecord(years, months, weeks, days, hours, minutes, seconds,
            milliseconds, microseconds, nanoseconds);
    }

    private double ReadDurationProperty(IJsPropertyAccessor obj, string property)
    {
        if (!obj.TryGetProperty(property, out var value) || value.IsUndefined)
        {
            return double.NaN; // sentinel for "undefined"
        }

        var num = JsOps.ToNumber(value);
        if (!double.IsFinite(num))
        {
            throw ThrowRangeError($"Duration property '{property}' must be finite", realm: Realm);
        }

        if (num != Math.Truncate(num))
        {
            throw ThrowRangeError($"Duration property '{property}' must be an integer", realm: Realm);
        }

        return num;
    }

    private void ValidateDurationRecord(
        double years, double months, double weeks, double days,
        double hours, double minutes, double seconds,
        double milliseconds, double microseconds, double nanoseconds)
    {
        // Check sign consistency: all non-zero values must have the same sign
        var hasPositive = false;
        var hasNegative = false;
        foreach (var v in (ReadOnlySpan<double>)[years, months, weeks, days, hours, minutes, seconds,
                     milliseconds, microseconds, nanoseconds])
        {
            if (v > 0)
            {
                hasPositive = true;
            }

            if (v < 0)
            {
                hasNegative = true;
            }
        }

        if (hasPositive && hasNegative)
        {
            throw ThrowRangeError("Duration must not have mixed positive and negative values", realm: Realm);
        }

        // abs(years/months/weeks) must be < 2^32
        const double limit = 4294967296d; // 2^32
        if (Math.Abs(years) >= limit)
        {
            throw ThrowRangeError("Duration years value out of range", realm: Realm);
        }

        if (Math.Abs(months) >= limit)
        {
            throw ThrowRangeError("Duration months value out of range", realm: Realm);
        }

        if (Math.Abs(weeks) >= limit)
        {
            throw ThrowRangeError("Duration weeks value out of range", realm: Realm);
        }

        if (double.IsNaN(days) || double.IsInfinity(days) ||
            double.IsNaN(hours) || double.IsInfinity(hours) ||
            double.IsNaN(minutes) || double.IsInfinity(minutes) ||
            double.IsNaN(seconds) || double.IsInfinity(seconds) ||
            double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) ||
            double.IsNaN(microseconds) || double.IsInfinity(microseconds) ||
            double.IsNaN(nanoseconds) || double.IsInfinity(nanoseconds))
        {
            throw ThrowRangeError("Duration time value out of range", realm: Realm);
        }

        var normalizedNanoseconds =
            (BigInteger)days * 86_400 * 1_000_000_000 +
            (BigInteger)hours * 3_600 * 1_000_000_000 +
            (BigInteger)minutes * 60 * 1_000_000_000 +
            (BigInteger)seconds * 1_000_000_000 +
            (BigInteger)milliseconds * 1_000_000 +
            (BigInteger)microseconds * 1_000 +
            (BigInteger)nanoseconds;

        var maxTimeDuration = BigInteger.Pow(2, 53) * 1_000_000_000;

        if (BigInteger.Abs(normalizedNanoseconds) >= maxTimeDuration)
        {
            throw ThrowRangeError("Duration time value out of range", realm: Realm);
        }
    }

    private DurationRecord ParseDurationString(string input)
    {
        // Simple ISO 8601 duration parsing: PnYnMnWnDTnHnMnS
        // This is a simplified parser for the Test262 test cases
        if (string.IsNullOrEmpty(input) || input[0] != 'P')
        {
            throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
        }

        double years = 0, months = 0, weeks = 0, days = 0;
        double hours = 0, minutes = 0, seconds = 0;
        double milliseconds = 0, microseconds = 0, nanoseconds = 0;
        var inTimePart = false;
        var i = 1;
        var hasAnyUnit = false;

        while (i < input.Length)
        {
            if (input[i] == 'T')
            {
                inTimePart = true;
                i++;
                continue;
            }

            // Parse number (possibly with fractional part)
            var start = i;
            var negative = false;
            if (i < input.Length && input[i] == '-')
            {
                negative = true;
                i++;
            }

            while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
            {
                i++;
            }

            if (i >= input.Length || i == start || (i == start + 1 && negative))
            {
                throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
            }

            var numStr = input[start..i];
            if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
            }

            var unit = input[i];
            i++;
            hasAnyUnit = true;

            if (!inTimePart)
            {
                switch (unit)
                {
                    case 'Y': years = num; break;
                    case 'M': months = num; break;
                    case 'W': weeks = num; break;
                    case 'D': days = num; break;
                    default:
                        throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
                }
            }
            else
            {
                switch (unit)
                {
                    case 'H': hours = num; break;
                    case 'M': minutes = num; break;
                    case 'S':
                        ParseSecondsComponent(numStr, negative, out var wholeSeconds, out var msPart, out var usPart,
                            out var nsPart);
                        seconds = wholeSeconds;
                        milliseconds = msPart;
                        microseconds = usPart;
                        nanoseconds = nsPart;
                        break;
                    default:
                        throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
                }
            }
        }

        if (!hasAnyUnit)
        {
            throw ThrowRangeError($"Invalid duration string: '{input}'", realm: Realm);
        }

        ValidateDurationRecord(years, months, weeks, days, hours, minutes, seconds,
            milliseconds, microseconds, nanoseconds);

        return new DurationRecord(years, months, weeks, days, hours, minutes, seconds,
            milliseconds, microseconds, nanoseconds);
    }

    private static void ParseSecondsComponent(string input, bool negative, out double seconds, out double ms, out double us,
        out double ns)
    {
        seconds = 0;
        ms = 0;
        us = 0;
        ns = 0;

        var normalized = negative && input.StartsWith("-", StringComparison.Ordinal) ? input[1..] : input;
        var split = normalized.Split('.', 2, StringSplitOptions.None);
        if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var secValue))
        {
            return;
        }

        seconds = negative ? -secValue : secValue;

        if (split.Length == 1)
        {
            return;
        }

        var fraction = split[1];
        var nsDigits = (fraction + "000000000")[..9];
        var msStr = nsDigits[..3];
        var usStr = nsDigits[3..6];
        var nsStr = nsDigits[6..9];

        if (double.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var msParsed))
        {
            ms = negative ? -msParsed : msParsed;
        }
        if (double.TryParse(usStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var usParsed))
        {
            us = negative ? -usParsed : usParsed;
        }
        if (double.TryParse(nsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nsParsed))
        {
            ns = negative ? -nsParsed : nsParsed;
        }
    }

    #endregion

    #region Format Implementation

    private sealed class ResolvedDurationOptions
    {
        public string Locale = "en";
        public string NumberingSystem = "latn";
        public string Style = "short";
        public string[] UnitStyles = new string[10];
        public string[] UnitDisplays = new string[10];
        public int? FractionalDigits;
    }

    private ResolvedDurationOptions ReadResolvedOptionsFromSlots(JsObject instance)
    {
        var opts = new ResolvedDurationOptions
        {
            Locale = GetSlotString(instance, LocaleSlot, "en"),
            NumberingSystem = GetSlotString(instance, NumberingSystemSlot, "latn"),
            Style = GetSlotString(instance, StyleSlot, "short"),
        };

        for (var i = 0; i < UnitNames.Length; i++)
        {
            opts.UnitStyles[i] = GetSlotString(instance, $"__{UnitNames[i]}Style__", "short");
            opts.UnitDisplays[i] = GetSlotString(instance, $"__{UnitNames[i]}Display__", "auto");
        }

        if (instance.TryGetProperty(FractionalDigitsSlot, out var fdVal) && fdVal.TryGetDouble(out var fdNum))
        {
            opts.FractionalDigits = (int)fdNum;
        }

        return opts;
    }

    private string FormatDuration(DurationRecord record, ResolvedDurationOptions opts)
    {
        var parts = PartitionDurationFormatPattern(record, opts);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Value);
        }
        return sb.ToString();
    }

    private List<DurationPart> FormatDurationToParts(DurationRecord record, ResolvedDurationOptions opts)
    {
        return PartitionDurationFormatPattern(record, opts);
    }

    private readonly record struct DurationPart(string Type, string Value, string? Unit = null);

    private List<DurationPart> PartitionDurationFormatPattern(DurationRecord duration, ResolvedDurationOptions opts)
    {
        var locale = string.IsNullOrWhiteSpace(opts.Locale) ? "en" : opts.Locale;
        const string timeSeparator = ":";

        var values = new[]
        {
            duration.Years, duration.Months, duration.Weeks, duration.Days,
            duration.Hours, duration.Minutes, duration.Seconds,
            duration.Milliseconds, duration.Microseconds, duration.Nanoseconds
        };

        var result = new List<List<DurationPart>>();
        var needSeparator = false;
        var displayNegativeSign = true;
        var previousDisplayedStyle = (string?)null;

        for (var i = 0; i < UnitNames.Length; i++)
        {
            var unit = UnitNames[i];
            var singularUnit = SingularUnitNames[i];
            var value = values[i];
            var style = opts.UnitStyles[i];
            var display = opts.UnitDisplays[i];

            // Numeric seconds/milliseconds/microseconds combined with sub-second units (fractional)
            var done = false;
            var maxFrac = opts.FractionalDigits ?? 9;
            var minFrac = opts.FractionalDigits ?? 0;
            string? exactFractionalValue = null;

            if (unit == "seconds" && opts.Style == "digital" &&
                (duration.Milliseconds != 0 || duration.Microseconds != 0 || duration.Nanoseconds != 0))
            {
                exactFractionalValue = DurationToFractionalDecimalString(duration, 9);
                done = true;
            }
            else if (unit is "seconds" or "milliseconds" or "microseconds")
            {
                // Check if next unit has "numeric" style
                var nextIdx = i + 1;
                if (nextIdx < UnitNames.Length && opts.UnitStyles[nextIdx] == "numeric")
                {
                    exactFractionalValue = unit switch
                    {
                        "seconds" => DurationToFractionalDecimalString(duration, 9),
                        "milliseconds" => DurationToFractionalDecimalString(duration, 6),
                        _ => DurationToFractionalDecimalString(duration, 3) // microseconds
                    };
                    done = true;
                }
            }

            // Display zero numeric minutes only when the chain is actually clock-like.
            // Digital style needs the minute slot even when the leading hour is omitted,
            // but numeric/short mixed styles should not force an extra zero minute.
            var displayRequired = false;
            if (unit == "minutes")
            {
                var lowerUnitsDisplayable = opts.UnitDisplays[6] == "always" || // secondsDisplay
                                            duration.Seconds != 0 ||
                                            duration.Milliseconds != 0 ||
                                            duration.Microseconds != 0 ||
                                            duration.Nanoseconds != 0;

                displayRequired = opts.Style == "digital"
                    ? lowerUnitsDisplayable
                    : needSeparator && lowerUnitsDisplayable;
            }

            var remainingHasDisplayableUnit = false;
            for (var lookahead = i + 1; lookahead < UnitNames.Length; lookahead++)
            {
                if (values[lookahead] != 0 || opts.UnitDisplays[lookahead] != "auto")
                {
                    remainingHasDisplayableUnit = true;
                    break;
                }
            }

            if (style is not "numeric" and not "2-digit" &&
                previousDisplayedStyle is not null &&
                previousDisplayedStyle != style &&
                remainingHasDisplayableUnit)
            {
                displayRequired = true;
            }

            // Treat -0 as 0 for display/auto comparison but preserve sign for formatting
            var valueIsZero = exactFractionalValue is null
                ? value == 0 // -0 == 0 is true in C#
                : IsDecimalStringZero(exactFractionalValue);

            if (!valueIsZero || display != "auto" || displayRequired)
            {
                var signDisplayNever = false;

                if (displayNegativeSign)
                {
                    displayNegativeSign = false;

                    // If this is the first displayed unit and value is 0, but duration is negative,
                    // display as -0
                    if (valueIsZero)
                    {
                        var hasNegativeValue = values.Any(v => v < 0);
                        var hasNonZeroValue = values.Any(v => v != 0);
                        value = hasNegativeValue && hasNonZeroValue ? NegativeZero() : 0.0;
                    }
                }
                else
                {
                    signDisplayNever = true;
                }

                List<DurationPart> list;
                if (!needSeparator)
                {
                    list = [];
                }
                else
                {
                    list = result[^1];
                    list.Add(new DurationPart("literal", timeSeparator));
                }

                // Format the value
                if (done)
                {
                    if (style is not "numeric" and not "2-digit")
                    {
                        // Non-numeric fractional (e.g., "short" milliseconds with numeric microseconds):
                        // use FormatUnitParts to include the unit label (e.g. "ms").
                        var unitParts = exactFractionalValue is null
                            ? FormatUnitParts(value, singularUnit, style, locale, signDisplayNever, true, maxFrac, minFrac)
                            : FormatUnitParts(exactFractionalValue, singularUnit, style, locale, signDisplayNever, maxFrac, minFrac);
                        list.AddRange(unitParts);
                    }
                    else
                    {
                        // Numeric/2-digit fractional: format using IntlNumberFormatter to match
                        // the same code path as Intl.NumberFormat (which the test harness uses).
                        var isTwoDigit = style == "2-digit";
                        var numSlots = new IntlNumberFormatInternalSlots
                        {
                            Locale = locale,
                            NumberingSystem = "latn",
                            Style = "decimal",
                            MinimumIntegerDigits = isTwoDigit ? 2 : 1,
                            MinimumFractionDigits = minFrac,
                            MaximumFractionDigits = maxFrac,
                            UseGrouping = "false",
                            Notation = "standard",
                            SignDisplay = signDisplayNever ? "never" : "auto",
                            RoundingIncrement = 1,
                            RoundingMode = "trunc",
                            RoundingPriority = "auto",
                            TrailingZeroDisplay = "auto",
                            RoundingType = "fractionDigits",
                            Culture = IntlUtilities.ResolveCulture(locale)
                        };
                        var fmtResult = exactFractionalValue is null
                            ? IntlNumberFormatter.FormatDouble(value, numSlots)
                            : IntlNumberFormatter.TryFormatDecimalString(exactFractionalValue, numSlots)
                              ?? IntlNumberFormatter.FormatDouble(value, numSlots);
                        var formatted = fmtResult.Formatted;
                        var numParts = ParseNumericParts(formatted, singularUnit);
                        list.AddRange(numParts);
                    }
                }
                else if (style is not "numeric" and not "2-digit")
                {
                    // Non-numeric: preserve Intl.NumberFormat-provided parts instead of reparsing the string
                    var unitParts = FormatUnitParts(value, singularUnit, style, locale, signDisplayNever, done, maxFrac, minFrac);
                    list.AddRange(unitParts);
                }
                else
                {
                    // Numeric/2-digit: plain number with no grouping
                    var formatted = FormatPlainNumeric(value, signDisplayNever, style == "2-digit");
                    var numParts = ParseNumericParts(formatted, singularUnit);
                    list.AddRange(numParts);
                }

                if (!needSeparator)
                {
                    if (style is "2-digit" or "numeric")
                    {
                        needSeparator = true;
                    }

                    result.Add(list);
                    previousDisplayedStyle = style;
                }
            }

            if (done)
            {
                break;
            }
        }

        // Join with ListFormat
        var listStyle = opts.Style == "digital" ? "short" : opts.Style;
        return JoinWithListFormat(result, listStyle, locale);
    }

    private static double NegativeZero()
    {
        return -0.0;
    }

    private static string DurationToFractionalDecimalString(DurationRecord duration, int exponent)
    {
        var nanoseconds = ToBigInteger(duration.Nanoseconds);
        switch (exponent)
        {
            case 9:
                nanoseconds += ToBigInteger(duration.Seconds) * 1_000_000_000;
                goto case 6;
            case 6:
                nanoseconds += ToBigInteger(duration.Milliseconds) * 1_000_000;
                goto case 3;
            case 3:
                nanoseconds += ToBigInteger(duration.Microseconds) * 1_000;
                break;
        }

        var isNegative = nanoseconds.Sign < 0;
        var absoluteNanoseconds = BigInteger.Abs(nanoseconds);
        var divisor = BigInteger.Pow(10, exponent);
        var quotient = BigInteger.DivRem(absoluteNanoseconds, divisor, out var remainder);
        var sign = isNegative ? "-" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{quotient}.{remainder.ToString(CultureInfo.InvariantCulture).PadLeft(exponent, '0')}");
    }

    private static BigInteger ToBigInteger(double value)
    {
        return new BigInteger(value);
    }

    private static bool IsDecimalStringZero(string value)
    {
        foreach (var ch in value)
        {
            if (ch is >= '1' and <= '9')
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Number Formatting

    private static List<DurationPart> FormatUnitParts(double value, string unit, string unitDisplay,
        string locale, bool signDisplayNever, bool fractional, int maxFrac, int minFrac)
    {
        var slots = new IntlNumberFormatInternalSlots
        {
            Locale = locale,
            NumberingSystem = "latn",
            Style = "unit",
            Unit = unit,
            UnitDisplay = unitDisplay,
            MinimumIntegerDigits = 1,
            MinimumFractionDigits = fractional ? minFrac : 0,
            MaximumFractionDigits = fractional ? maxFrac : 0,
            UseGrouping = "auto",
            Notation = "standard",
            SignDisplay = signDisplayNever ? "never" : "auto",
            RoundingIncrement = 1,
            RoundingMode = "trunc",
            RoundingPriority = "auto",
            TrailingZeroDisplay = "auto",
            RoundingType = "fractionDigits",
            Culture = IntlUtilities.ResolveCulture(locale)
        };

        var result = IntlNumberFormatter.FormatDouble(value, slots);
        return ConvertUnitFormatParts(result, unit);
    }

    private static List<DurationPart> FormatUnitParts(string decimalValue, string unit, string unitDisplay,
        string locale, bool signDisplayNever, int maxFrac, int minFrac)
    {
        var slots = new IntlNumberFormatInternalSlots
        {
            Locale = locale,
            NumberingSystem = "latn",
            Style = "unit",
            Unit = unit,
            UnitDisplay = unitDisplay,
            MinimumIntegerDigits = 1,
            MinimumFractionDigits = minFrac,
            MaximumFractionDigits = maxFrac,
            UseGrouping = "auto",
            Notation = "standard",
            SignDisplay = signDisplayNever ? "never" : "auto",
            RoundingIncrement = 1,
            RoundingMode = "trunc",
            RoundingPriority = "auto",
            TrailingZeroDisplay = "auto",
            RoundingType = "fractionDigits",
            Culture = IntlUtilities.ResolveCulture(locale)
        };

        var result = IntlNumberFormatter.TryFormatDecimalString(decimalValue, slots);
        return result is null
            ? [new DurationPart("literal", decimalValue)]
            : ConvertUnitFormatParts(result, unit);
    }

    private static List<DurationPart> ConvertUnitFormatParts(IntlNumberFormatResult result, string unit)
    {
        var formatterParts = result.Parts;
        if (formatterParts is null || formatterParts.Count == 0)
        {
            return [new DurationPart("literal", result.Formatted)];
        }

        var parts = new List<DurationPart>(formatterParts.Count);
        foreach (var part in formatterParts)
        {
            if (part.Type == "literal")
            {
                parts.Add(new DurationPart("literal", part.Value, unit));
            }
            else if (part.Type == "unit")
            {
                parts.Add(new DurationPart("unit", part.Value, unit));
            }
            else
            {
                parts.Add(new DurationPart(part.Type, part.Value, unit));
            }
        }

        return parts;
    }

    private static string FormatIntegerForUnit(double value)
    {
        // Integer formatting without grouping for unit-style NumberFormat
        var absValue = Math.Abs(value);
        var intValue = decimal.Truncate((decimal)absValue);
        var result = intValue.ToString(CultureInfo.InvariantCulture);
        if (value < 0 || double.IsNegative(value))
        {
            result = "-" + result;
        }
        return result;
    }

    private static string FormatFractionalNumber(double value, int maxFrac, int minFrac)
    {
        // Format with truncation rounding
        var absValue = Math.Abs(value);
        var absDecimal = (decimal)absValue;
        var intPart = decimal.Truncate(absDecimal);

        var result = new StringBuilder();
        if (value < 0 || double.IsNegative(value))
        {
            result.Append('-');
        }
        result.Append(intPart.ToString(CultureInfo.InvariantCulture));

        if (maxFrac > 0)
        {
            // Get fractional digits by truncation
            var fracStr = GetTruncatedFractionDigits(absDecimal, maxFrac);
            // Trim trailing zeros down to minFrac
            var trimmed = fracStr.TrimEnd('0');
            if (trimmed.Length < minFrac)
            {
                trimmed = trimmed.PadRight(minFrac, '0');
            }
            if (trimmed.Length > 0)
            {
                result.Append('.');
                result.Append(trimmed);
            }
            else if (minFrac > 0)
            {
                result.Append('.');
                result.Append(new string('0', minFrac));
            }
        }

        return result.ToString();
    }

    private static string GetTruncatedFractionDigits(decimal absValue, int digits)
    {
        var multiplier = 1m;
        for (var i = 0; i < digits; i++)
        {
            multiplier *= 10m;
        }

        var truncated = decimal.Truncate(absValue * multiplier);
        var intPart = decimal.Truncate(absValue);
        var fracInt = truncated - intPart * multiplier;
        return fracInt.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static string FormatPlainNumeric(double value, bool signDisplayNever, bool twoDigit)
    {
        // Plain numeric format with no grouping
        var absValue = Math.Abs(value);
        var intValue = decimal.Truncate((decimal)absValue);
        var result = twoDigit
            ? intValue.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0')
            : intValue.ToString(CultureInfo.InvariantCulture);

        if (!signDisplayNever && (value < 0 || double.IsNegative(value)))
        {
            result = "-" + result;
        }
        return result;
    }

    /// <summary>
    /// Get unit display suffix for en locale.
    /// Replicates: Intl.NumberFormat("en", {style:"unit", unit, unitDisplay}).format(value)
    /// but returns only the suffix part (space + unit name).
    /// </summary>
    private static string GetUnitDisplayString(double absValue, string unit, string displayStyle, string locale)
    {
        var isOne = absValue == 1;
        var lang = ExtractLanguageTag(locale);

        return displayStyle switch
        {
            "long" => lang switch
            {
                "es" => GetLongUnitDisplayEs(unit, isOne),
                _ => GetLongUnitDisplay(unit, isOne),
            },
            "short" => lang switch
            {
                "es" => GetShortUnitDisplayEs(unit, isOne),
                _ => GetShortUnitDisplay(unit, isOne),
            },
            "narrow" => lang switch
            {
                "es" => GetNarrowUnitDisplayEs(unit, isOne),
                _ => GetNarrowUnitDisplay(unit, isOne),
            },
            _ => lang switch
            {
                "es" => GetShortUnitDisplayEs(unit, isOne),
                _ => GetShortUnitDisplay(unit, isOne),
            },
        };
    }

    private static string ExtractLanguageTag(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        var idx = locale.IndexOfAny(new[] { '-', '_' });
        return idx < 0 ? locale : locale[..idx];
    }

    private static string GetNumericUnitDisplayStyle(string style)
    {
        return style switch
        {
            "long" => "long",
            "narrow" => "narrow",
            _ => "short"
        };
    }

    private static string GetLongUnitDisplayEs(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", true) => " año",
        ("year", false) => " años",
        ("month", true) => " mes",
        ("month", false) => " meses",
        ("week", true) => " semana",
        ("week", false) => " semanas",
        ("day", true) => " día",
        ("day", false) => " días",
        ("hour", true) => " hora",
        ("hour", false) => " horas",
        ("minute", true) => " minuto",
        ("minute", false) => " minutos",
        ("second", true) => " segundo",
        ("second", false) => " segundos",
        ("millisecond", true) => " milisegundo",
        ("millisecond", false) => " milisegundos",
        ("microsecond", true) => " microsegundo",
        ("microsecond", false) => " microsegundos",
        ("nanosecond", true) => " nanosegundo",
        ("nanosecond", false) => " nanosegundos",
        _ => $" {unit}s"
    };

    private static string GetShortUnitDisplayEs(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", _) => " a",
        ("month", _) => " mes",
        ("week", _) => " sem",
        ("day", _) => " día",
        ("hour", _) => " h",
        ("minute", _) => " min",
        ("second", _) => " s",
        ("millisecond", _) => " ms",
        ("microsecond", _) => " μs",
        ("nanosecond", _) => " ns",
        _ => $" {unit}"
    };

    private static string GetNarrowUnitDisplayEs(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", _) => "a",
        ("month", _) => "m",
        ("week", _) => "s",
        ("day", _) => "d",
        ("hour", _) => "h",
        ("minute", _) => "m",
        ("second", _) => "s",
        ("millisecond", _) => "ms",
        ("microsecond", _) => "μs",
        ("nanosecond", _) => "ns",
        _ => unit[..1]
    };

    private static string GetLongUnitDisplay(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", true) => " year",
        ("year", false) => " years",
        ("month", true) => " month",
        ("month", false) => " months",
        ("week", true) => " week",
        ("week", false) => " weeks",
        ("day", true) => " day",
        ("day", false) => " days",
        ("hour", true) => " hour",
        ("hour", false) => " hours",
        ("minute", true) => " minute",
        ("minute", false) => " minutes",
        ("second", true) => " second",
        ("second", false) => " seconds",
        ("millisecond", true) => " millisecond",
        ("millisecond", false) => " milliseconds",
        ("microsecond", true) => " microsecond",
        ("microsecond", false) => " microseconds",
        ("nanosecond", true) => " nanosecond",
        ("nanosecond", false) => " nanoseconds",
        _ => $" {unit}s"
    };

    private static string GetShortUnitDisplay(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", true) => " yr",
        ("year", false) => " yrs",
        ("month", true) => " mth",
        ("month", false) => " mths",
        ("week", true) => " wk",
        ("week", false) => " wks",
        ("day", true) => " day",
        ("day", false) => " days",
        ("hour", true) => " hr",
        ("hour", false) => " hr",
        ("minute", true) => " min",
        ("minute", false) => " min",
        ("second", true) => " sec",
        ("second", false) => " sec",
        ("millisecond", _) => " ms",
        ("microsecond", _) => " μs",
        ("nanosecond", _) => " ns",
        _ => $" {unit}"
    };

    private static string GetNarrowUnitDisplay(string unit, bool isOne) => (unit, isOne) switch
    {
        ("year", _) => "y",
        ("month", _) => "m",
        ("week", _) => "w",
        ("day", _) => "d",
        ("hour", _) => "h",
        ("minute", _) => "m",
        ("second", _) => "s",
        ("millisecond", _) => "ms",
        ("microsecond", _) => "μs",
        ("nanosecond", _) => "ns",
        _ => unit[..1]
    };

    private static List<DurationPart> ParseUnitFormatParts(string formatted, string unit)
    {
        // For unit formatting, the whole string is one part with the unit
        // In practice, for formatToParts we need to split into number parts and unit literal
        var parts = new List<DurationPart>();

        // Find where the unit suffix starts (after the number)
        var numEnd = 0;
        for (var i = 0; i < formatted.Length; i++)
        {
            if (char.IsDigit(formatted[i]) || formatted[i] == '-' || formatted[i] == ',' ||
                formatted[i] == '.' || formatted[i] == '\u00a0') // NBSP
            {
                numEnd = i + 1;
            }
            else if (i > 0 && formatted[i] == ' ' && numEnd == i)
            {
                // Space between number and unit - this is part of the unit literal
                break;
            }
            else if (numEnd > 0)
            {
                break;
            }
        }

        if (numEnd > 0)
        {
            // Split the number part into integer/group/decimal/fraction/minus parts
            var numStr = formatted[..numEnd];
            AddNumberPartsForUnit(parts, numStr, unit);

            // The rest is the unit literal
            if (numEnd < formatted.Length)
            {
                parts.Add(new DurationPart("unit", formatted[numEnd..], unit));
            }
        }
        else
        {
            parts.Add(new DurationPart("literal", formatted));
        }

        return parts;
    }

    private static void AddNumberPartsForUnit(List<DurationPart> parts, string numStr, string unit)
    {
        // Parse a formatted number string into parts
        for (var i = 0; i < numStr.Length; i++)
        {
            var c = numStr[i];
            if (c == '-')
            {
                parts.Add(new DurationPart("minusSign", "-", unit));
            }
            else if (c == ',')
            {
                parts.Add(new DurationPart("group", ",", unit));
            }
            else if (c == '.')
            {
                parts.Add(new DurationPart("decimal", ".", unit));
            }
            else if (char.IsDigit(c))
            {
                // Collect consecutive digits
                var start = i;
                while (i + 1 < numStr.Length && char.IsDigit(numStr[i + 1]))
                {
                    i++;
                }

                // Determine if these are integer or fraction digits
                var digits = numStr[start..(i + 1)];
                var isAfterDecimal = numStr[..start].Contains('.');
                parts.Add(new DurationPart(isAfterDecimal ? "fraction" : "integer", digits, unit));
            }
        }
    }

    private static List<DurationPart> ParseNumericParts(string formatted, string unit)
    {
        var parts = new List<DurationPart>();
        AddNumberPartsForUnit(parts, formatted, unit);
        return parts;
    }

    #endregion

    #region ListFormat Integration

    private static List<DurationPart> JoinWithListFormat(List<List<DurationPart>> groups,
        string listStyle, string locale)
    {
        if (groups.Count == 0)
        {
            return [];
        }

        // Convert groups to strings for list formatting
        var strings = new List<string>();
        foreach (var group in groups)
        {
            var sb = new StringBuilder();
            foreach (var part in group)
            {
                sb.Append(part.Value);
            }
            strings.Add(sb.ToString());
        }

        if (strings.Count == 1)
        {
            return [.. groups[0]];
        }

        var listParts = IntlListFormatPrototype.FormatListToParts(strings, "unit", listStyle, locale);
        var result = new List<DurationPart>();
        var elementIndex = 0;
        foreach (var (type, value) in listParts)
        {
            if (type == "element")
            {
                result.AddRange(groups[elementIndex++]);
            }
            else if (!string.IsNullOrEmpty(value))
            {
                result.Add(new DurationPart("literal", value));
            }
        }

        return result;
    }

    #endregion

    #region Helpers

    private JsValue BuildPartsArray(List<DurationPart> parts)
    {
        var array = new JsArray(Realm);

        foreach (var part in parts)
        {
            var partObj = new JsObject(Realm.ObjectPrototype);
            partObj.SetProperty("type", (JsValue)part.Type);
            partObj.SetProperty("value", (JsValue)part.Value);
            if (part.Unit is not null)
            {
                partObj.SetProperty("unit", (JsValue)part.Unit);
            }
            array.Push(new JsValue(partObj));
        }

        return JsValue.FromJsArray(array);
    }

    private static string GetSlotString(JsObject instance, string slot, string defaultValue)
    {
        return instance.TryGetProperty(slot, out var value) && value.TryGetString(out var str)
            ? str
            : defaultValue;
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }

    #endregion
}
