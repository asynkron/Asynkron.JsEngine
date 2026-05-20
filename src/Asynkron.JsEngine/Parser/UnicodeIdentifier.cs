using System.Text;
using Asynkron.JsEngine.StdLib.RegExp;

namespace Asynkron.JsEngine.Parser;

internal static class UnicodeIdentifier
{
    private static readonly (int Start, int End)[] IdStartRanges = UnicodePropertyData.Resolve("ID_Start") ?? [];
    private static readonly (int Start, int End)[] IdContinueRanges = UnicodePropertyData.Resolve("ID_Continue") ?? [];
    private static readonly (int Start, int End)[] OtherIdStartRanges = UnicodePropertyData.Resolve("Other_ID_Start") ?? [];
    private static readonly (int Start, int End)[] OtherIdContinueRanges = UnicodePropertyData.Resolve("Other_ID_Continue") ?? [];

    public static bool IsIdentifierStart(Rune rune)
    {
        return rune.Value is '$' or '_' ||
               Contains(IdStartRanges, rune.Value) ||
               Contains(OtherIdStartRanges, rune.Value);
    }

    public static bool IsIdentifierPart(Rune rune)
    {
        return rune.Value is '$' or 0x200C or 0x200D ||
               Contains(IdContinueRanges, rune.Value) ||
               Contains(OtherIdContinueRanges, rune.Value);
    }

    private static bool Contains((int Start, int End)[] ranges, int codePoint)
    {
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var range = ranges[mid];
            if (codePoint < range.Start)
            {
                hi = mid - 1;
            }
            else if (codePoint > range.End)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
