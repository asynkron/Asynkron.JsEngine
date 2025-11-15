using System;

namespace Asynkron.JsEngine;

internal static class JsNumericConversions
{
    private const double TwoTo32 = 4294967296d;
    private const double TwoTo31 = 2147483648d;

    public static int ToInt32(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number == 0d)
        {
            return 0;
        }

        var sign = number < 0 ? -1 : 1;
        var abs = Math.Abs(number);
        var integer = Math.Floor(abs);
        var modulo = integer % TwoTo32;

        if (sign < 0)
        {
            modulo = TwoTo32 - modulo;
            if (modulo == TwoTo32)
            {
                modulo = 0;
            }
        }

        if (modulo >= TwoTo31)
        {
            return (int)(modulo - TwoTo32);
        }

        return (int)modulo;
    }

    public static uint ToUInt32(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number == 0d)
        {
            return 0u;
        }

        var sign = number < 0 ? -1 : 1;
        var abs = Math.Abs(number);
        var integer = Math.Floor(abs);
        var modulo = integer % TwoTo32;

        if (sign < 0)
        {
            modulo = TwoTo32 - modulo;
            if (modulo == TwoTo32)
            {
                modulo = 0;
            }
        }

        return (uint)modulo;
    }
}
