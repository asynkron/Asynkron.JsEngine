using System.Globalization;
using System.Text;

namespace Asynkron.JsEngine.Parser;

internal static class UnicodeIdentifier
{
    public static bool IsIdentifierStart(Rune rune)
    {
        if (rune.Value is '$' or '_')
        {
            return true;
        }

        if (IsOtherIdStart(rune.Value))
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter => true,
            UnicodeCategory.LowercaseLetter => true,
            UnicodeCategory.TitlecaseLetter => true,
            UnicodeCategory.ModifierLetter => true,
            UnicodeCategory.OtherLetter => true,
            UnicodeCategory.LetterNumber => true,
            _ => false
        };
    }

    public static bool IsIdentifierPart(Rune rune)
    {
        if (rune.Value is '$' or 0x200C or 0x200D)
        {
            return true;
        }

        if (IsIdentifierStart(rune))
        {
            return true;
        }

        if (IsOtherIdContinue(rune.Value))
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            UnicodeCategory.DecimalDigitNumber => true,
            UnicodeCategory.ConnectorPunctuation => true,
            _ => false
        };
    }

    private static bool IsOtherIdStart(int codePoint)
    {
        return codePoint switch
        {
            0x1885 => true,
            0x1886 => true,
            0x2118 => true,
            0x212E => true,
            0x309B => true,
            0x309C => true,
            _ => false
        };
    }

    private static bool IsOtherIdContinue(int codePoint)
    {
        return codePoint is
            0x00B7 or
            0x0387 or
            0x1369 or
            0x136A or
            0x136B or
            0x136C or
            0x136D or
            0x136E or
            0x136F or
            0x1370 or
            0x1371 or
            0x19DA or
            0x30FB or
            0xFF65;
    }
}
