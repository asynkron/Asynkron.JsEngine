#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Segmenter", ToStringTag = "Intl.Segmenter")]
public sealed partial class IntlSegmenterPrototype
{
    private const string BrandKey = "__segmenter__";
    private const string LocaleSlot = "__segmenter_locale__";
    private const string GranularitySlot = "__segmenter_granularity__";

    internal static void InitializeInternalSlots(JsObject instance, string locale, string granularity)
    {
        instance.SetProperty(BrandKey, new JsValue(true));
        instance.SetProperty(LocaleSlot, new JsValue(locale));
        instance.SetProperty(GranularitySlot, new JsValue(granularity));
    }

    [JsHostMethod("segment", Length = 1d)]
    private JsValue Segment(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var stringArg = args.GetArgument(0);
        var str = JsValueToString(stringArg, Realm);

        var granularity = GetStringSlot(instance, GranularitySlot, "grapheme");

        return new JsValue(CreateSegmentsObject(str, granularity, Realm));
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.Segmenter.prototype.resolvedOptions";

        var locale = GetStringSlot(instance, LocaleSlot, "en");
        var granularity = GetStringSlot(instance, GranularitySlot, "grapheme");

        CreateDataPropertyOrThrow(obj, "locale", locale, Realm, operation);
        CreateDataPropertyOrThrow(obj, "granularity", granularity, Realm, operation);

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue candidate)
    {
        return candidate.EnsureBrand(BrandKey, Realm, "Intl.Segmenter method called on incompatible receiver");
    }

    private static string GetStringSlot(JsObject instance, string key, string defaultValue)
    {
        return instance.TryGetProperty(key, out var value) && value.TryGetString(out var str)
            ? str
            : defaultValue;
    }

    private const string SegmentsBrandKey = "__segments__";

