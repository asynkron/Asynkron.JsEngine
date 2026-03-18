namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Shared helpers for the ISO 8601 proleptic Gregorian calendar.
///     Works for all years including negative (extended) years.
/// </summary>
internal static class IsoCalendarHelpers
{
    /// <summary>
    ///     Returns whether the given year is a leap year in the proleptic Gregorian calendar.
    /// </summary>
    public static bool IsLeapYear(int year)
    {
        if (year % 4 != 0)
        {
            return false;
        }
        if (year % 100 != 0)
        {
            return true;
        }
        return year % 400 == 0;
    }

    /// <summary>
    ///     Returns the number of days in the given month for the given year.
    /// </summary>
    public static int DaysInMonth(int year, int month)
    {
        return month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => IsLeapYear(year) ? 29 : 28,
            _ => throw new ArgumentOutOfRangeException(nameof(month))
        };
    }

    /// <summary>
    ///     Converts a proleptic Gregorian calendar date to Unix epoch days.
    ///     Uses the Howard Hinnant civil_from_days algorithm.
    ///     Works for all years including negative (extended) years.
    /// </summary>
    public static long DateToEpochDays(int year, int month, int day)
    {
        // Shift year so that March is the first month
        var y = month <= 2 ? (long)year - 1 : year;
        var m = month <= 2 ? month + 9 : month - 3;

        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - era * 400; // year of era [0, 399]
        var doy = (153 * m + 2) / 5 + day - 1; // day of year [0, 365]
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy; // day of era [0, 146096]

        return era * 146097 + doe - 719468; // adjust to Unix epoch (1970-01-01)
    }

    /// <summary>
    ///     Converts Unix epoch days to a proleptic Gregorian calendar date.
    ///     Uses the Howard Hinnant civil_from_days algorithm.
    ///     Works for all years including negative (extended) years.
    /// </summary>
    public static (int Year, int Month, int Day) EpochDaysToDate(long epochDays)
    {
        var z = epochDays + 719468;
        var era = (z >= 0 ? z : z - 146096) / 146097;
        var doe = z - era * 146097;
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        var y = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        var d = doy - (153 * mp + 2) / 5 + 1;
        var m = mp + (mp < 10 ? 3 : -9);
        return ((int)(y + (m <= 2 ? 1 : 0)), (int)m, (int)d);
    }
}
