#region

using System.Globalization;
using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.DateTimeFormat", ToStringTag = "Intl.DateTimeFormat")]
public sealed partial class IntlDateTimeFormatPrototype
{
    private const string BrandKey = "__dateTimeFormat__";
    private const string SlotsKey = "__dateTimeFormatSlots__";

    internal static void InitializeInternalSlots(JsObject instance, DateTimeFormatInternalSlots slots)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty(SlotsKey, JsValue.FromObjectUnsafe(slots));
    }

    [JsHostGetter("format", DisplayName = "get format")]
    private JsValue GetFormat(JsValue thisValue)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var realm = Realm;
        return (JsValue)CreateBoundFormatFunction(value => FormatChecked(value, slotData, realm));
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);
        var dateValue = args.GetArgument(0);
        var epochMs = ToEpochMilliseconds(dateValue);
        if (double.IsNaN(epochMs))
        {
            throw ThrowRangeError("Invalid time value", realm: Realm);
        }

        var dto = ToDateTimeOffset(epochMs, slotData.TimeZone);
        var culture = IntlUtilities.ResolveCulture(slotData.Locale);
        var parts = FormatToPartsInternal(dto, slotData, culture);
        TranslatePartsDigits(parts, slotData.NumberingSystem);
        return JsValue.FromJsArray(parts);
    }

    [JsHostMethod("formatRange", Length = 2d)]
    private JsValue FormatRange(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);

        var startDate = args.GetArgument(0);
        var endDate = args.GetArgument(1);

        // Per spec: if startDate or endDate is undefined, throw TypeError
        if (startDate.IsUndefined)
        {
            throw ThrowTypeError("startDate is undefined", realm: Realm);
        }

        if (endDate.IsUndefined)
        {
            throw ThrowTypeError("endDate is undefined", realm: Realm);
        }

        var startMs = ToEpochMilliseconds(startDate);
        var endMs = ToEpochMilliseconds(endDate);

        // Per spec: if either is NaN, throw RangeError
        if (double.IsNaN(startMs))
        {
            throw ThrowRangeError("Invalid start date value", realm: Realm);
        }

        if (double.IsNaN(endMs))
        {
            throw ThrowRangeError("Invalid end date value", realm: Realm);
        }

        var startFormatted = FormatDateTimeOffset(startMs, slotData);
        var endFormatted = FormatDateTimeOffset(endMs, slotData);

        // If both format to the same string, return single formatted value (no range)
        if (string.Equals(startFormatted, endFormatted, StringComparison.Ordinal))
        {
            return new JsValue(startFormatted);
        }

        // Try range collapsing for named month formats
        if (ShouldCollapseRange(slotData))
        {
            var culture = IntlUtilities.ResolveCulture(slotData.Locale);
            var startDto = ToDateTimeOffset(startMs, slotData.TimeZone);
            var endDto = ToDateTimeOffset(endMs, slotData.TimeZone);
            var startParts = FormatToPartsInternal(startDto, slotData, culture);
            var endParts = FormatToPartsInternal(endDto, slotData, culture);

            var collapsed = BuildCollapsedRangeString(startParts, endParts);
            if (collapsed is not null)
            {
                return new JsValue(collapsed);
            }
        }

        return new JsValue($"{startFormatted} \u2013 {endFormatted}");
    }

    [JsHostMethod("formatRangeToParts", Length = 2d)]
    private JsValue FormatRangeToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var slotData = ValidateReceiver(thisValue, out _);

        var startDate = args.GetArgument(0);
        var endDate = args.GetArgument(1);

        // Per spec: if startDate or endDate is undefined, throw TypeError
        if (startDate.IsUndefined)
        {
            throw ThrowTypeError("startDate is undefined", realm: Realm);
        }

        if (endDate.IsUndefined)
        {
            throw ThrowTypeError("endDate is undefined", realm: Realm);
        }

        var startMs = ToEpochMilliseconds(startDate);
        var endMs = ToEpochMilliseconds(endDate);

        // Per spec: if either is NaN, throw RangeError
        if (double.IsNaN(startMs))
        {
            throw ThrowRangeError("Invalid start date value", realm: Realm);
        }

        if (double.IsNaN(endMs))
        {
            throw ThrowRangeError("Invalid end date value", realm: Realm);
        }

        var startFormatted = FormatDateTimeOffset(startMs, slotData);
        var endFormatted = FormatDateTimeOffset(endMs, slotData);

        var culture = IntlUtilities.ResolveCulture(slotData.Locale);

        if (string.Equals(startFormatted, endFormatted, StringComparison.Ordinal))
        {
            // Same value — return typed parts from formatToParts with source="shared"
            var dto = ToDateTimeOffset(startMs, slotData.TimeZone);
            var typedParts = FormatToPartsInternal(dto, slotData, culture);

            // Set source="shared" on all parts
            var sharedParts = new JsArray(Realm);
            for (var i = 0; i < typedParts.Length; i++)
            {
                var part = typedParts.Get(i);
                if (part.TryGetObject<JsObject>(out var partObj))
                {
                    partObj.SetProperty("source", new JsValue("shared"));
                }

                sharedParts.Push(part);
            }

            return JsValue.FromJsArray(sharedParts);
        }

        var parts = new JsArray(Realm);
        var startDto = ToDateTimeOffset(startMs, slotData.TimeZone);
        var endDto = ToDateTimeOffset(endMs, slotData.TimeZone);

        var startParts = FormatToPartsInternal(startDto, slotData, culture);
        var endParts = FormatToPartsInternal(endDto, slotData, culture);

        // Try range collapsing for named month formats
        if (ShouldCollapseRange(slotData))
        {
            var collapsed = BuildCollapsedRangeParts(Realm, startParts, endParts);
            if (collapsed is not null)
            {
                return JsValue.FromJsArray(collapsed);
            }
        }

        // No collapsing: full start + separator + full end
        for (var i = 0; i < startParts.Length; i++)
        {
            var part = startParts.Get(i);
            if (part.TryGetObject<JsObject>(out var partObj))
            {
                partObj.SetProperty("source", new JsValue("startRange"));
            }

            parts.Push(part);
        }

        parts.Push(CreateRangePart("literal", " \u2013 ", "shared"));

        for (var i = 0; i < endParts.Length; i++)
        {
            var part = endParts.Get(i);
            if (part.TryGetObject<JsObject>(out var partObj))
            {
                partObj.SetProperty("source", new JsValue("endRange"));
            }

            parts.Push(part);
        }

        return JsValue.FromJsArray(parts);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue)
    {
        var slots = ValidateReceiver(thisValue, out _);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string op = "Intl.DateTimeFormat.prototype.resolvedOptions";

        // Per spec order: locale, calendar, numberingSystem, timeZone, then conditionally
        // hourCycle, hour12, then components in table order, then dateStyle, timeStyle
        CreateDataPropertyOrThrowJsValue(obj, "locale", slots.Locale, Realm, op);
        CreateDataPropertyOrThrowJsValue(obj, "calendar", slots.Calendar, Realm, op);
        CreateDataPropertyOrThrowJsValue(obj, "numberingSystem", slots.NumberingSystem, Realm, op);
        CreateDataPropertyOrThrowJsValue(obj, "timeZone", slots.TimeZone, Realm, op);

        // hourCycle and hour12 are only output when hour component is present or timeStyle is used
        var hasHour = slots.Components.ContainsKey("hour") || slots.TimeStyle is not null;
        if (hasHour)
        {
            CreateDataPropertyOrThrowJsValue(obj, "hourCycle", slots.HourCycle, Realm, op);
            CreateDataPropertyOrThrowJsValue(obj, "hour12", slots.Hour12 ?? (slots.HourCycle is "h11" or "h12"),
                Realm, op);
        }

        if (slots.DateStyle is not null || slots.TimeStyle is not null)
        {
            // When using styles, output the inferred components
            OutputStyleComponents(obj, slots, op);
        }
        else
        {
            // Output only the components that were requested
            foreach (var component in DateTimeFormatInternalSlots.ComponentNames)
            {
                if (slots.Components.TryGetValue(component, out var value))
                {
                    if (string.Equals(component, "fractionalSecondDigits", StringComparison.Ordinal))
                    {
                        CreateDataPropertyOrThrowJsValue(obj, component,
                            int.TryParse(value, CultureInfo.InvariantCulture, out var fsd)
                                ? (double)fsd
                                : JsValue.Undefined, Realm, op);
                    }
                    else
                    {
                        CreateDataPropertyOrThrowJsValue(obj, component, value, Realm, op);
                    }
                }
            }
        }

        // dateStyle and timeStyle
        if (slots.DateStyle is not null)
        {
            CreateDataPropertyOrThrowJsValue(obj, "dateStyle", slots.DateStyle, Realm, op);
        }

        if (slots.TimeStyle is not null)
        {
            CreateDataPropertyOrThrowJsValue(obj, "timeStyle", slots.TimeStyle, Realm, op);
        }

        return new JsValue(obj);
    }

    private void OutputStyleComponents(JsObject obj, DateTimeFormatInternalSlots slots, string op)
    {
        var realm = Realm;

        // dateStyle implies year, month, day (and possibly weekday, era)
        if (slots.DateStyle is not null)
        {
            switch (slots.DateStyle)
            {
                case "full":
                    CreateDataPropertyOrThrowJsValue(obj, "weekday", "long", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "year", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "month", "long", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "day", "numeric", realm!, op);
                    break;
                case "long":
                    CreateDataPropertyOrThrowJsValue(obj, "year", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "month", "long", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "day", "numeric", realm!, op);
                    break;
                case "medium":
                    CreateDataPropertyOrThrowJsValue(obj, "year", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "month", "short", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "day", "numeric", realm!, op);
                    break;
                case "short":
                    CreateDataPropertyOrThrowJsValue(obj, "year", "2-digit", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "month", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "day", "numeric", realm!, op);
                    break;
            }
        }

        // timeStyle implies hour, minute (and possibly second)
        if (slots.TimeStyle is not null)
        {
            switch (slots.TimeStyle)
            {
                case "full":
                case "long":
                    CreateDataPropertyOrThrowJsValue(obj, "hour", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "minute", "2-digit", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "second", "2-digit", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "timeZoneName",
                        slots.TimeStyle is "full" ? "long" : "short", realm!, op);
                    break;
                case "medium":
                    CreateDataPropertyOrThrowJsValue(obj, "hour", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "minute", "2-digit", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "second", "2-digit", realm!, op);
                    break;
                case "short":
                    CreateDataPropertyOrThrowJsValue(obj, "hour", "numeric", realm!, op);
                    CreateDataPropertyOrThrowJsValue(obj, "minute", "2-digit", realm!, op);
                    break;
            }
        }
    }

    private DateTimeFormatInternalSlots ValidateReceiver(JsValue thisValue, out JsObject instance)
    {
        var obj = thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.DateTimeFormat method called on incompatible receiver");
        if (!obj.TryGetProperty(SlotsKey, out var slotValue) ||
            !slotValue.TryGetObject<DateTimeFormatInternalSlots>(out var slots))
        {
            throw ThrowTypeError("Intl.DateTimeFormat method called on incompatible receiver",
                realm: Realm);
        }

        instance = obj;
        return slots;
    }

    internal static JsValue FormatFromTemporal(JsValue thisValue, JsValue value, RealmState realm,
        string? displayTimeZoneId = null)
    {
        var slots = GetInternalSlots(thisValue, realm);
        return FormatChecked(value, slots, realm, displayTimeZoneId);
    }

    internal static string GetCalendarForTemporal(JsValue thisValue, RealmState realm)
    {
        return GetInternalSlots(thisValue, realm).Calendar;
    }

    private static DateTimeFormatInternalSlots GetInternalSlots(JsValue thisValue, RealmState realm)
    {
        var obj = thisValue.EnsureBrand(BrandKey, realm,
            "Intl.DateTimeFormat method called on incompatible receiver");
        if (!obj.TryGetProperty(SlotsKey, out var slotValue) ||
            !slotValue.TryGetObject<DateTimeFormatInternalSlots>(out var slots))
        {
            throw ThrowTypeError("Intl.DateTimeFormat method called on incompatible receiver",
                realm: realm);
        }

        return slots;
    }

    private static JsObject CreateRangePart(string type, string value, string? source = null)
    {
        var obj = new JsObject();
        obj.SetProperty("type", new JsValue(type));
        obj.SetProperty("value", new JsValue(value));
        if (source is not null)
        {
            obj.SetProperty("source", new JsValue(source));
        }

        return obj;
    }

    /// <summary>
    /// Returns true when the format uses named months (short/long/narrow) and should attempt range collapsing.
    /// Numeric dates are not collapsed per CLDR conventions.
    /// </summary>
    private static bool ShouldCollapseRange(DateTimeFormatInternalSlots slots)
    {
        // Only collapse when using component-based formatting (not style-based)
        if (slots.DateStyle is not null || slots.TimeStyle is not null)
        {
            return false;
        }

        // Only collapse when month is named (short/long/narrow), not numeric
        return slots.Components.TryGetValue("month", out var monthWidth)
               && monthWidth is "long" or "short" or "narrow";
    }

    /// <summary>
    /// Extract (type, value) from a typed part JsObject.
    /// </summary>
    private static (string type, string value) ExtractPartInfo(JsValue part)
    {
        if (!part.TryGetObject<JsObject>(out var obj))
        {
            return ("literal", "");
        }

        var type = obj.TryGetProperty("type", out var t) ? (string)t.ObjectValue! : "literal";
        var value = obj.TryGetProperty("value", out var v) ? (string)v.ObjectValue! : "";
        return (type, value);
    }

    /// <summary>
    /// Build a collapsed range string by finding shared prefix/suffix parts.
    /// Returns null if no collapsing is possible (all parts differ).
    /// </summary>
    private static string? BuildCollapsedRangeString(JsArray startParts, JsArray endParts)
    {
        var startCount = (int)startParts.Length;
        var endCount = (int)endParts.Length;

        if (startCount != endCount)
        {
            return null; // Different structure, can't collapse
        }

        // Find shared prefix length
        var prefixLen = 0;
        while (prefixLen < startCount)
        {
            var (st, sv) = ExtractPartInfo(startParts.Get(prefixLen));
            var (et, ev) = ExtractPartInfo(endParts.Get(prefixLen));
            if (st != et || sv != ev)
            {
                break;
            }

            prefixLen++;
        }

        // If all parts match, they're equal (shouldn't reach here)
        if (prefixLen == startCount)
        {
            return null;
        }

        // Find shared suffix length (don't overlap with prefix)
        var suffixLen = 0;
        while (suffixLen < startCount - prefixLen)
        {
            var si = startCount - 1 - suffixLen;
            var ei = endCount - 1 - suffixLen;
            var (st, sv) = ExtractPartInfo(startParts.Get(si));
            var (et, ev) = ExtractPartInfo(endParts.Get(ei));
            if (st != et || sv != ev)
            {
                break;
            }

            suffixLen++;
        }

        // Per CLDR: only collapse when there's a shared suffix (year is shared).
        // When the year (last significant component) differs, show full dates.
        if (suffixLen == 0)
        {
            return null;
        }

        // Build collapsed string: prefix + start-middle + " – " + end-middle + suffix
        var sb = new StringBuilder();

        // Shared prefix
        for (var i = 0; i < prefixLen; i++)
        {
            var (_, v) = ExtractPartInfo(startParts.Get(i));
            sb.Append(v);
        }

        // Start-specific middle
        for (var i = prefixLen; i < startCount - suffixLen; i++)
        {
            var (_, v) = ExtractPartInfo(startParts.Get(i));
            sb.Append(v);
        }

        sb.Append(" \u2013 ");

        // End-specific middle
        for (var i = prefixLen; i < endCount - suffixLen; i++)
        {
            var (_, v) = ExtractPartInfo(endParts.Get(i));
            sb.Append(v);
        }

        // Shared suffix
        for (var i = startCount - suffixLen; i < startCount; i++)
        {
            var (_, v) = ExtractPartInfo(startParts.Get(i));
            sb.Append(v);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build collapsed range parts with source attributes.
    /// Returns null if no collapsing is possible.
    /// </summary>
    private static JsArray? BuildCollapsedRangeParts(RealmState realm, JsArray startParts, JsArray endParts)
    {
        var startCount = (int)startParts.Length;
        var endCount = (int)endParts.Length;

        if (startCount != endCount)
        {
            return null;
        }

        // Find shared prefix length
        var prefixLen = 0;
        while (prefixLen < startCount)
        {
            var (st, sv) = ExtractPartInfo(startParts.Get(prefixLen));
            var (et, ev) = ExtractPartInfo(endParts.Get(prefixLen));
            if (st != et || sv != ev)
            {
                break;
            }

            prefixLen++;
        }

        if (prefixLen == startCount)
        {
            return null;
        }

        // Find shared suffix length
        var suffixLen = 0;
        while (suffixLen < startCount - prefixLen)
        {
            var si = startCount - 1 - suffixLen;
            var ei = endCount - 1 - suffixLen;
            var (st, sv) = ExtractPartInfo(startParts.Get(si));
            var (et, ev) = ExtractPartInfo(endParts.Get(ei));
            if (st != et || sv != ev)
            {
                break;
            }

            suffixLen++;
        }

        if (suffixLen == 0)
        {
            return null;
        }

        var result = new JsArray(realm);

        // Shared prefix parts
        for (var i = 0; i < prefixLen; i++)
        {
            var (type, value) = ExtractPartInfo(startParts.Get(i));
            result.Push(CreateRangePart(type, value, "shared"));
        }

        // Start-specific middle
        for (var i = prefixLen; i < startCount - suffixLen; i++)
        {
            var (type, value) = ExtractPartInfo(startParts.Get(i));
            result.Push(CreateRangePart(type, value, "startRange"));
        }

        // Range separator
        result.Push(CreateRangePart("literal", " \u2013 ", "shared"));

        // End-specific middle
        for (var i = prefixLen; i < endCount - suffixLen; i++)
        {
            var (type, value) = ExtractPartInfo(endParts.Get(i));
            result.Push(CreateRangePart(type, value, "endRange"));
        }

        // Shared suffix parts
        for (var i = startCount - suffixLen; i < startCount; i++)
        {
            var (type, value) = ExtractPartInfo(startParts.Get(i));
            result.Push(CreateRangePart(type, value, "shared"));
        }

        return result;
    }

    /// <summary>
    /// Translate digits in all part values of a typed parts array.
    /// </summary>
    private static void TranslatePartsDigits(JsArray parts, string numberingSystem)
    {
        if (numberingSystem is "latn")
        {
            return;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts.Get(i);
            if (part.TryGetObject<JsObject>(out var partObj) &&
                partObj.TryGetProperty("value", out var val) &&
                val.TryGetString(out var str))
            {
                var translated = IntlUtilities.TranslateDigits(str, numberingSystem);
                if (!ReferenceEquals(translated, str))
                {
                    partObj.SetProperty("value", new JsValue(translated));
                }
            }
        }
    }

    private HostFunction CreateBoundFormatFunction(Func<JsValue, JsValue> formatter)
    {
        var function = new HostFunction((_, args) => formatter(args.GetArgument(0)), Realm, false);
        DefineFormatFunctionMetadata(function);
        return function;
    }

    private static void DefineFormatFunctionMetadata(HostFunction function)
    {
        function.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        function.DefineProperty("name",
            new PropertyDescriptor { Value = string.Empty, Writable = false, Enumerable = false, Configurable = true });
    }

    private static string FormatDateTimeOffset(double epochMs, DateTimeFormatInternalSlots slots)
    {
        return (string)FormatEpoch(epochMs, slots).ObjectValue!;
    }

    private static JsValue FormatChecked(JsValue value, DateTimeFormatInternalSlots slots, RealmState realm,
        string? displayTimeZoneId = null)
    {
        var epochMilliseconds = ToEpochMilliseconds(value);
        if (double.IsNaN(epochMilliseconds))
        {
            throw ThrowRangeError("Invalid time value", realm: realm);
        }

        return FormatEpoch(epochMilliseconds, slots, displayTimeZoneId);
    }

    private static JsValue FormatEpoch(double epochMilliseconds, DateTimeFormatInternalSlots slots,
        string? displayTimeZoneId = null)
    {
        var dto = ToDateTimeOffset(epochMilliseconds, slots.TimeZone);
        var culture = IntlUtilities.ResolveCulture(slots.Locale);

        string result;

        // Style-based formatting
        if (slots.DateStyle is not null || slots.TimeStyle is not null)
        {
            result = FormatWithStyles(dto, slots, culture, displayTimeZoneId);
        }
        // Component-based formatting
        else if (slots.Components.Count > 0)
        {
            result = FormatWithComponents(dto, slots, culture, displayTimeZoneId);
        }
        else
        {
            // Default: year, month, day
            result = FormatDefault(dto, culture);
        }

        // Apply numbering system digit translation
        result = IntlUtilities.TranslateDigits(result, slots.NumberingSystem);

        return new JsValue(result);
    }

    private static string FormatWithStyles(DateTimeOffset dto, DateTimeFormatInternalSlots slots, CultureInfo culture,
        string? displayTimeZoneId)
    {
        var datePart = slots.Calendar.StartsWith("islamic", StringComparison.OrdinalIgnoreCase)
            ? FormatIslamicDatePart(dto, slots.DateStyle)
            : FormatGregorianDatePart(dto, slots.DateStyle, ApplyCalendarToCulture(culture, slots.Calendar));

        var timePart = slots.TimeStyle switch
        {
            "full" or "long" => FormatTime(dto, slots, culture, includeSeconds: true, includeTimeZone: true,
                longTimeZone: slots.TimeStyle is "full", displayTimeZoneId),
            "medium" => FormatTime(dto, slots, culture, includeSeconds: true, includeTimeZone: false,
                longTimeZone: false, displayTimeZoneId),
            "short" => FormatTime(dto, slots, culture, includeSeconds: false, includeTimeZone: false,
                longTimeZone: false, displayTimeZoneId),
            _ => null
        };

        if (datePart is not null && timePart is not null)
        {
            return $"{datePart}, {timePart}";
        }

        return datePart ?? timePart ?? dto.ToString("f", culture);
    }

    private static string? FormatGregorianDatePart(DateTimeOffset dto, string? dateStyle, CultureInfo culture)
    {
        return dateStyle switch
        {
            "full" => dto.ToString("dddd, MMMM d, yyyy", culture),
            "long" => dto.ToString("MMMM d, yyyy", culture),
            "medium" => dto.ToString("MMM d, yyyy", culture),
            "short" => dto.ToString("M/d/yy", culture),
            _ => null
        };
    }

    private static string? FormatIslamicDatePart(DateTimeOffset dto, string? dateStyle)
    {
        if (dateStyle is null)
        {
            return null;
        }

        var calendar = new HijriCalendar();
        var dateTime = dto.DateTime;
        var year = calendar.GetYear(dateTime);
        var month = calendar.GetMonth(dateTime);
        var day = calendar.GetDayOfMonth(dateTime);
        var monthNames = new[]
        {
            "Muharram", "Safar", "Rabi I", "Rabi II", "Jumada I", "Jumada II",
            "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah"
        };
        var monthName = month >= 1 && month <= monthNames.Length ? monthNames[month - 1] : month.ToString(CultureInfo.InvariantCulture);
        var weekday = dateTime.ToString("dddd", CultureInfo.InvariantCulture);

        return dateStyle switch
        {
            "full" => $"{weekday}, {monthName} {day}, {year}",
            "long" => $"{monthName} {day}, {year}",
            "medium" => $"{monthName[..Math.Min(3, monthName.Length)]} {day}, {year}",
            "short" => $"{month}/{day}/{year % 100:00}",
            _ => null
        };
    }

    private static CultureInfo ApplyCalendarToCulture(CultureInfo culture, string calendar)
    {
        if (!calendar.StartsWith("islamic", StringComparison.OrdinalIgnoreCase))
        {
            return culture;
        }

        try
        {
            var clone = (CultureInfo)culture.Clone();
            var format = (DateTimeFormatInfo)clone.DateTimeFormat.Clone();
            format.Calendar = new HijriCalendar();
            clone.DateTimeFormat = format;
            return clone;
        }
        catch
        {
            return culture;
        }
    }

    private static string FormatTime(DateTimeOffset dto, DateTimeFormatInternalSlots slots, CultureInfo culture,
        bool includeSeconds, bool includeTimeZone, bool longTimeZone, string? displayTimeZoneId)
    {
        var sb = new StringBuilder();
        var use12 = slots.HourCycle is "h11" or "h12";

        int hour;
        if (slots.HourCycle is "h11")
        {
            hour = dto.Hour % 12;
        }
        else if (slots.HourCycle is "h12")
        {
            hour = dto.Hour % 12;
            if (hour == 0)
            {
                hour = 12;
            }
        }
        else if (slots.HourCycle is "h24")
        {
            hour = dto.Hour == 0 ? 24 : dto.Hour;
        }
        else
        {
            hour = dto.Hour;
        }

        sb.Append(hour.ToString(CultureInfo.InvariantCulture));
        sb.Append(':');
        sb.Append(dto.Minute.ToString("D2", CultureInfo.InvariantCulture));

        if (includeSeconds)
        {
            sb.Append(':');
            sb.Append(dto.Second.ToString("D2", CultureInfo.InvariantCulture));
        }

        if (use12)
        {
            sb.Append(' ');
            sb.Append(dto.Hour < 12 ? "AM" : "PM");
        }

        if (includeTimeZone)
        {
            sb.Append(' ');
            sb.Append(GetTimeZoneDisplay(slots.DisplayTimeZone ?? displayTimeZoneId ?? slots.TimeZone, dto,
                longTimeZone));
        }

        return sb.ToString();
    }

    private static string FormatWithComponents(DateTimeOffset dto, DateTimeFormatInternalSlots slots,
        CultureInfo culture, string? displayTimeZoneId)
    {
        var sb = new StringBuilder();
        var components = slots.Components;
        var addedSomething = false;

        // Weekday prefix
        if (components.TryGetValue("weekday", out var weekday))
        {
            AppendSeparator(sb, ref addedSomething);
            sb.Append(FormatWeekday(dto, weekday, culture));
        }

        if (components.TryGetValue("era", out var era))
        {
            AppendSeparator(sb, ref addedSomething);
            sb.Append(FormatEra(dto, era));
        }

        // Date components (year/month/day) - locale-aware ordering and separators
        var hasYear = components.TryGetValue("year", out var year);
        var hasMonth = components.TryGetValue("month", out var month);
        var hasDay = components.TryGetValue("day", out var day);

        if (hasYear || hasMonth || hasDay)
        {
            var (order, numericSep) = ParseLocaleDateOrder(culture);
            var dateStr = FormatLocaleDateString(dto, culture, order, numericSep,
                hasYear, year, hasMonth, month, hasDay, day);
            AppendSeparator(sb, ref addedSomething);
            sb.Append(dateStr);
        }

        // dayPeriod only
        if (components.TryGetValue("dayPeriod", out var dayPeriod) && !components.ContainsKey("hour"))
        {
            AppendSeparator(sb, ref addedSomething);
            sb.Append(GetDayPeriod(dto, dayPeriod));
        }

        // Time components
        string? pendingDayPeriod = null;
        if (components.TryGetValue("hour", out var hour))
        {
            AppendSeparator(sb, ref addedSomething);
            sb.Append(FormatHour(dto, hour, slots));

            // Determine AM/PM but defer appending until after all time components
            if (components.TryGetValue("dayPeriod", out var dp))
            {
                pendingDayPeriod = GetDayPeriod(dto, dp);
            }
            else if (slots.HourCycle is "h11" or "h12")
            {
                pendingDayPeriod = dto.Hour < 12 ? "AM" : "PM";
            }
        }

        // Per ICU best-fit: when minute and second appear together (or with hour),
        // they should always be 2-digit padded regardless of the "numeric" width setting
        var forceMinSecPad = components.ContainsKey("minute") && components.ContainsKey("second");

        if (components.TryGetValue("minute", out var minute))
        {
            if (components.ContainsKey("hour"))
            {
                sb.Append(':');
            }
            else
            {
                AppendSeparator(sb, ref addedSomething);
            }

            sb.Append(forceMinSecPad ? FormatMinute(dto, "2-digit") : FormatMinute(dto, minute));
        }

        if (components.TryGetValue("second", out var second))
        {
            if (components.ContainsKey("minute"))
            {
                sb.Append(':');
            }
            else
            {
                AppendSeparator(sb, ref addedSomething);
            }

            sb.Append(forceMinSecPad ? FormatSecond(dto, "2-digit") : FormatSecond(dto, second));
        }

        if (components.TryGetValue("fractionalSecondDigits", out var fsdStr) &&
            int.TryParse(fsdStr, CultureInfo.InvariantCulture, out var fsd))
        {
            sb.Append(IntlUtilities.GetDecimalSeparator(slots.NumberingSystem));
            var ms = dto.Millisecond;
            var formatted = ms.ToString("D3", CultureInfo.InvariantCulture);
            sb.Append(formatted[..fsd]);
        }

        // Append AM/PM after all time components
        if (pendingDayPeriod is not null)
        {
            sb.Append(' ');
            sb.Append(pendingDayPeriod);
        }

        if (components.TryGetValue("timeZoneName", out var tzName))
        {
            AppendSeparator(sb, ref addedSomething);
            sb.Append(GetTimeZoneDisplay(slots.DisplayTimeZone ?? displayTimeZoneId ?? slots.TimeZone, dto,
                tzName is "long"));
        }

        return sb.ToString();
    }

    private static string FormatDefault(DateTimeOffset dto, CultureInfo culture)
    {
        // When no components and no styles, use default: year, month, day with locale pattern
        var (order, separator) = ParseLocaleDateOrder(culture);
        return FormatLocaleDateString(dto, culture, order, separator,
            true, "numeric", true, "numeric", true, "numeric");
    }

    private static void AppendSeparator(StringBuilder sb, ref bool addedSomething)
    {
        if (addedSomething)
        {
            sb.Append(", ");
        }

        addedSomething = true;
    }

    private enum DateOrder
    {
        MDY,
        DMY,
        YMD
    }

    private static (DateOrder order, string separator) ParseLocaleDateOrder(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;

        var mPos = pattern.IndexOf('M');
        var dPos = pattern.IndexOf('d');
        var yPos = pattern.IndexOf('y');

        if (mPos < 0)
        {
            mPos = int.MaxValue;
        }

        if (dPos < 0)
        {
            dPos = int.MaxValue;
        }

        if (yPos < 0)
        {
            yPos = int.MaxValue;
        }

        // Extract separator between first two date components
        var separator = "/";
        var positions = new (int pos, char ch)[] { (mPos, 'M'), (dPos, 'd'), (yPos, 'y') };
        Array.Sort(positions, (a, b) => a.pos.CompareTo(b.pos));

        if (positions.Length >= 2 && positions[0].pos != int.MaxValue && positions[1].pos != int.MaxValue)
        {
            // Find end of first component group
            var endFirst = positions[0].pos;
            while (endFirst < pattern.Length && pattern[endFirst] == positions[0].ch)
            {
                endFirst++;
            }

            // Find start of second component
            var startSecond = positions[1].pos;

            if (endFirst < startSecond)
            {
                separator = pattern[endFirst..startSecond];
            }
        }

        DateOrder order;
        if (mPos < dPos && mPos < yPos)
        {
            order = DateOrder.MDY;
        }
        else if (dPos < mPos && dPos < yPos)
        {
            order = DateOrder.DMY;
        }
        else
        {
            order = DateOrder.YMD;
        }

        return (order, separator);
    }

    private static char[] GetDateComponentChars(DateOrder order) => order switch
    {
        DateOrder.MDY => ['M', 'd', 'y'],
        DateOrder.DMY => ['d', 'M', 'y'],
        DateOrder.YMD => ['y', 'M', 'd'],
        _ => ['M', 'd', 'y']
    };

    private static string FormatLocaleDateString(
        DateTimeOffset dto, CultureInfo culture, DateOrder order, string numericSep,
        bool hasYear, string? yearWidth,
        bool hasMonth, string? monthWidth,
        bool hasDay, string? dayWidth)
    {
        var namedMonth = monthWidth is "long" or "short" or "narrow";

        var yearStr = hasYear ? FormatYear(dto, yearWidth) : null;
        var monthStr = hasMonth ? FormatMonth(dto, monthWidth, culture) : null;
        var dayStr = hasDay ? FormatDay(dto, dayWidth) : null;

        // Build ordered list of present components
        var parts = new List<(char ch, string value)>();
        foreach (var c in GetDateComponentChars(order))
        {
            switch (c)
            {
                case 'M' when monthStr is not null:
                    parts.Add(('M', monthStr));
                    break;
                case 'd' when dayStr is not null:
                    parts.Add(('d', dayStr));
                    break;
                case 'y' when yearStr is not null:
                    parts.Add(('y', yearStr));
                    break;
            }
        }

        if (parts.Count == 0)
        {
            return "";
        }

        if (parts.Count == 1)
        {
            return parts[0].value;
        }

        if (!namedMonth)
        {
            return string.Join(numericSep, parts.Select(p => p.value));
        }

        // Named month: use locale-aware separators
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(GetNamedMonthSeparator(order, parts, i));
            }

            sb.Append(parts[i].value);
        }

        return sb.ToString();
    }

    private static string GetNamedMonthSeparator(DateOrder order, List<(char ch, string value)> parts, int index)
    {
        // MDY: "Jan 3, 2019" → " " between M-d, ", " between d-y
        // DMY: "3 Jan 2019" → " " between all
        // YMD: "2019 Jan 3" → " " between all
        if (order == DateOrder.MDY && parts[index - 1].ch == 'd' && parts[index].ch == 'y')
        {
            return ", ";
        }

        if (order == DateOrder.DMY && parts[index - 1].ch == 'd' && parts[index].ch == 'M')
        {
            return " ";
        }

        return " ";
    }

    private static string FormatWeekday(DateTimeOffset dto, string width, CultureInfo culture)
    {
        return width switch
        {
            "long" => dto.ToString("dddd", culture),
            "short" => dto.ToString("ddd", culture),
            "narrow" => dto.ToString("dddd", culture)[..1],
            _ => dto.ToString("dddd", culture)
        };
    }

    private static string FormatEra(DateTimeOffset dto, string width)
    {
        var era = dto.Year > 0 ? "AD" : "BC";
        return width switch
        {
            "long" => dto.Year > 0 ? "Anno Domini" : "Before Christ",
            "short" => era,
            "narrow" => era[..1],
            _ => era
        };
    }

    private static string FormatYear(DateTimeOffset dto, string width)
    {
        return width switch
        {
            "2-digit" => (dto.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
            _ => dto.Year.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatMonth(DateTimeOffset dto, string width, CultureInfo culture)
    {
        return width switch
        {
            "long" => dto.ToString("MMMM", culture),
            "short" => dto.ToString("MMM", culture),
            "narrow" => dto.ToString("MMMM", culture)[..1],
            "2-digit" => dto.Month.ToString("D2", CultureInfo.InvariantCulture),
            _ => dto.Month.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatDay(DateTimeOffset dto, string width)
    {
        return width switch
        {
            "2-digit" => dto.Day.ToString("D2", CultureInfo.InvariantCulture),
            _ => dto.Day.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatHour(DateTimeOffset dto, string width, DateTimeFormatInternalSlots slots)
    {
        int hour;
        if (slots.HourCycle is "h11")
        {
            hour = dto.Hour % 12;
        }
        else if (slots.HourCycle is "h12")
        {
            hour = dto.Hour % 12;
            if (hour == 0)
            {
                hour = 12;
            }
        }
        else if (slots.HourCycle is "h24")
        {
            hour = dto.Hour == 0 ? 24 : dto.Hour;
        }
        else
        {
            hour = dto.Hour;
        }

        return width switch
        {
            "2-digit" => hour.ToString("D2", CultureInfo.InvariantCulture),
            _ => hour.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatMinute(DateTimeOffset dto, string width)
    {
        return width switch
        {
            "2-digit" => dto.Minute.ToString("D2", CultureInfo.InvariantCulture),
            _ => dto.Minute.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatSecond(DateTimeOffset dto, string width)
    {
        return width switch
        {
            "2-digit" => dto.Second.ToString("D2", CultureInfo.InvariantCulture),
            _ => dto.Second.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string GetDayPeriod(DateTimeOffset dto, string width)
    {
        var hour = dto.Hour;

        // Per CLDR day period rules for English
        // midnight = 0, morning = 6-12, noon = 12, afternoon = 12-18, evening = 18-21, night = 21-6
        return width switch
        {
            "narrow" => GetDayPeriodNarrow(hour),
            "short" => GetDayPeriodShort(hour),
            "long" => GetDayPeriodLong(hour),
            _ => hour < 12 ? "AM" : "PM"
        };
    }

    private static string GetDayPeriodNarrow(int hour)
    {
        if (hour == 0)
        {
            return "at night";
        }

        if (hour == 12)
        {
            return "n";
        }

        return hour switch
        {
            >= 1 and < 6 => "at night",
            >= 6 and < 12 => "in the morning",
            > 12 and < 18 => "in the afternoon",
            >= 18 and < 21 => "in the evening",
            _ => "at night"
        };
    }

    private static string GetDayPeriodShort(int hour)
    {
        if (hour == 0)
        {
            return "at night";
        }

        if (hour == 12)
        {
            return "noon";
        }

        return hour switch
        {
            >= 1 and < 6 => "at night",
            >= 6 and < 12 => "in the morning",
            > 12 and < 18 => "in the afternoon",
            >= 18 and < 21 => "in the evening",
            _ => "at night"
        };
    }

    private static string GetDayPeriodLong(int hour)
    {
        if (hour == 0)
        {
            return "at night";
        }

        if (hour == 12)
        {
            return "noon";
        }

        return hour switch
        {
            >= 1 and < 6 => "at night",
            >= 6 and < 12 => "in the morning",
            > 12 and < 18 => "in the afternoon",
            >= 18 and < 21 => "in the evening",
            _ => "at night"
        };
    }

    private static string GetTimeZoneDisplay(string timeZoneId, DateTimeOffset dto, bool longForm)
    {
        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return longForm ? "Coordinated Universal Time" : "UTC";
        }

        // Handle offset timezone IDs like +03:00, -07:00
        if (timeZoneId.Length > 0 && (timeZoneId[0] == '+' || timeZoneId[0] == '-'))
        {
            if (TryParseTimeZoneOffset(timeZoneId, out var offset))
            {
                var sign = offset >= TimeSpan.Zero ? "+" : "";
                return $"GMT{sign}{offset.Hours:D2}:{offset.Minutes:D2}";
            }

            return $"GMT{timeZoneId}";
        }

        try
        {
            if (!IntlUtilities.TryResolveTimeZoneId(timeZoneId, out var resolvedTimeZoneId))
            {
                return timeZoneId;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(resolvedTimeZoneId);
            if (longForm)
            {
                var longName = tz.IsDaylightSavingTime(dto) ? tz.DaylightName : tz.StandardName;
                if (!string.IsNullOrWhiteSpace(longName) &&
                    !longName.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
                {
                    return longName;
                }

                var ianaLongName = GetIanaTimeZoneLongName(timeZoneId, dto, tz);
                if (ianaLongName is not null)
                {
                    return ianaLongName;
                }

                return longName;
            }

            var offset = tz.GetUtcOffset(dto);
            return $"GMT{(offset >= TimeSpan.Zero ? "+" : "")}{offset.Hours:D2}:{offset.Minutes:D2}";
        }
        catch
        {
            return timeZoneId;
        }
    }

    private static string? GetIanaTimeZoneLongName(string timeZoneId, DateTimeOffset dto, TimeZoneInfo tz)
    {
        return timeZoneId switch
        {
            "Europe/Vienna" or "Europe/Berlin" or "Europe/Paris" or "Europe/Prague" or "Europe/Rome" =>
                tz.IsDaylightSavingTime(dto) ? "Central European Summer Time" : "Central European Standard Time",
            _ => null
        };
    }

    private JsArray FormatToPartsInternal(DateTimeOffset dto, DateTimeFormatInternalSlots slots, CultureInfo culture)
    {
        var parts = new JsArray(Realm);
        var components = slots.Components;

        if (slots.DateStyle is not null || slots.TimeStyle is not null)
        {
            FormatStyleToParts(dto, slots, culture, parts);
            return parts;
        }

        if (components.Count == 0)
        {
            var formatted = FormatDefault(dto, culture);
            parts.Push(CreateTypedPart("literal", formatted));
            return parts;
        }

        var addedSomething = false;

        if (components.TryGetValue("weekday", out var weekday))
        {
            AddSeparatorPart(parts, ref addedSomething);
            parts.Push(CreateTypedPart("weekday", FormatWeekday(dto, weekday, culture)));
        }

        if (components.TryGetValue("era", out var era))
        {
            AddSeparatorPart(parts, ref addedSomething);
            parts.Push(CreateTypedPart("era", FormatEra(dto, era)));
        }

        // Date components (year/month/day) - locale-aware ordering and separators
        var hasYear = components.TryGetValue("year", out var year);
        var hasMonth = components.TryGetValue("month", out var month);
        var hasDay = components.TryGetValue("day", out var day);

        if (hasYear || hasMonth || hasDay)
        {
            AppendLocaleDateParts(parts, ref addedSomething, dto, culture,
                hasYear, year, hasMonth, month, hasDay, day);
        }

        if (components.TryGetValue("dayPeriod", out var dayPeriod) && !components.ContainsKey("hour"))
        {
            AddSeparatorPart(parts, ref addedSomething);
            parts.Push(CreateTypedPart("dayPeriod", GetDayPeriod(dto, dayPeriod)));
        }

        // Determine pending day period but defer until after time components
        string? pendingDayPeriodParts = null;
        if (components.TryGetValue("hour", out var hour))
        {
            AddSeparatorPart(parts, ref addedSomething);
            parts.Push(CreateTypedPart("hour", FormatHour(dto, hour, slots)));

            if (components.TryGetValue("dayPeriod", out var dp))
            {
                pendingDayPeriodParts = GetDayPeriod(dto, dp);
            }
            else if (slots.HourCycle is "h11" or "h12")
            {
                pendingDayPeriodParts = dto.Hour < 12 ? "AM" : "PM";
            }
        }

        var forceMinSecPadParts = components.ContainsKey("minute") && components.ContainsKey("second");

        if (components.TryGetValue("minute", out var minute))
        {
            if (components.ContainsKey("hour"))
            {
                parts.Push(CreateTypedPart("literal", ":"));
            }
            else
            {
                AddSeparatorPart(parts, ref addedSomething);
            }

            parts.Push(CreateTypedPart("minute",
                forceMinSecPadParts ? FormatMinute(dto, "2-digit") : FormatMinute(dto, minute)));
        }

        if (components.TryGetValue("second", out var second))
        {
            if (components.ContainsKey("minute"))
            {
                parts.Push(CreateTypedPart("literal", ":"));
            }
            else
            {
                AddSeparatorPart(parts, ref addedSomething);
            }

            parts.Push(CreateTypedPart("second",
                forceMinSecPadParts ? FormatSecond(dto, "2-digit") : FormatSecond(dto, second)));
        }

        if (components.TryGetValue("fractionalSecondDigits", out var fsdStr) &&
            int.TryParse(fsdStr, CultureInfo.InvariantCulture, out var fsd))
        {
            parts.Push(CreateTypedPart("literal", IntlUtilities.GetDecimalSeparator(slots.NumberingSystem)));
            var ms = dto.Millisecond;
            var formatted = ms.ToString("D3", CultureInfo.InvariantCulture);
            parts.Push(CreateTypedPart("fractionalSecond", formatted[..fsd]));
        }

        // Append AM/PM after all time components
        if (pendingDayPeriodParts is not null)
        {
            parts.Push(CreateTypedPart("literal", " "));
            parts.Push(CreateTypedPart("dayPeriod", pendingDayPeriodParts));
        }

        if (components.TryGetValue("timeZoneName", out var tzName))
        {
            AddSeparatorPart(parts, ref addedSomething);
            parts.Push(CreateTypedPart("timeZoneName",
                GetTimeZoneDisplay(slots.TimeZone, dto, tzName is "long")));
        }

        return parts;
    }

    private static void FormatStyleToParts(DateTimeOffset dto, DateTimeFormatInternalSlots slots, CultureInfo culture,
        JsArray parts)
    {
        var use12 = slots.HourCycle is "h11" or "h12";
        var addedSomething = false;

        // Date parts
        if (slots.DateStyle is not null)
        {
            if (slots.DateStyle is "full")
            {
                parts.Push(CreateTypedPart("weekday", dto.ToString("dddd", culture)));
                parts.Push(CreateTypedPart("literal", ", "));
            }

            parts.Push(CreateTypedPart("month",
                slots.DateStyle is "short"
                    ? dto.Month.ToString(CultureInfo.InvariantCulture)
                    : slots.DateStyle is "medium"
                        ? dto.ToString("MMM", culture)
                        : dto.ToString("MMMM", culture)));
            parts.Push(CreateTypedPart("literal", " "));
            parts.Push(CreateTypedPart("day", dto.Day.ToString(CultureInfo.InvariantCulture)));
            parts.Push(CreateTypedPart("literal", ", "));
            parts.Push(CreateTypedPart("year",
                slots.DateStyle is "short"
                    ? (dto.Year % 100).ToString("D2", CultureInfo.InvariantCulture)
                    : dto.Year.ToString(CultureInfo.InvariantCulture)));
            addedSomething = true;
        }

        // Time parts
        if (slots.TimeStyle is not null)
        {
            if (addedSomething)
            {
                parts.Push(CreateTypedPart("literal", ", "));
            }

            int hour;
            if (slots.HourCycle is "h11")
            {
                hour = dto.Hour % 12;
            }
            else if (slots.HourCycle is "h12")
            {
                hour = dto.Hour % 12;
                if (hour == 0)
                {
                    hour = 12;
                }
            }
            else if (slots.HourCycle is "h24")
            {
                hour = dto.Hour == 0 ? 24 : dto.Hour;
            }
            else
            {
                hour = dto.Hour;
            }

            parts.Push(CreateTypedPart("hour", hour.ToString(CultureInfo.InvariantCulture)));
            parts.Push(CreateTypedPart("literal", ":"));
            parts.Push(CreateTypedPart("minute",
                dto.Minute.ToString("D2", CultureInfo.InvariantCulture)));

            if (slots.TimeStyle is "full" or "long" or "medium")
            {
                parts.Push(CreateTypedPart("literal", ":"));
                parts.Push(CreateTypedPart("second",
                    dto.Second.ToString("D2", CultureInfo.InvariantCulture)));
            }

            if (use12)
            {
                parts.Push(CreateTypedPart("literal", " "));
                parts.Push(CreateTypedPart("dayPeriod", dto.Hour < 12 ? "AM" : "PM"));
            }

            if (slots.TimeStyle is "full" or "long")
            {
                parts.Push(CreateTypedPart("literal", " "));
                parts.Push(CreateTypedPart("timeZoneName",
                    GetTimeZoneDisplay(slots.TimeZone, dto, slots.TimeStyle is "full")));
            }
        }
    }

    private static JsObject CreateTypedPart(string type, string value)
    {
        var obj = new JsObject();
        obj.SetProperty("type", new JsValue(type));
        obj.SetProperty("value", new JsValue(value));
        return obj;
    }

    private static void AddSeparatorPart(JsArray parts, ref bool addedSomething)
    {
        if (addedSomething)
        {
            parts.Push(CreateTypedPart("literal", ", "));
        }

        addedSomething = true;
    }

    private static void AppendLocaleDateParts(
        JsArray parts, ref bool addedSomething,
        DateTimeOffset dto, CultureInfo culture,
        bool hasYear, string? yearWidth,
        bool hasMonth, string? monthWidth,
        bool hasDay, string? dayWidth)
    {
        var (order, numericSep) = ParseLocaleDateOrder(culture);
        var namedMonth = monthWidth is "long" or "short" or "narrow";

        // Build ordered list of present components
        var componentList = new List<(char ch, string type, string value)>();
        foreach (var c in GetDateComponentChars(order))
        {
            switch (c)
            {
                case 'M' when hasMonth:
                    componentList.Add(('M', "month", FormatMonth(dto, monthWidth, culture)));
                    break;
                case 'd' when hasDay:
                    componentList.Add(('d', "day", FormatDay(dto, dayWidth)));
                    break;
                case 'y' when hasYear:
                    componentList.Add(('y', "year", FormatYear(dto, yearWidth)));
                    break;
            }
        }

        for (var i = 0; i < componentList.Count; i++)
        {
            if (i == 0)
            {
                // Separator from previous non-date components (weekday, era)
                if (addedSomething)
                {
                    parts.Push(CreateTypedPart("literal", ", "));
                }
            }
            else
            {
                // Separator between date components
                if (namedMonth)
                {
                    var sep = GetNamedMonthSeparator(order,
                        componentList.ConvertAll(x => (x.ch, x.value)), i);
                    parts.Push(CreateTypedPart("literal", sep));
                }
                else
                {
                    parts.Push(CreateTypedPart("literal", numericSep));
                }
            }

            parts.Push(CreateTypedPart(componentList[i].type, componentList[i].value));
            addedSomething = true;
        }
    }

    private static DateTimeOffset ToDateTimeOffset(double epochMs, string timeZoneId)
    {
        var truncated = Math.Truncate(epochMs);
        var longMs = (long)truncated;

        // .NET DateTimeOffset range is narrower than ECMAScript time range
        // Clamp to .NET's supported range
        const long dotNetMinMs = -62135596800000L;
        const long dotNetMaxMs = 253402300799999L;
        longMs = Math.Clamp(longMs, dotNetMinMs, dotNetMaxMs);

        var dto = DateTimeOffset.FromUnixTimeMilliseconds(longMs);

        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return dto;
        }

        // Handle offset timezones like +03:00, -07:00
        if (timeZoneId.Length > 0 && (timeZoneId[0] == '+' || timeZoneId[0] == '-'))
        {
            if (TryParseTimeZoneOffset(timeZoneId, out var offset))
            {
                // .NET only supports ±14h offsets in DateTimeOffset.ToOffset
                // For larger offsets, manually adjust the UTC time
                var totalMinutes = (int)offset.TotalMinutes;
                var utcTicks = dto.UtcTicks;
                var adjustedTicks = utcTicks + TimeSpan.FromMinutes(totalMinutes).Ticks;

                // Clamp to valid DateTimeOffset range
                if (adjustedTicks < DateTime.MinValue.Ticks)
                {
                    adjustedTicks = DateTime.MinValue.Ticks;
                }
                else if (adjustedTicks > DateTime.MaxValue.Ticks)
                {
                    adjustedTicks = DateTime.MaxValue.Ticks;
                }

                // Use a safe offset for the DateTimeOffset constructor (clamp to ±14h)
                var safeOffset = offset;
                if (offset.TotalHours > 14 || offset.TotalHours < -14)
                {
                    safeOffset = TimeSpan.Zero;
                }

                try
                {
                    return new DateTimeOffset(new DateTime(adjustedTicks, DateTimeKind.Unspecified), safeOffset);
                }
                catch
                {
                    return dto;
                }
            }

            return dto;
        }

        try
        {
            if (!IntlUtilities.TryResolveTimeZoneId(timeZoneId, out var resolvedTimeZoneId))
            {
                return dto;
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(resolvedTimeZoneId);
            return TimeZoneInfo.ConvertTime(dto, tz);
        }
        catch
        {
            return dto;
        }
    }

    private static bool TryParseTimeZoneOffset(string timeZoneId, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrEmpty(timeZoneId) || (timeZoneId[0] != '+' && timeZoneId[0] != '-'))
        {
            return false;
        }

        var sign = timeZoneId[0] == '+' ? 1 : -1;
        var rest = timeZoneId.AsSpan(1);

        int hours, minutes;
        if (rest.Length == 5 && rest[2] == ':' &&
            int.TryParse(rest[..2], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out hours) &&
            int.TryParse(rest[3..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out minutes))
        {
            offset = new TimeSpan(sign * hours, sign * minutes, 0);
            return true;
        }

        if (rest.Length == 2 && int.TryParse(rest, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out hours))
        {
            offset = new TimeSpan(sign * hours, 0, 0);
            return true;
        }

        return false;
    }

    private static double ToEpochMilliseconds(JsValue value)
    {
        if (value.IsNullOrUndefined)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (value is { Kind: JsValueKind.Object, ObjectValue: JsObject jsObject })
        {
            // Check for Date internal slot
            if (jsObject.TryGetProperty("_internalDate", out var stored) && stored.TryGetDouble(out var storedMs))
            {
                return TimeClip(storedMs);
            }

            // Detect Temporal types — valueOf() throws for these, so we must extract epoch ms directly.
            // Per ECMA-402: HandleDateTimeTemporalDate / HandleDateTimeValue
            if (jsObject.TryGetProperty("[[TemporalInstant]]", out var instantSlot) &&
                instantSlot.TryGetObject<JsTemporalInstant>(out var instant))
            {
                return TimeClip(instant.EpochMilliseconds);
            }

            // NOTE: ZonedDateTime is intentionally NOT handled here.
            // Per spec, DateTimeFormat.format(zonedDateTime) must throw TypeError.
            // ZDT formatting is handled by Temporal.ZonedDateTime.prototype.toLocaleString()
            // which converts to Instant before delegating to DateTimeFormat.

            if (jsObject.TryGetProperty("[[TemporalPlainDateTime]]", out var pdtSlot) &&
                pdtSlot.TryGetObject<JsTemporalPlainDateTime>(out var pdt))
            {
                // Treat as UTC midnight
                var dto = new DateTimeOffset(pdt.Year, pdt.Month, pdt.Day,
                    pdt.Hour, pdt.Minute, pdt.Second, pdt.Millisecond, TimeSpan.Zero);
                return TimeClip(dto.ToUnixTimeMilliseconds());
            }

            if (jsObject.TryGetProperty("[[TemporalPlainDate]]", out var pdSlot) &&
                pdSlot.TryGetObject<JsTemporalPlainDate>(out var pd))
            {
                // Treat as midnight UTC on that date
                var dto = new DateTimeOffset(pd.Year, pd.Month, pd.Day, 0, 0, 0, TimeSpan.Zero);
                return TimeClip(dto.ToUnixTimeMilliseconds());
            }

            if (jsObject.TryGetProperty("[[TemporalPlainTime]]", out var ptSlot) &&
                ptSlot.TryGetObject<JsTemporalPlainTime>(out var pt))
            {
                // Treat as today's date + time in UTC
                var today = DateTimeOffset.UtcNow.Date;
                var dto = new DateTimeOffset(today.Year, today.Month, today.Day,
                    pt.Hour, pt.Minute, pt.Second, pt.Millisecond, TimeSpan.Zero);
                return TimeClip(dto.ToUnixTimeMilliseconds());
            }

            if (jsObject.TryGetProperty("[[TemporalPlainYearMonth]]", out var ymSlot) &&
                ymSlot.TryGetObject<JsTemporalPlainYearMonth>(out var ym))
            {
                // Treat as first of month midnight UTC
                var dto = new DateTimeOffset(ym.Year, ym.Month, 1, 0, 0, 0, TimeSpan.Zero);
                return TimeClip(dto.ToUnixTimeMilliseconds());
            }

            if (jsObject.TryGetProperty("[[TemporalPlainMonthDay]]", out var mdSlot) &&
                mdSlot.TryGetObject<JsTemporalPlainMonthDay>(out var md))
            {
                // Use reference year from the month-day object (internal, but accessible within assembly)
                var dto = new DateTimeOffset(md.ReferenceYear, md.Month, md.Day, 0, 0, 0, TimeSpan.Zero);
                return TimeClip(dto.ToUnixTimeMilliseconds());
            }
        }

        try
        {
            var number = JsOps.ToNumber(value);
            return TimeClip(number);
        }
        catch (ThrowSignal)
        {
            throw; // Propagate JS throw signals (valueOf/toString/Symbol.toPrimitive exceptions)
        }
        catch
        {
            return double.NaN;
        }
    }

    /// <summary>
    /// ECMAScript TimeClip(time) - 20.4.1.15
    /// </summary>
    private static double TimeClip(double time)
    {
        if (!double.IsFinite(time))
        {
            return double.NaN;
        }

        if (Math.Abs(time) > 8.64e15)
        {
            return double.NaN;
        }

        return Math.Truncate(time);
    }
}