    internal static JsObject CreateSegmentsObject(string input, string granularity, RealmState realm)
    {
        var segments = new JsObject(realm.ObjectPrototype);

        // Brand the Segments object for receiver validation in containing()
        segments.SetProperty(SegmentsBrandKey, new JsValue(true));

        // Store internal state
        segments.SetProperty("__segments_input__", new JsValue(input));
        segments.SetProperty("__segments_granularity__", new JsValue(granularity));

        // containing() method
        var containingFn = new HostFunction((thisValue, containingArgs) =>
        {
            // RequireInternalSlot(segments, [[SegmentsSegmenter]])
            if (!thisValue.IsObject || !thisValue.TryGetObject<IJsPropertyAccessor>(out var receiver) ||
                !receiver.TryGetProperty(SegmentsBrandKey, out var brand) || !JsOps.ToBoolean(brand))
            {
                throw ThrowTypeError(
                    "Intl.Segmenter.prototype.segment().containing called on incompatible receiver",
                    realm: realm);
            }

            var indexArg = containingArgs.GetArgument(0);
            var index = StandardLibrary.ToIntegerOrInfinity(indexArg);

            if (index < 0 || index >= input.Length)
            {
                return JsValue.Undefined;
            }

            var idx = (int)index;
            return new JsValue(FindSegmentAt(input, granularity, idx, realm));
        }, realm, false);

        containingFn.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        containingFn.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "containing",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        containingFn.Delete("prototype");
        segments.DefineProperty("containing",
            new PropertyDescriptor { Value = containingFn, Writable = true, Enumerable = false, Configurable = true });

        // [Symbol.iterator]() method
        var iteratorFn = new HostFunction((_, _) =>
        {
            return new JsValue(CreateSegmentIterator(input, granularity, realm));
        }, realm, false);

        iteratorFn.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        iteratorFn.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "[Symbol.iterator]",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        iteratorFn.Delete("prototype");
        segments.DefineProperty(SymbolKeys.Iterator,
            new PropertyDescriptor
            {
                Value = iteratorFn,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        return segments;
    }

    internal static JsObject FindSegmentAt(string input, string granularity, int position, RealmState realm)
    {
        var boundaries = ComputeBoundaries(input, granularity);

        // Find the segment that contains the position
        var segStart = 0;
        var segEnd = input.Length;

        for (var i = 0; i < boundaries.Count; i++)
        {
            if (i + 1 < boundaries.Count)
            {
                if (position >= boundaries[i] && position < boundaries[i + 1])
                {
                    segStart = boundaries[i];
                    segEnd = boundaries[i + 1];
                    break;
                }
            }
            else
            {
                segStart = boundaries[i];
                segEnd = input.Length;
            }
        }

        var segmentText = input[segStart..segEnd];
        return CreateSegmentDataObject(segmentText, segStart, input, granularity, realm);
    }

    internal static JsObject CreateSegmentDataObject(string segment, int index, string input, string granularity,
        RealmState realm)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        const string operation = "Intl.Segmenter segment data";

        CreateDataPropertyOrThrow(obj, "segment", segment, realm, operation);
        CreateDataPropertyOrThrow(obj, "index", (double)index, realm, operation);
        CreateDataPropertyOrThrow(obj, "input", input, realm, operation);

        if (string.Equals(granularity, "word", StringComparison.Ordinal))
        {
            var isWordLike = IsWordLike(segment);
            CreateDataPropertyOrThrow(obj, "isWordLike", isWordLike, realm, operation);
        }

        return obj;
    }

    internal static JsObject CreateSegmentIterator(string input, string granularity, RealmState realm)
    {
        var boundaries = ComputeBoundaries(input, granularity);
        var currentIndex = 0;

        var iterator = new JsObject(realm.IteratorPrototype ?? realm.ObjectPrototype);

        var nextFn = new HostFunction((_, _) =>
        {
            if (currentIndex >= boundaries.Count - 1)
            {
                var done = new JsObject(realm.ObjectPrototype);
                CreateDataPropertyOrThrow(done, "value", JsValue.Undefined, realm, "Segment Iterator");
                CreateDataPropertyOrThrow(done, "done", true, realm, "Segment Iterator");
                return new JsValue(done);
            }

            var segStart = boundaries[currentIndex];
            var segEnd = boundaries[currentIndex + 1];
            var segmentText = input[segStart..segEnd];
            var segmentData = CreateSegmentDataObject(segmentText, segStart, input, granularity, realm);

            currentIndex++;

            var result = new JsObject(realm.ObjectPrototype);
            CreateDataPropertyOrThrow(result, "value", new JsValue(segmentData), realm, "Segment Iterator");
            CreateDataPropertyOrThrow(result, "done", false, realm, "Segment Iterator");
            return new JsValue(result);
        }, realm, false);

        nextFn.DefineProperty("length",
            new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });
        nextFn.DefineProperty("name",
            new PropertyDescriptor { Value = "next", Writable = false, Enumerable = false, Configurable = true });
        nextFn.Delete("prototype");

        iterator.DefineProperty("next",
            new PropertyDescriptor { Value = nextFn, Writable = true, Enumerable = false, Configurable = true });

        // @@toStringTag
        iterator.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor
            {
                Value = "Segmenter String Iterator",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

        return iterator;
    }

    /// <summary>
    /// Computes segment boundary positions for the given input and granularity.
    /// Returns a list of indices where segments start/end. Always starts with 0 and ends with input.Length.
    /// </summary>
    internal static List<int> ComputeBoundaries(string input, string granularity)
    {
        if (input.Length == 0)
        {
            return [0];
        }

        return granularity switch
        {
            "grapheme" => ComputeGraphemeBoundaries(input),
            "word" => ComputeWordBoundaries(input),
            "sentence" => ComputeSentenceBoundaries(input),
            _ => ComputeGraphemeBoundaries(input)
        };
    }

    private static List<int> ComputeGraphemeBoundaries(string input)
    {
        var boundaries = new List<int> { 0 };
        var si = new StringInfo(input);
        var offset = 0;

        for (var i = 0; i < si.LengthInTextElements; i++)
        {
            var element = si.SubstringByTextElements(i, 1);
            offset += element.Length;
            boundaries.Add(offset);
        }

        if (boundaries[^1] != input.Length)
        {
            boundaries.Add(input.Length);
        }

        // Post-process: merge emoji modifier sequences, ZWJ sequences, and Jamo clusters
        // that StringInfo may have incorrectly split
        return MergeExtendedGraphemeClusters(input, boundaries);
    }

    /// <summary>
    /// Merges boundaries that incorrectly split emoji modifier sequences, ZWJ sequences,
    /// and Korean Jamo combining sequences.
    /// </summary>
    private static List<int> MergeExtendedGraphemeClusters(string input, List<int> boundaries)
    {
        if (boundaries.Count <= 2)
        {
            return boundaries;
        }

        var merged = new List<int> { 0 };

        for (var i = 1; i < boundaries.Count; i++)
        {
            var pos = boundaries[i];

            // Check if we should merge this boundary with the previous one
            if (pos < input.Length && ShouldMergeAtBoundary(input, pos))
            {
                // Skip this boundary (merge with previous segment)
                continue;
            }

            merged.Add(pos);
        }

        if (merged[^1] != input.Length)
        {
            merged.Add(input.Length);
        }

        return merged;
    }

    private static bool ShouldMergeAtBoundary(string input, int pos)
    {
        if (pos <= 0 || pos >= input.Length)
        {
            return false;
        }

        var codePointAtPos = GetCodePointAt(input, pos);

        // Emoji modifier (skin tone): U+1F3FB..U+1F3FF
        if (codePointAtPos >= 0x1F3FB && codePointAtPos <= 0x1F3FF)
        {
            return true;
        }

        // ZWJ (U+200D): merge ZWJ and the following character with the previous
        if (codePointAtPos == 0x200D)
        {
            return true;
        }

        // After ZWJ: if the previous character(s) end with ZWJ, merge
        var prevCodePoint = GetCodePointBefore(input, pos);
        if (prevCodePoint == 0x200D)
        {
            return true;
        }

        // Variation selectors: U+FE00..U+FE0F (text/emoji presentation)
        if (codePointAtPos >= 0xFE00 && codePointAtPos <= 0xFE0F)
        {
            return true;
        }

        // Korean Jamo combining rules:
        // L (Leading) = U+1100..U+115F, U+A960..U+A97C
        // V (Vowel)   = U+1160..U+11A7, U+D7B0..U+D7C6
        // T (Trailing) = U+11A8..U+11FF, U+D7CB..U+D7FB
        if (IsJamoV(codePointAtPos) && IsJamoL(prevCodePoint))
        {
            return true; // L + V
        }

        if (IsJamoV(codePointAtPos) && IsJamoV(prevCodePoint))
        {
            return true; // V + V
        }

        if (IsJamoT(codePointAtPos) && (IsJamoV(prevCodePoint) || IsJamoT(prevCodePoint)))
        {
            return true; // V + T or T + T
        }

        if (IsJamoL(codePointAtPos) && IsJamoL(prevCodePoint))
        {
            return true; // L + L
        }

        // Combining marks following emoji: NonSpacingMark, SpacingCombiningMark, EnclosingMark
        var cat = char.GetUnicodeCategory(input, pos);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark)
        {
            // Only merge with emoji/special chars, not with regular ASCII
            var prevCp = GetCodePointBefore(input, pos);
            if (prevCp > 0x7F) // non-ASCII predecessor
            {
                return true;
            }
        }

        // Regional indicator pairs (flags): U+1F1E6..U+1F1FF
        if (codePointAtPos >= 0x1F1E6 && codePointAtPos <= 0x1F1FF)
        {
            if (prevCodePoint >= 0x1F1E6 && prevCodePoint <= 0x1F1FF)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetCodePointAt(string input, int pos)
    {
        if (pos >= input.Length)
        {
            return -1;
        }

        return char.IsSurrogatePair(input, pos)
            ? char.ConvertToUtf32(input, pos)
            : input[pos];
    }

    private static int GetCodePointBefore(string input, int pos)
    {
        if (pos <= 0)
        {
            return -1;
        }

        if (pos >= 2 && char.IsLowSurrogate(input[pos - 1]) && char.IsHighSurrogate(input[pos - 2]))
        {
            return char.ConvertToUtf32(input, pos - 2);
        }

        return input[pos - 1];
    }

    private static bool IsJamoL(int cp)
    {
        return (cp >= 0x1100 && cp <= 0x115F) || (cp >= 0xA960 && cp <= 0xA97C);
    }

    private static bool IsJamoV(int cp)
    {
        return (cp >= 0x1160 && cp <= 0x11A7) || (cp >= 0xD7B0 && cp <= 0xD7C6);
    }

    private static bool IsJamoT(int cp)
    {
        return (cp >= 0x11A8 && cp <= 0x11FF) || (cp >= 0xD7CB && cp <= 0xD7FB);
    }

    private static List<int> ComputeWordBoundaries(string input)
    {
        // Build word boundaries on top of grapheme clusters to properly handle
        // multi-char sequences like emoji, surrogate pairs, and combining marks
        var graphemes = ComputeGraphemeBoundaries(input);
        var boundaries = new List<int> { 0 };

        var g = 0;
        while (g < graphemes.Count - 1)
        {
            var gStart = graphemes[g];
            var gEnd = graphemes[g + 1];
            _ = input[gStart..gEnd];
            var firstCp = GetCodePointAt(input, gStart);
            var cat = GetCodePointCategory(firstCp);

            if (IsWordChar(cat))
            {
                // Word segment: consume graphemes that are word-like, including midword punctuation
                var wordEnd = gEnd;
                g++;

                while (g < graphemes.Count - 1)
                {
                    var nextStart = graphemes[g];
                    var nextEnd = graphemes[g + 1];
                    var nextCp = GetCodePointAt(input, nextStart);
                    var nextCat = GetCodePointCategory(nextCp);

                    if (IsWordChar(nextCat))
                    {
                        wordEnd = nextEnd;
                        g++;
                    }
                    else if (nextEnd - nextStart == 1 && IsMidWordPunctuation(input, nextStart) &&
                             g + 1 < graphemes.Count - 1)
                    {
                        // Check if followed by a word grapheme
                        var afterStart = graphemes[g + 1];
                        var afterCp = GetCodePointAt(input, afterStart);
                        if (IsWordChar(GetCodePointCategory(afterCp)))
                        {
                            wordEnd = graphemes[g + 2];
                            g += 2;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                boundaries.Add(wordEnd);
            }
            else
            {
                // Non-word grapheme: each is its own segment (whitespace, punctuation, emoji, etc.)
                boundaries.Add(gEnd);
                g++;
            }
        }

        if (boundaries[^1] != input.Length)
        {
            boundaries.Add(input.Length);
        }

        return boundaries;
    }

    private static UnicodeCategory GetCodePointCategory(int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            return char.GetUnicodeCategory((char)codePoint);
        }

        // For supplementary characters, convert to string and use the string overload
        var str = char.ConvertFromUtf32(codePoint);
        return char.GetUnicodeCategory(str, 0);
    }

    private static List<int> ComputeSentenceBoundaries(string input)
    {
        // Build sentence boundaries on top of grapheme clusters
        var graphemes = ComputeGraphemeBoundaries(input);
        var boundaries = new List<int> { 0 };

        for (var g = 0; g < graphemes.Count - 1; g++)
        {
            var gStart = graphemes[g];
            var gEnd = graphemes[g + 1];

            // Check if this grapheme is a sentence terminator (single BMP char)
            if (gEnd - gStart == 1 && IsSentenceTerminator(input[gStart]))
            {
                // Consume trailing whitespace graphemes as part of this sentence
                while (g + 1 < graphemes.Count - 1)
                {
                    var nextStart = graphemes[g + 1];
                    var nextEnd = graphemes[g + 2];
                    if (nextEnd - nextStart == 1 && char.IsWhiteSpace(input[nextStart]))
                    {
                        g++;
                    }
                    else
                    {
                        break;
                    }
                }

                boundaries.Add(graphemes[g + 1]);
            }
        }

        if (boundaries[^1] != input.Length)
        {
            boundaries.Add(input.Length);
        }

        return boundaries;
    }

    private static bool IsWordChar(UnicodeCategory cat)
    {
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation;
    }

    private static bool IsWordLike(string segment)
    {
        for (var i = 0; i < segment.Length; i++)
        {
            var cat = char.GetUnicodeCategory(segment, i);
            if (IsWordChar(cat))
            {
                return true;
            }

            if (char.IsSurrogatePair(segment, i))
            {
                i++;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the character at position i is midword punctuation (like '.' or '\'' between word chars).
    /// Per Unicode Word Boundary rules: MidNum, MidNumLet, and single quote can appear inside words.
    /// </summary>
    private static bool IsMidWordPunctuation(string input, int i)
    {
        var c = input[i];
        // UAX #29 MidNum: '.', ',', U+066B, U+066C
        // MidNumLet: '.', U+2018, U+2019, U+2024, U+FE52, U+FF07, U+FF0E
        // Also include apostrophe and single quote for contractions
        // Note: comma is NOT midword - "a,b" should be three segments
        return c is '.' or '\'' or '\u2018' or '\u2019' or '\u2024'
            or '\u066B' or '\u066C' or '\uFE52' or '\uFF07' or '\uFF0E';
    }

    private static bool IsSentenceTerminator(char c)
    {
        return c is '.' or '!' or '?' or '\u2026' // ellipsis
            or '\u3002' // CJK full stop
            or '\uFF01' // fullwidth exclamation
            or '\uFF1F'; // fullwidth question
    }
}
