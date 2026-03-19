#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DurationFormat", ToStringTag = "Intl.DurationFormat")]
public sealed partial class IntlDurationFormatPrototype
{
    private const string BrandKey = "__durationFormat__";
    private const string SlotsKey = "__durationFormatSlots__";

    private static readonly string[] Units =
        ["years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds", "nanoseconds"];

    internal static void InitializeInternalSlots(
        JsObject instance,
        string locale,
        string numberingSystem,
        string style,
        Dictionary<string, string> unitStyles,
        Dictionary<string, string> unitDisplays,
        int? fractionalDigits)
    {
        instance.SetProperty(BrandKey, true);
        var slots = new DurationFormatSlots
        {
            Locale = locale,
            NumberingSystem = numberingSystem,
            Style = style,
            UnitStyles = unitStyles,
            UnitDisplays = unitDisplays,
            FractionalDigits = fractionalDigits,
        };
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
    }

    [JsHostMethod("format", Length = 1d)]
    private JsValue Format(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var durationArg = args.GetArgument(0);
        var record = ToDurationRecord(durationArg, Realm);
        var slots = GetDurationFormatSlots(instance);
        var parts = PartitionDurationFormatPattern(slots, record);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Value);
        }

        return new JsValue(sb.ToString());
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var durationArg = args.GetArgument(0);
        var record = ToDurationRecord(durationArg, Realm);
        var slots = GetDurationFormatSlots(instance);
        var parts = PartitionDurationFormatPattern(slots, record);

        var result = new JsArray(Realm);
        foreach (var part in parts)
        {
            var entry = new JsObject(Realm.ObjectPrototype);
            entry.SetProperty("type", (JsValue)part.Type);
            entry.SetProperty("value", (JsValue)part.Value);
            if (part.Unit is not null)
            {
                entry.SetProperty("unit", (JsValue)part.Unit);
            }

            result.Push(entry);
        }

        return JsValue.FromJsArray(result);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.DurationFormat.prototype.resolvedOptions";

        var slots = GetDurationFormatSlots(instance);
        CreateDataPropertyOrThrow(obj, "locale", slots.Locale, Realm, operation);
        CreateDataPropertyOrThrow(obj, "numberingSystem", slots.NumberingSystem, Realm, operation);
        CreateDataPropertyOrThrow(obj, "style", slots.Style, Realm, operation);

        foreach (var unit in Units)
        {
            CreateDataPropertyOrThrow(obj, unit, slots.UnitStyles[unit], Realm, operation);
            CreateDataPropertyOrThrow(obj, unit + "Display", slots.UnitDisplays[unit], Realm, operation);
        }

        if (slots.FractionalDigits.HasValue)
        {
            CreateDataPropertyOrThrow(obj, "fractionalDigits", (double)slots.FractionalDigits.Value, Realm,
                operation);
        }

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DurationFormat method called on incompatible receiver");
    }

    #region Internal Slots

    private sealed class DurationFormatSlots
    {
        public required string Locale { get; init; }
        public required string NumberingSystem { get; init; }
        public required string Style { get; init; }
        public required Dictionary<string, string> UnitStyles { get; init; }
        public required Dictionary<string, string> UnitDisplays { get; init; }
        public int? FractionalDigits { get; init; }
    }

    private static DurationFormatSlots GetDurationFormatSlots(JsObject instance)
    {
        if (instance.TryGetProperty(SlotsKey, out var sv) &&
            sv.TryGetObject<DurationFormatSlots>(out var slots))
        {
            return slots;
        }

        throw ThrowTypeError("Intl.DurationFormat internal slots not found");
    }

    #endregion

    #region ToDurationRecord

    private sealed class DurationRecord
    {
        public double Years { get; set; }
        public double Months { get; set; }
        public double Weeks { get; set; }
        public double Days { get; set; }
        public double Hours { get; set; }
        public double Minutes { get; set; }
        public double Seconds { get; set; }
        public double Milliseconds { get; set; }
        public double Microseconds { get; set; }
        public double Nanoseconds { get; set; }
    }

    private static readonly string[] DurationFieldNames =
        ["years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds", "nanoseconds"];

    private static DurationRecord ToDurationRecord(JsValue input, RealmState realm)
    {
        if (input.IsNullOrUndefined)
        {
            throw ThrowTypeError("Duration input must be an object", realm: realm);
        }

        // Type check: must be an object (not bool, number, bigint, symbol)
        if (!input.IsObject)
        {
            // String input: try to parse as ISO 8601 duration
            if (input.TryGetString(out var str))
            {
                return ParseDurationString(str, realm);
            }

            throw ThrowTypeError("Duration input must be an object or string", realm: realm);
        }

        var obj = input.AsObject();

        // Check for Temporal.Duration internal slot
        if (obj.TryGetProperty("[[TemporalDuration]]", out var durationSlot) &&
            durationSlot.TryGetObject<JsTypes.JsTemporalDuration>(out var temporalDuration))
        {
            return new DurationRecord
            {
                Years = temporalDuration.Years,
                Months = temporalDuration.Months,
                Weeks = temporalDuration.Weeks,
                Days = temporalDuration.Days,
                Hours = temporalDuration.Hours,
                Minutes = temporalDuration.Minutes,
                Seconds = temporalDuration.Seconds,
                Milliseconds = temporalDuration.Milliseconds,
                Microseconds = temporalDuration.Microseconds,
                Nanoseconds = temporalDuration.Nanoseconds,
            };
        }

        // Check that at least one duration field is present and not undefined
        var anyDefined = false;
        foreach (var field in DurationFieldNames)
        {
            if (obj.TryGetProperty(field, out var val) && !val.IsUndefined)
            {
                anyDefined = true;
                break;
            }
        }

        if (!anyDefined)
        {
            throw ThrowTypeError("Duration object must have at least one duration property", realm: realm);
        }

        var record = new DurationRecord();
        record.Years = GetDurationField(obj, "years", realm);
        record.Months = GetDurationField(obj, "months", realm);
        record.Weeks = GetDurationField(obj, "weeks", realm);
        record.Days = GetDurationField(obj, "days", realm);
        record.Hours = GetDurationField(obj, "hours", realm);
        record.Minutes = GetDurationField(obj, "minutes", realm);
        record.Seconds = GetDurationField(obj, "seconds", realm);
        record.Milliseconds = GetDurationField(obj, "milliseconds", realm);
        record.Microseconds = GetDurationField(obj, "microseconds", realm);
        record.Nanoseconds = GetDurationField(obj, "nanoseconds", realm);

        if (!IsValidDurationRecord(record))
        {
            throw ThrowRangeError("Duration values are out of range", realm: realm);
        }

        return record;
    }

    private static double GetDurationField(IJsPropertyAccessor obj, string name, RealmState realm)
    {
        if (!obj.TryGetProperty(name, out var val) || val.IsUndefined)
        {
            return 0;
        }

        var num = JsOps.ToNumber(val);
        if (double.IsNaN(num) || double.IsInfinity(num))
        {
            throw ThrowRangeError($"Invalid duration value for {name}: {num}", realm: realm);
        }

        // Per spec: duration properties must be integers
        if (num != Math.Truncate(num))
        {
            throw ThrowRangeError($"Duration field {name} must be an integer, got {num.ToString(CultureInfo.InvariantCulture)}", realm: realm);
        }

        // Normalize -0 to +0
        if (num == 0)
        {
            return 0;
        }

        return num;
    }

    private static bool IsValidDurationRecord(DurationRecord record)
    {
        // Check sign consistency: all non-zero values must have the same sign
        var hasPositive = false;
        var hasNegative = false;
        double[] values =
        [
            record.Years, record.Months, record.Weeks, record.Days,
            record.Hours, record.Minutes, record.Seconds,
            record.Milliseconds, record.Microseconds, record.Nanoseconds,
        ];
        foreach (var v in values)
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
            return false;
        }

        // abs(years), abs(months), abs(weeks) must be < 2^32
        const double dateLimit = 4294967296.0; // 2^32
        if (Math.Abs(record.Years) >= dateLimit)
        {
            return false;
        }

        if (Math.Abs(record.Months) >= dateLimit)
        {
            return false;
        }

        if (Math.Abs(record.Weeks) >= dateLimit)
        {
            return false;
        }

        // Per spec step 16-17: compute normalizedSeconds using exact mathematical values
        // normalizedSeconds = days × 86400 + hours × 3600 + minutes × 60 + seconds
        //                   + milliseconds × 10^-3 + microseconds × 10^-6 + nanoseconds × 10^-9
        // Use decimal arithmetic for precision at the 2^53 boundary.
        // NOTE: (decimal)double can lose precision for large integer doubles (e.g. 2^53),
        // so we use ToExactDecimal which converts via long when possible.
        var normalizedSeconds =
            ToExactDecimal(record.Days) * 86400m +
            ToExactDecimal(record.Hours) * 3600m +
            ToExactDecimal(record.Minutes) * 60m +
            ToExactDecimal(record.Seconds) +
            ToExactDecimal(record.Milliseconds) / 1000m +
            ToExactDecimal(record.Microseconds) / 1_000_000m +
            ToExactDecimal(record.Nanoseconds) / 1_000_000_000m;

        // abs(normalizedSeconds) must be < 2^53
        const decimal secondsLimit = 9007199254740992m; // 2^53
        if (Math.Abs(normalizedSeconds) >= secondsLimit)
        {
            return false;
        }

        return true;
    }

    private static DurationRecord ParseDurationString(string input, RealmState realm)
    {
        // Minimal ISO 8601 duration parser: PnYnMnWnDTnHnMnS
        if (string.IsNullOrEmpty(input))
        {
            throw ThrowRangeError("Invalid duration string", realm: realm);
        }

        var s = input.AsSpan();
        var idx = 0;
        var sign = 1.0;

        if (idx < s.Length && (s[idx] == '+' || s[idx] == '-' || s[idx] == '\u2212'))
        {
            if (s[idx] != '+')
            {
                sign = -1.0;
            }

            idx++;
        }

        if (idx >= s.Length || s[idx] != 'P')
        {
            throw ThrowRangeError("Invalid duration string: missing 'P'", realm: realm);
        }

        idx++;

        var record = new DurationRecord();
        var inTimePart = false;
        var anyComponent = false;

        while (idx < s.Length)
        {
            if (s[idx] == 'T')
            {
                inTimePart = true;
                idx++;
                continue;
            }

            // Parse number (possibly fractional)
            var numStart = idx;
            while (idx < s.Length && (char.IsDigit(s[idx]) || s[idx] == '.' || s[idx] == ','))
            {
                idx++;
            }

            if (idx == numStart || idx >= s.Length)
            {
                throw ThrowRangeError("Invalid duration string", realm: realm);
            }

            var numStr = s[numStart..idx].ToString().Replace(',', '.');
            if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                throw ThrowRangeError($"Invalid number in duration string: {numStr}", realm: realm);
            }

            var designator = s[idx];
            idx++;
            anyComponent = true;

            if (!inTimePart)
            {
                switch (designator)
                {
                    case 'Y':
                        record.Years = sign * num;
                        break;
                    case 'M':
                        record.Months = sign * num;
                        break;
                    case 'W':
                        record.Weeks = sign * num;
                        break;
                    case 'D':
                        record.Days = sign * num;
                        break;
                    default:
                        throw ThrowRangeError($"Invalid duration designator: {designator}", realm: realm);
                }
            }
            else
            {
                switch (designator)
                {
                    case 'H':
                        record.Hours = sign * num;
                        break;
                    case 'M':
                        record.Minutes = sign * num;
                        break;
                    case 'S':
                        // Decompose fractional seconds into ms/µs/ns
                        DecomposeFractionalSeconds(sign * num, record);
                        break;
                    default:
                        throw ThrowRangeError($"Invalid duration time designator: {designator}", realm: realm);
                }
            }
        }

        if (!anyComponent)
        {
            throw ThrowRangeError("Invalid duration string: no components", realm: realm);
        }

        if (!IsValidDurationRecord(record))
        {
            throw ThrowRangeError("Duration string values are out of range", realm: realm);
        }

        return record;
    }

    private static void DecomposeFractionalSeconds(double totalSeconds, DurationRecord record)
    {
        var wholeSeconds = Math.Truncate(totalSeconds);
        record.Seconds = wholeSeconds;

        // Use string-based decomposition for the fractional part to avoid floating-point precision issues
        var absStr = Math.Abs(totalSeconds).ToString("R", CultureInfo.InvariantCulture);
        var dotIdx = absStr.IndexOf('.');
        if (dotIdx < 0)
        {
            return; // No fractional part
        }

        var fracStr = absStr[(dotIdx + 1)..];
        // Pad to 9 digits (nanosecond precision)
        fracStr = fracStr.PadRight(9, '0');
        if (fracStr.Length > 9)
        {
            fracStr = fracStr[..9];
        }

        var msStr = fracStr[..3];
        var usStr = fracStr[3..6];
        var nsStr = fracStr[6..9];

        var msSign = totalSeconds < 0 ? -1.0 : 1.0;
        record.Milliseconds = msSign * double.Parse(msStr, CultureInfo.InvariantCulture);
        record.Microseconds = msSign * double.Parse(usStr, CultureInfo.InvariantCulture);
        record.Nanoseconds = msSign * double.Parse(nsStr, CultureInfo.InvariantCulture);
    }

    #endregion

    #region PartitionDurationFormatPattern

    private readonly record struct DurationPart(string Type, string Value, string? Unit);

    private List<DurationPart> PartitionDurationFormatPattern(DurationFormatSlots slots, DurationRecord record)
    {
        var locale = "en"; // Only "en" is required by tests
        var timeSeparator = ":";

        // result is a list of "groups" — each group is a list of parts that form one element for ListFormat
        var resultGroups = new List<List<DurationPart>>();
        var needSeparator = false;
        var displayNegativeSign = true;

        for (var i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];
            var value = GetDurationValue(record, unit);
            var style = slots.UnitStyles[unit];
            var display = slots.UnitDisplays[unit];
            var numberFormatUnit = unit[..^1]; // Remove trailing 's'

            // Per spec: if current is seconds/ms/µs and next unit's style is "numeric", combine fractional
            var done = false;
            double fractionalDouble = double.NaN;
            var hasFractional = false;

            if (unit is "seconds" or "milliseconds" or "microseconds")
            {
                var nextUnit = Units[i + 1];
                var nextStyle = slots.UnitStyles[nextUnit];
                if (nextStyle == "numeric")
                {
                    // Compute combined fractional value as a double directly
                    fractionalDouble = ComputeFractionalDouble(record, unit, slots.FractionalDigits);
                    hasFractional = true;
                    done = true;
                }
            }

            // Display zero numeric minutes when seconds/subseconds will be displayed.
            // Per testIntl.js reference: only when a previous numeric unit was actually displayed (needSeparator).
            // Exception: in digital mode, use the resolved hours style (spec prevStyle) even if hours wasn't displayed.
            var displayRequired = false;
            if (unit == "minutes" && (needSeparator || slots.Style == "digital"))
            {
                var checkDisplay = needSeparator;
                if (!checkDisplay && slots.Style == "digital")
                {
                    var hoursStyle = slots.UnitStyles["hours"];
                    checkDisplay = hoursStyle is "numeric" or "2-digit";
                }

                if (checkDisplay)
                {
                    displayRequired = slots.UnitDisplays["seconds"] == "always" ||
                                      record.Seconds != 0 ||
                                      record.Milliseconds != 0 ||
                                      record.Microseconds != 0 ||
                                      record.Nanoseconds != 0;

                    // Also required if seconds has fractionalDigits > 0 (would show "0.000")
                    if (!displayRequired && slots.UnitStyles["seconds"] is "numeric" or "2-digit" &&
                        slots.FractionalDigits is > 0)
                    {
                        displayRequired = true;
                    }
                }
            }

            // Determine the effective value for display check:
            // For fractional units, the combined value may be non-zero even if the unit value is 0
            var effectiveValue = hasFractional ? fractionalDouble : value;

            // "auto" display omits zero units
            if (effectiveValue != 0 || display != "auto" || displayRequired)
            {
                double formatValue;
                if (hasFractional)
                {
                    formatValue = fractionalDouble;
                }
                else
                {
                    formatValue = value;
                }

                // Handle negative sign display
                var suppressSign = false;
                if (displayNegativeSign)
                {
                    displayNegativeSign = false;

                    // Set to negative zero if the duration is negative but this value is zero
                    if (formatValue == 0)
                    {
                        var isNegative = IsNegativeDuration(record);
                        if (isNegative)
                        {
                            formatValue = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
                        }
                    }
                }
                else
                {
                    // All subsequent values: use absolute value and suppress sign
                    suppressSign = true;
                    formatValue = Math.Abs(formatValue);
                }

                // Build NumberFormat options
                var nfSlots = BuildNumberFormatSlots(style, numberFormatUnit, locale, slots.NumberingSystem,
                    hasFractional, slots.FractionalDigits, style == "2-digit", suppressSign);

                // Format the number
                var nfResult = IntlNumberFormatter.FormatDouble(formatValue, nfSlots);

                List<DurationPart> list;
                if (!needSeparator)
                {
                    list = new List<DurationPart>();
                }
                else
                {
                    // Append time separator to the last group
                    list = resultGroups[^1];
                    list.Add(new DurationPart("literal", timeSeparator, null));
                }

                // Add parts from NumberFormat result
                if (nfResult.Parts is not null)
                {
                    foreach (var part in nfResult.Parts)
                    {
                        list.Add(new DurationPart(part.Type, part.Value, numberFormatUnit));
                    }
                }
                else
                {
                    list.Add(new DurationPart("literal", nfResult.Formatted, numberFormatUnit));
                }

                if (!needSeparator)
                {
                    if (style is "2-digit" or "numeric")
                    {
                        needSeparator = true;
                    }

                    resultGroups.Add(list);
                }
            }

            if (done)
            {
                break;
            }
        }

        // ListFormat joining
        var listStyle = slots.Style == "digital" ? "short" : slots.Style;

        // Collect strings from each group
        var strings = new List<string>(resultGroups.Count);
        foreach (var group in resultGroups)
        {
            var sb = new StringBuilder();
            foreach (var part in group)
            {
                sb.Append(part.Value);
            }

            strings.Add(sb.ToString());
        }

        // Format with ListFormat and reconstruct parts
        var listParts = IntlListFormatPrototype.FormatListToParts(strings, "unit", listStyle, locale);
        var flattened = new List<DurationPart>();
        var groupIdx = 0;
        foreach (var (type, partValue) in listParts)
        {
            if (type == "element")
            {
                if (groupIdx < resultGroups.Count)
                {
                    flattened.AddRange(resultGroups[groupIdx]);
                    groupIdx++;
                }
            }
            else
            {
                flattened.Add(new DurationPart(type, partValue, null));
            }
        }

        return flattened;
    }

    private static double GetDurationValue(DurationRecord record, string unit)
    {
        return unit switch
        {
            "years" => record.Years,
            "months" => record.Months,
            "weeks" => record.Weeks,
            "days" => record.Days,
            "hours" => record.Hours,
            "minutes" => record.Minutes,
            "seconds" => record.Seconds,
            "milliseconds" => record.Milliseconds,
            "microseconds" => record.Microseconds,
            "nanoseconds" => record.Nanoseconds,
            _ => 0,
        };
    }

    private static bool IsNegativeDuration(DurationRecord record)
    {
        return record.Years < 0 || record.Months < 0 || record.Weeks < 0 || record.Days < 0 ||
               record.Hours < 0 || record.Minutes < 0 || record.Seconds < 0 ||
               record.Milliseconds < 0 || record.Microseconds < 0 || record.Nanoseconds < 0;
    }

    private static double ComputeFractionalDouble(DurationRecord record, string unit, int? fractionalDigits)
    {
        // Compute using decimal for precision, matching the BigInt approach in the test harness
        int exponent;
        decimal ns;

        switch (unit)
        {
            case "seconds":
                exponent = 9;
                ns = ToExactDecimal(record.Nanoseconds) +
                     ToExactDecimal(record.Microseconds) * 1_000m +
                     ToExactDecimal(record.Milliseconds) * 1_000_000m +
                     ToExactDecimal(record.Seconds) * 1_000_000_000m;
                break;
            case "milliseconds":
                exponent = 6;
                ns = ToExactDecimal(record.Nanoseconds) +
                     ToExactDecimal(record.Microseconds) * 1_000m +
                     ToExactDecimal(record.Milliseconds) * 1_000_000m;
                break;
            case "microseconds":
                exponent = 3;
                ns = ToExactDecimal(record.Nanoseconds) +
                     ToExactDecimal(record.Microseconds) * 1_000m;
                break;
            default:
                return 0;
        }

        var divisor = PowerOfTen(exponent);

        // Return as double — the NumberFormat will handle fraction digits and rounding
        return (double)(ns / divisor);
    }

    /// <summary>
    /// Convert a double to decimal without the precision loss of direct (decimal)double cast.
    /// Duration fields are always integers (validated in GetDurationField), so we convert via
    /// long when the value fits, falling back to string round-trip for very large values.
    /// </summary>
    private static decimal ToExactDecimal(double value)
    {
        if (value == 0)
        {
            return 0m;
        }

        // All duration fields are integers. For integer doubles within long range,
        // converting via long is exact and avoids (decimal)double precision loss.
        if (Math.Abs(value) <= 9.2e18 && value == Math.Truncate(value))
        {
            return (decimal)(long)value;
        }

        // For very large values (e.g. microseconds/nanoseconds at scale), use string round-trip
        return decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static decimal PowerOfTen(int exponent)
    {
        decimal result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    private static IntlNumberFormatInternalSlots BuildNumberFormatSlots(
        string style,
        string numberFormatUnit,
        string locale,
        string numberingSystem,
        bool isFractional,
        int? fractionalDigits,
        bool is2Digit,
        bool suppressSign)
    {
        var culture = IntlUtilities.ResolveCulture(locale);
        var signDisplay = suppressSign ? "never" : "auto";

        if (style is "numeric" or "2-digit")
        {
            // Numeric style: no unit display, no grouping
            return new IntlNumberFormatInternalSlots
            {
                Locale = locale,
                NumberingSystem = numberingSystem,
                Style = "decimal",
                MinimumIntegerDigits = is2Digit ? 2 : 1,
                MinimumFractionDigits = isFractional ? (fractionalDigits ?? 0) : 0,
                MaximumFractionDigits = isFractional ? (fractionalDigits ?? 9) : 0,
                UseGrouping = "false",
                SignDisplay = signDisplay,
                RoundingMode = isFractional ? "trunc" : "halfExpand",
                Culture = culture,
            };
        }

        // Non-numeric: format as unit (long, short, narrow)
        var slots = new IntlNumberFormatInternalSlots
        {
            Locale = locale,
            NumberingSystem = numberingSystem,
            Style = "unit",
            Unit = numberFormatUnit,
            UnitDisplay = style,
            SignDisplay = signDisplay,
            Culture = culture,
            MinimumFractionDigits = isFractional ? (fractionalDigits ?? 0) : 0,
            MaximumFractionDigits = isFractional ? (fractionalDigits ?? 9) : 0,
            RoundingMode = isFractional ? "trunc" : "halfExpand",
        };

        return slots;
    }

    #endregion
}
