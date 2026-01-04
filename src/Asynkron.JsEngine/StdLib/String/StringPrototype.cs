#region

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.JsonHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using static Asynkron.JsEngine.StdLib.StringHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("String", ToStringTag = "String")]
[JsMethodAlias("trimLeft", "trimStart")]
[JsMethodAlias("trimRight", "trimEnd")]
public sealed partial class StringPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    private JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireStringReceiver(thisValue, Realm));
    }

    [JsHostMethod("valueOf", Length = 0d)]
    private JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireStringReceiver(thisValue, Realm));
    }

    [JsHostMethod("parseJSON", Length = 1d)]
    private JsValue ParseJson(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var context = Realm.CreateContext();
        var source = JsOps.ToJsString(thisValue, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var reviver = args.Count > 0 ? args[0] : JsValue.Undefined;
        return ParseJsonWithReviverJsValue(source, Realm, context, reviver);
    }

    [JsHostMethod("charAt", Length = 1d)]
    private JsValue CharAt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        // ToIntegerOrInfinity on missing/undefined argument returns 0
        var posArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var context = Realm?.CreateContext();
        var position = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Handle infinity or out of bounds
        if (double.IsPositiveInfinity(position) || double.IsNegativeInfinity(position))
        {
            return new JsValue("");
        }

        var index = (int)position;
        if (index < 0 || index >= value.Length)
        {
            return new JsValue("");
        }

        return new JsValue(value[index].ToString());
    }

    [JsHostMethod("charCodeAt", Length = 1d)]
    private JsValue CharCodeAt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        // ToIntegerOrInfinity on missing/undefined argument returns 0
        var posArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var context = Realm?.CreateContext();
        var position = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Handle infinity or out of bounds
        if (double.IsPositiveInfinity(position) || double.IsNegativeInfinity(position))
        {
            return new JsValue(double.NaN);
        }

        var index = (int)position;
        if (index < 0 || index >= value.Length)
        {
            return new JsValue(double.NaN);
        }

        return new JsValue((double)value[index]);
    }

    [JsHostMethod("indexOf", Length = 1d)]
    private JsValue IndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var len = value.Length;

        var context = Realm?.CreateContext();

        // Convert search string using ToString
        var searchStrArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var searchStr = JsOps.ToJsString(searchStrArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // ToIntegerOrInfinity on position
        var posArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var pos = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Clamp position
        int start;
        if (double.IsNegativeInfinity(pos))
        {
            start = 0;
        }
        else if (double.IsPositiveInfinity(pos) || pos >= len)
        {
            start = len;
        }
        else
        {
            start = Math.Max(0, (int)pos);
        }

        if (start >= len && searchStr.Length > 0)
        {
            return new JsValue(-1d);
        }

        var result = value.IndexOf(searchStr, start, StringComparison.Ordinal);
        return new JsValue((double)result);
    }

    [JsHostMethod("lastIndexOf", Length = 1d)]
    private JsValue LastIndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var len = value.Length;

        var context = Realm?.CreateContext();

        // Convert search string using ToString
        var searchStrArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var searchStr = JsOps.ToJsString(searchStrArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // ToNumber on position (lastIndexOf uses ToNumber, not ToIntegerOrInfinity)
        var posArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        double numPos;
        if (posArg.IsUndefined)
        {
            numPos = double.PositiveInfinity;
        }
        else
        {
            numPos = JsOps.ToNumber(posArg, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }
        }

        // Handle NaN - per spec, NaN becomes +Infinity for lastIndexOf
        if (double.IsNaN(numPos))
        {
            numPos = double.PositiveInfinity;
        }

        // Clamp position
        int start;
        if (double.IsPositiveInfinity(numPos) || numPos >= len)
        {
            start = len;
        }
        else if (numPos < 0)
        {
            start = 0;
        }
        else
        {
            start = (int)numPos;
        }

        // Empty search string at position >= len should return len
        if (searchStr.Length == 0 && start >= len)
        {
            return new JsValue((double)len);
        }

        if (start <= 0 && searchStr.Length > 0)
        {
            // Search from beginning only
            if (value.StartsWith(searchStr, StringComparison.Ordinal))
            {
                return new JsValue(0d);
            }
            return new JsValue(-1d);
        }

        // Adjust start to be a valid starting index for LastIndexOf
        var searchStart = Math.Min(start + searchStr.Length - 1, len - 1);
        if (searchStart < 0)
        {
            return new JsValue(-1d);
        }

        var result = value.LastIndexOf(searchStr, searchStart, StringComparison.Ordinal);
        return new JsValue((double)result);
    }

    [JsHostMethod("substring", Length = 2d)]
    private JsValue Substring(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var len = value.Length;
        var (intStart, intEnd) = ParseStartEnd(args, len);

        // Clamp to [0, len]
        var finalStart = (int)Math.Max(0, Math.Min(double.IsPositiveInfinity(intStart) ? len : intStart, len));
        var finalEnd = (int)Math.Max(0, Math.Min(double.IsPositiveInfinity(intEnd) ? len : intEnd, len));

        // Swap if start > end
        if (finalStart > finalEnd)
        {
            (finalStart, finalEnd) = (finalEnd, finalStart);
        }

        return new JsValue(value.Substring(finalStart, finalEnd - finalStart));
    }

    [JsHostMethod("slice", Length = 2d)]
    private JsValue Slice(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var len = value.Length;
        var (intStart, intEnd) = ParseStartEnd(args, len);

        // Handle infinity cases
        int from, to;
        if (double.IsNegativeInfinity(intStart))
        {
            from = 0;
        }
        else if (intStart < 0)
        {
            from = Math.Max(0, len + (int)intStart);
        }
        else
        {
            from = Math.Min((int)intStart, len);
        }

        if (double.IsNegativeInfinity(intEnd))
        {
            to = 0;
        }
        else if (intEnd < 0)
        {
            to = Math.Max(0, len + (int)intEnd);
        }
        else
        {
            to = Math.Min((int)intEnd, len);
        }

        if (from >= to)
        {
            return new JsValue("");
        }

        return new JsValue(value.Substring(from, to - from));
    }

    /// <summary>
    /// Parses start and end arguments for substring/slice methods.
    /// </summary>
    private (double Start, double End) ParseStartEnd(IReadOnlyList<JsValue> args, int defaultEnd)
    {
        var context = Realm?.CreateContext();

        // ToIntegerOrInfinity on missing/undefined argument returns 0
        var startArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var intStart = ToIntegerOrInfinity(startArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        double intEnd;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            intEnd = ToIntegerOrInfinity(args[1], context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }
        }
        else
        {
            intEnd = defaultEnd;
        }

        return (intStart, intEnd);
    }

    [JsHostMethod("substr", Length = 2d)]
    private JsValue Substr(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var length = value.Length;
        if (args.Count == 0)
        {
            return new JsValue(value);
        }

        var startNumber = ConvertToNumber(args[0]);
        double startInteger;
        if (double.IsNaN(startNumber))
        {
            startInteger = 0;
        }
        else if (double.IsInfinity(startNumber) || startNumber == 0)
        {
            startInteger = startNumber;
        }
        else
        {
            startInteger = Math.Sign(startNumber) * Math.Floor(Math.Abs(startNumber));
        }

        double lengthNumber;
        if (args.Count > 1)
        {
            if (args[1].IsUndefined)
            {
                lengthNumber = double.PositiveInfinity;
            }
            else
            {
                lengthNumber = ConvertToNumber(args[1]);
                lengthNumber = double.IsNaN(lengthNumber)
                    ? 0
                    : double.IsInfinity(lengthNumber) || lengthNumber == 0
                        ? lengthNumber
                        : Math.Sign(lengthNumber) * Math.Floor(Math.Abs(lengthNumber));
            }
        }
        else
        {
            lengthNumber = double.PositiveInfinity;
        }

        var start = double.IsNegativeInfinity(startInteger) ? 0 : (int)startInteger;
        if (double.IsPositiveInfinity(startInteger))
        {
            return new JsValue("");
        }

        if (start < 0)
        {
            start = Math.Max(0, length + start);
        }
        else if (start > length)
        {
            start = length;
        }

        if (double.IsNaN(lengthNumber) || lengthNumber <= 0)
        {
            return new JsValue("");
        }

        lengthNumber = Math.Min(Math.Max(lengthNumber, 0), length);

        var substrLength = (int)Math.Min(lengthNumber, Math.Max(0, length - start));
        return new JsValue(value.Substring(start, substrLength));
    }

    [JsHostMethod("concat", Length = 1d)]
    private JsValue Concat(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return new JsValue(result);
        }

        var builder = new StringBuilder(result);
        foreach (var arg in args)
        {
            builder.Append(JsValueToString(arg));
        }

        return new JsValue(builder.ToString());
    }

    [JsHostMethod("toLowerCase", Length = 0d)]
    private JsValue ToLowerCase(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(ResolveString(thisValue).ToLowerInvariant());
    }

    [JsHostMethod("toUpperCase", Length = 0d)]
    private JsValue ToUpperCase(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(ResolveString(thisValue).ToUpperInvariant());
    }

    [JsHostMethod("trim", Length = 0d)]
    private JsValue Trim(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(ResolveString(thisValue).Trim());
    }

    [JsHostMethod("trimStart", Length = 0d)]
    private JsValue TrimStart(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(ResolveString(thisValue).TrimStart());
    }

    [JsHostMethod("trimEnd", Length = 0d)]
    private JsValue TrimEnd(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(ResolveString(thisValue).TrimEnd());
    }

    [JsHostMethod("split", Length = 2d)]
    private JsValue Split(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return JsValue.FromJsArray(CreateArrayFromStrings([value], Realm));
        }

        var separatorValue = args[0];
        var splitMethod = GetMethod(separatorValue, SymbolKeys.Split, "@@split");
        if (splitMethod is not null)
        {
            var limitArg = args.GetArgument(1);
            return splitMethod.Invoke([new JsValue(value), limitArg], separatorValue);
        }

        var separator = separatorValue.IsUndefined
            ? null
            : CoerceToString(separatorValue);
        var limit = args.Count > 1 && args[1].TryGetDouble(out var d) ? (int)d : int.MaxValue;

        if (separator is null or "")
        {
            var charCount = Math.Min(value.Length, limit);
            var chars = new string[charCount];
            for (var i = 0; i < charCount; i++)
            {
                chars[i] = value[i].ToString();
            }

            return JsValue.FromJsArray(CreateArrayFromStrings(chars, Realm));
        }

        var parts = value.Split([separator], StringSplitOptions.None);
        if (limit < parts.Length)
        {
            parts = parts.Take(limit).ToArray();
        }

        return JsValue.FromJsArray(CreateArrayFromStrings(parts, Realm));
    }

    [JsHostMethod("replace", Length = 2d)]
    private JsValue Replace(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var search = args.GetArgument(0);
        var replacement = args.GetArgument(1);

        var replaceMethod = GetMethod(search, SymbolKeys.Replace, "@@replace");
        if (replaceMethod is not null)
        {
            return replaceMethod.Invoke([new JsValue(value), replacement], search);
        }

        if (replacement.TryGetObject<IJsCallable>(out var replacer))
        {
            if (TryResolveRegExp(search, out var regex))
            {
                var dotNetRegex = new Regex(regex.Pattern);
                var result = new StringBuilder();
                var lastIndex = 0;

                if (regex.Global)
                {
                    var matches = dotNetRegex.Matches(value);
                    if (matches.Count == 0)
                    {
                        return new JsValue(value);
                    }

                    foreach (Match match in matches)
                    {
                        if (!match.Success)
                        {
                            continue;
                        }

                        if (match.Index > lastIndex)
                        {
                            result.Append(value.AsSpan(lastIndex, match.Index - lastIndex));
                        }

                        var replacementValue = replacer.Invoke(new SingleValueArgs(new JsValue(match.Value)), JsValue.Undefined);
                        var replacementString = replacementValue.ToJsString();
                        result.Append(replacementString);

                        lastIndex = match.Index + match.Length;
                    }
                }
                else
                {
                    var match = dotNetRegex.Match(value);
                    if (!match.Success)
                    {
                        return new JsValue(value);
                    }

                    if (match.Index > 0)
                    {
                        result.Append(value.AsSpan(0, match.Index));
                    }

                    var replacementValue = replacer.Invoke(new SingleValueArgs(new JsValue(match.Value)), JsValue.Undefined);
                    var replacementString = replacementValue.ToJsString();
                    result.Append(replacementString);

                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < value.Length)
                {
                    result.Append(value.AsSpan(lastIndex));
                }

                return new JsValue(result.ToString());
            }

            var searchValueFunc = CoerceToString(search);
            if (searchValueFunc.Length == 0)
            {
                var replacementValue = replacer.Invoke(new SingleValueArgs(new JsValue("")), JsValue.Undefined);
                var replacementString = replacementValue.ToJsString();
                return new JsValue(replacementString + value);
            }

            var idx = value.IndexOf(searchValueFunc, StringComparison.Ordinal);
            if (idx < 0)
            {
                return new JsValue(value);
            }

            var prefix = value[..idx];
            var suffix = value[(idx + searchValueFunc.Length)..];
            var replacedSegment = replacer.Invoke(new SingleValueArgs(new JsValue(searchValueFunc)), JsValue.Undefined).ToJsString();
            return new JsValue(prefix + replacedSegment + suffix);
        }

        if (TryResolveRegExp(search, out var regex2))
        {
            var replaceValue = JsOps.ToJsString(replacement);
            if (regex2.Global)
            {
                return new JsValue(Regex.Replace(value, regex2.Pattern, replaceValue));
            }

            var match = Regex.Match(value, regex2.Pattern);
            if (match.Success)
            {
                return new JsValue(string.Concat(value.AsSpan(0, match.Index), replaceValue,
                    value.AsSpan(match.Index + match.Length)));
            }

            return new JsValue(value);
        }

        var searchValue = CoerceToString(search);
        var replaceStr = CoerceToString(replacement);
        var index = value.IndexOf(searchValue, StringComparison.Ordinal);
        if (index == -1)
        {
            return new JsValue(value);
        }

        return new JsValue(string.Concat(value.AsSpan(0, index), replaceStr, value.AsSpan(index + searchValue.Length)));
    }

    [JsHostMethod("match", Length = 1d)]
    private JsValue Match(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return JsValue.Null;
        }

        var searchValue = args[0];
        var matcher = GetMethod(searchValue, SymbolKeys.Match, "@@match");
        if (matcher is not null)
        {
            return matcher.Invoke(new SingleValueArgs(new JsValue(value)), searchValue);
        }

        var regex = ToRegExpValue(searchValue, string.Empty, false);
        var execResult = regex.Global ? (IAsJsValue?)regex.MatchAll(value) : regex.Exec(value);
        return execResult is null ? JsValue.Null : JsValue.FromObjectUnsafe(execResult);
    }

    [JsHostMethod("search", Length = 1d)]
    private JsValue Search(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return new JsValue(-1d);
        }

        var searchValue = args[0];
        var searchMethod = GetMethod(searchValue, SymbolKeys.Search, "@@search");
        if (searchMethod is not null)
        {
            return searchMethod.Invoke(new SingleValueArgs(new JsValue(value)), searchValue);
        }

        var regex = ToRegExpValue(searchValue, string.Empty, false);
        var result = regex.Exec(value);
        if (result is not null && result.TryGetProperty("index", out var indexObj) &&
            indexObj.TryGetDouble(out var d))
        {
            return new JsValue(d);
        }

        return new JsValue(-1d);
    }

    [JsHostMethod("startsWith", Length = 1d)]
    private JsValue StartsWith(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        var searchArg = args.Count > 0 ? args[0] : JsValue.Undefined;

        // Check if searchString is a RegExp and throw TypeError
        if (IsRegExp(searchArg))
        {
            throw ThrowTypeError("First argument to String.prototype.startsWith must not be a regular expression", context, Realm);
        }

        var searchStr = JsOps.ToJsString(searchArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var len = value.Length;

        // ToIntegerOrInfinity on position
        var posArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var pos = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Clamp position
        int start;
        if (double.IsNegativeInfinity(pos))
        {
            start = 0;
        }
        else if (double.IsPositiveInfinity(pos) || pos >= len)
        {
            start = len;
        }
        else
        {
            start = Math.Max(0, (int)pos);
        }

        if (start + searchStr.Length > len)
        {
            return JsValue.False;
        }

        return new JsValue(value.AsSpan(start).StartsWith(searchStr, StringComparison.Ordinal));
    }

    [JsHostMethod("endsWith", Length = 1d)]
    private JsValue EndsWith(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        var searchArg = args.Count > 0 ? args[0] : JsValue.Undefined;

        // Check if searchString is a RegExp and throw TypeError
        if (IsRegExp(searchArg))
        {
            throw ThrowTypeError("First argument to String.prototype.endsWith must not be a regular expression", context, Realm);
        }

        var searchStr = JsOps.ToJsString(searchArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var len = value.Length;

        // ToIntegerOrInfinity on endPosition
        int end;
        var endPosArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        if (endPosArg.IsUndefined)
        {
            end = len;
        }
        else
        {
            var pos = ToIntegerOrInfinity(endPosArg, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            if (double.IsNegativeInfinity(pos))
            {
                end = 0;
            }
            else if (double.IsPositiveInfinity(pos) || pos >= len)
            {
                end = len;
            }
            else
            {
                end = Math.Max(0, (int)pos);
            }
        }

        var searchLength = searchStr.Length;
        var start = end - searchLength;

        if (start < 0)
        {
            return JsValue.False;
        }

        return new JsValue(value.AsSpan(start, searchLength).SequenceEqual(searchStr.AsSpan()));
    }

    [JsHostMethod("includes", Length = 1d)]
    private JsValue Includes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        var searchArg = args.Count > 0 ? args[0] : JsValue.Undefined;

        // Check if searchString is a RegExp and throw TypeError
        if (IsRegExp(searchArg))
        {
            throw ThrowTypeError("First argument to String.prototype.includes must not be a regular expression", context, Realm);
        }

        var searchStr = JsOps.ToJsString(searchArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var len = value.Length;

        // ToIntegerOrInfinity on position
        var posArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var pos = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Clamp position
        int start;
        if (double.IsNegativeInfinity(pos))
        {
            start = 0;
        }
        else if (double.IsPositiveInfinity(pos) || pos >= len)
        {
            return new JsValue(searchStr.Length == 0);
        }
        else
        {
            start = Math.Max(0, (int)pos);
        }

        return new JsValue(value.IndexOf(searchStr, start, StringComparison.Ordinal) >= 0);
    }

    [JsHostMethod("repeat", Length = 1d)]
    private JsValue Repeat(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        // ToIntegerOrInfinity on count
        var countArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var n = ToIntegerOrInfinity(countArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Throw RangeError for negative count or positive infinity
        if (n < 0 || double.IsPositiveInfinity(n))
        {
            throw ThrowRangeError("Invalid count value", context, Realm);
        }

        if (n == 0 || value.Length == 0)
        {
            return new JsValue("");
        }

        var count = (int)n;

        // Check if result would be too large
        if ((long)value.Length * count > int.MaxValue / 2)
        {
            throw ThrowRangeError("Invalid count value", context, Realm);
        }

        return new JsValue(string.Concat(Enumerable.Repeat(value, count)));
    }

    [JsHostMethod("padStart", Length = 1d)]
    private JsValue PadStart(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        // ToIntegerOrInfinity on maxLength
        var maxLengthArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var intMaxLength = ToIntegerOrInfinity(maxLengthArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var stringLength = value.Length;

        // If maxLength is -Infinity, 0, or <= stringLength, return S
        if (double.IsNegativeInfinity(intMaxLength) || intMaxLength <= stringLength)
        {
            return new JsValue(value);
        }

        // Get fill string
        string filler;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            filler = JsOps.ToJsString(args[1], context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }
        }
        else
        {
            filler = " ";
        }

        if (filler.Length == 0)
        {
            return new JsValue(value);
        }

        // Handle infinity - just return original string
        if (double.IsPositiveInfinity(intMaxLength))
        {
            return new JsValue(value);
        }

        var targetLength = (int)intMaxLength;
        var padLength = targetLength - stringLength;
        var padCount = (int)Math.Ceiling((double)padLength / filler.Length);
        var padding = string.Concat(Enumerable.Repeat(filler, padCount));
        return new JsValue(string.Concat(padding.AsSpan(0, padLength), value));
    }

    [JsHostMethod("padEnd", Length = 1d)]
    private JsValue PadEnd(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        var context = Realm?.CreateContext();

        // ToIntegerOrInfinity on maxLength
        var maxLengthArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var intMaxLength = ToIntegerOrInfinity(maxLengthArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var stringLength = value.Length;

        // If maxLength is -Infinity, 0, or <= stringLength, return S
        if (double.IsNegativeInfinity(intMaxLength) || intMaxLength <= stringLength)
        {
            return new JsValue(value);
        }

        // Get fill string
        string filler;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            filler = JsOps.ToJsString(args[1], context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }
        }
        else
        {
            filler = " ";
        }

        if (filler.Length == 0)
        {
            return new JsValue(value);
        }

        // Handle infinity - just return original string
        if (double.IsPositiveInfinity(intMaxLength))
        {
            return new JsValue(value);
        }

        var targetLength = (int)intMaxLength;
        var padLength = targetLength - stringLength;
        var padCount = (int)Math.Ceiling((double)padLength / filler.Length);
        var padding = string.Concat(Enumerable.Repeat(filler, padCount));
        return new JsValue(string.Concat(value, padding.AsSpan(0, padLength)));
    }

    [JsHostMethod("replaceAll", Length = 2d)]
    private JsValue ReplaceAll(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var searchValue = args.GetArgument(0);
        var replaceValue = args.GetArgument(1);

        var replaceMethod = GetMethod(searchValue, SymbolKeys.Replace, "@@replace");
        if (replaceMethod is not null)
        {
            return replaceMethod.Invoke([new JsValue(value), replaceValue], searchValue);
        }

        if (TryResolveRegExp(searchValue, out var regex))
        {
            if (!regex.Global)
            {
                throw ThrowTypeError("String.prototype.replaceAll called with a non-global RegExp", realm: Realm);
            }

            var replaceStr = CoerceToString(replaceValue);
            return new JsValue(Regex.Replace(value, regex.Pattern, replaceStr));
        }

        if (replaceValue.TryGetObject<IJsCallable>(out var replacer))
        {
            var searchStrFunc = CoerceToString(searchValue);
            if (searchStrFunc.Length == 0)
            {
                var replacementValue = replacer.Invoke(new SingleValueArgs(new JsValue("")), new JsValue(value)).ToJsString();
                var builder = new StringBuilder();
                builder.Append(replacementValue);
                foreach (var ch in value)
                {
                    builder.Append(ch);
                    builder.Append(replacementValue);
                }

                return new JsValue(builder.ToString());
            }

            var result = new StringBuilder();
            var currentIndex = 0;
            while (true)
            {
                var idx = value.IndexOf(searchStrFunc, currentIndex, StringComparison.Ordinal);
                if (idx < 0)
                {
                    result.Append(value.AsSpan(currentIndex));
                    break;
                }

                result.Append(value.AsSpan(currentIndex, idx - currentIndex));
                var replacementValue = replacer.Invoke(new SingleValueArgs(new JsValue(searchStrFunc)), new JsValue(value));
                var replacementString = replacementValue.ToJsString();
                result.Append(replacementString);
                currentIndex = idx + searchStrFunc.Length;
            }

            return new JsValue(result.ToString());
        }

        var searchStr = CoerceToString(searchValue);
        var replaceStrPlain = CoerceToString(replaceValue);
        return new JsValue(value.Replace(searchStr, replaceStrPlain, StringComparison.Ordinal));
    }

    [JsHostMethod("at", Length = 1d)]
    private JsValue At(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var len = value.Length;

        // ToIntegerOrInfinity on missing/undefined argument returns 0
        var indexArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var context = Realm?.CreateContext();
        var relativeIndex = ToIntegerOrInfinity(indexArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Handle infinity cases - they result in out-of-bounds
        if (double.IsPositiveInfinity(relativeIndex) || double.IsNegativeInfinity(relativeIndex))
        {
            return JsValue.Undefined;
        }

        int k;
        if (relativeIndex >= 0)
        {
            if (relativeIndex >= len)
            {
                return JsValue.Undefined;
            }

            k = (int)relativeIndex;
        }
        else
        {
            k = len + (int)relativeIndex;
            if (k < 0)
            {
                return JsValue.Undefined;
            }
        }

        return new JsValue(value[k].ToString());
    }

    [JsHostMethod("codePointAt", Length = 1d)]
    private JsValue CodePointAt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);

        // ToIntegerOrInfinity on missing/undefined argument returns 0
        var posArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var context = Realm?.CreateContext();
        var position = ToIntegerOrInfinity(posArg, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Handle infinity or out of bounds
        if (double.IsPositiveInfinity(position) || double.IsNegativeInfinity(position))
        {
            return JsValue.Undefined;
        }

        var index = (int)position;
        if (index < 0 || index >= value.Length)
        {
            return JsValue.Undefined;
        }

        var c = value[index];
        if (!char.IsHighSurrogate(c) || index + 1 >= value.Length)
        {
            return new JsValue((double)c);
        }

        var low = value[index + 1];
        if (!char.IsLowSurrogate(low))
        {
            return new JsValue((double)c);
        }

        var high = (int)c;
        var lowInt = (int)low;
        var codePoint = ((high - 0xD800) << 10) + (lowInt - 0xDC00) + 0x10000;
        return new JsValue((double)codePoint);
    }

    [JsHostMethod("localeCompare", Length = 1d)]
    private JsValue LocaleCompare(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return new JsValue(0d);
        }

        var compareString = JsOps.ToJsString(args[0]);
        var result = string.Compare(value, compareString, StringComparison.CurrentCulture);
        return new JsValue((double)result);
    }

    [JsHostMethod("normalize", Length = 0d)]
    private JsValue Normalize(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var form = args.Count > 0 && !args[0].IsUndefined ? JsOps.ToJsString(args[0]) : "NFC";

        return new JsValue(form switch
        {
            // Normalize uses explicit forms; invalid names should raise RangeError.
            "NFC" => value.Normalize(NormalizationForm.FormC),
            "NFD" => value.Normalize(NormalizationForm.FormD),
            "NFKC" => value.Normalize(NormalizationForm.FormKC),
            "NFKD" => value.Normalize(NormalizationForm.FormKD),
            _ => throw ThrowRangeError("The normalization form should be one of NFC, NFD, NFKC, NFKD.", realm: Realm)
        });
    }

    [JsHostMethod("matchAll", Length = 1d)]
    private JsValue MatchAll(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        if (args.Count == 0)
        {
            return JsValue.Null;
        }

        var matcher = args[0];
        var method = GetMethod(matcher, SymbolKeys.MatchAll, "@@matchAll");
        if (method is not null)
        {
            return method.Invoke(new SingleValueArgs(new JsValue(value)), matcher);
        }

        var regex = ToRegExpValue(matcher, "g", true);
        return JsValue.FromJsArray(regex.MatchAll(value));
    }

    // HTML wrapper methods (Annex B)

    [JsHostMethod("small", Length = 0d)]
    private JsValue Small(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<small>{value}</small>");
    }

    [JsHostMethod("strike", Length = 0d)]
    private JsValue Strike(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<strike>{value}</strike>");
    }

    [JsHostMethod("sub", Length = 0d)]
    private JsValue Sub(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<sub>{value}</sub>");
    }

    [JsHostMethod("sup", Length = 0d)]
    private JsValue Sup(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<sup>{value}</sup>");
    }

    [JsHostMethod("anchor", Length = 1d)]
    private JsValue Anchor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var name = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
        return new JsValue($"<a name=\"{EscapeAttr(name)}\">{value}</a>");
    }

    [JsHostMethod("big", Length = 0d)]
    private JsValue Big(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<big>{value}</big>");
    }

    [JsHostMethod("blink", Length = 0d)]
    private JsValue Blink(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<blink>{value}</blink>");
    }

    [JsHostMethod("bold", Length = 0d)]
    private JsValue Bold(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<b>{value}</b>");
    }

    [JsHostMethod("fixed", Length = 0d)]
    private JsValue Fixed(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<tt>{value}</tt>");
    }

    [JsHostMethod("fontcolor", Length = 1d)]
    private JsValue FontColor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var color = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
        return new JsValue($"<font color=\"{EscapeAttr(color)}\">{value}</font>");
    }

    [JsHostMethod("fontsize", Length = 1d)]
    private JsValue FontSize(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var size = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
        return new JsValue($"<font size=\"{EscapeAttr(size)}\">{value}</font>");
    }

    [JsHostMethod("italics", Length = 0d)]
    private JsValue Italics(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        return new JsValue($"<i>{value}</i>");
    }

    [JsHostMethod("link", Length = 1d)]
    private JsValue Link(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = ResolveString(thisValue);
        var url = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
        return new JsValue($"<a href=\"{EscapeAttr(url)}\">{value}</a>");
    }

    [JsSymbolMethod("iterator", Length = 0d)]
    private JsValue CreateIterator(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var value = ResolveString(thisValue);
        var index = 0;
        var iterator = new JsObject();

        iterator.SetHostedProperty("next", new HostFunction(Next, Realm, false));

        return new JsValue(iterator);

        JsValue Next(JsValue _, IReadOnlyList<JsValue> __)
        {
            var result = new JsObject();
            if (index < value.Length)
            {
                var currentValue = StringHelper.ReadCodePoint(value, ref index);
                result.SetProperty("value", currentValue);
                result.SetProperty("done", false);
            }
            else
            {
                result.SetProperty("value", JsValue.Undefined);
                result.SetProperty("done", true);
            }

            return new JsValue(result);
        }
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is not JsObject stringProto)
        {
            return;
        }

        Realm.StringPrototype ??= stringProto;
        stringProto.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        Realm.StringPrototypeMethodsInitialized = true;
    }

    // Helper methods

    private string ResolveString(JsValue thisValue)
    {
        var context = Realm?.CreateContext();
        if (thisValue.IsUndefined || thisValue.IsNull)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: Realm);
        }

        var str = thisValue.ToJsString(context, Realm);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return str;
    }

    private string CoerceToString(JsValue value)
    {
        var context = Realm?.CreateContext();
        var result = value.ToJsString(context, Realm);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    private string JsValueToString(JsValue value)
    {
        var context = Realm?.CreateContext();
        var result = value.ToJsString(context, Realm);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    private double ConvertToNumber(JsValue input)
    {
        if (input.TryGetSymbol(out _) || input.TryGetObject<JsSymbol>(out _))
        {
            throw new ThrowSignal(CreateTypeError("Cannot convert a Symbol value to a number",
                null, Realm));
        }

        var numericContext = Realm?.CreateContext();
        var primitive = JsOps.ToPrimitive(input, ToPrimitiveHint.Number, numericContext);
        if (numericContext?.IsThrow == true)
        {
            throw new ThrowSignal(numericContext.FlowValue);
        }

        var number = JsOps.ToNumber(primitive, numericContext);
        if (numericContext?.IsThrow == true)
        {
            var flowValue = numericContext.FlowValue;
            throw new ThrowSignal(!flowValue.IsUndefined
                ? flowValue
                : CreateTypeError("Cannot convert object to primitive value", numericContext, Realm));
        }

        return number;
    }

    private static bool IsRegExp(JsValue argument)
    {
        // Per spec: 7.2.8 IsRegExp ( argument )
        if (!argument.IsObject)
        {
            return false;
        }

        // Check Symbol.match property
        if (JsOps.TryGetPropertyValue(argument, SymbolKeys.Match, out var matchValue) && !matchValue.IsUndefined)
        {
            return matchValue.IsTruthy;
        }

        // Otherwise check if it's a RegExp object
        return argument.TryGetObject<JsRegExp>(out _);
    }

    private static bool TryResolveRegExp(JsValue candidate, out JsRegExp regex)
    {
        if (candidate.TryGetObject<JsRegExp>(out var direct))
        {
            regex = direct;
            return true;
        }

        if (candidate.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__regex__", out var regexValue) &&
            regexValue.TryGetObject<JsRegExp>(out var stored))
        {
            regex = stored;
            return true;
        }

        regex = null!;
        return false;
    }

    private IJsCallable? GetMethod(JsValue value, string methodKey, string opName)
    {
        if (!JsOps.TryGetPropertyValue(value, methodKey, out var method))
        {
            return null;
        }

        if (method.IsNullOrUndefined)
        {
            return null;
        }

        if (!method.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError($"{opName} is not callable", realm: Realm);
        }

        return callable;
    }

    private JsRegExp ToRegExpValue(JsValue candidate, string defaultFlags, bool requireGlobal)
    {
        if (candidate.TryGetObject<JsRegExp>(out var direct))
        {
            if (requireGlobal && !direct.Global)
            {
                throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: Realm);
            }

            return direct;
        }

        if (candidate.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__regex__", out var regexValue) &&
            regexValue.TryGetObject<JsRegExp>(out var stored))
        {
            if (requireGlobal && !stored.Global)
            {
                throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: Realm);
            }

            return stored;
        }

        var ctx = Realm?.CreateContext();
        var pattern = candidate.IsUndefined
            ? string.Empty
            : candidate.ToJsString(ctx, Realm);

        var created = new JsRegExp(pattern, defaultFlags ?? string.Empty, Realm);
        if (requireGlobal && !created.Global)
        {
            throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: Realm);
        }

        return created;
    }

    private static string EscapeAttr(string input)
    {
        return input.Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static JsArray CreateArrayFromStrings(string[] strings, RealmState? realm)
    {
        return new JsArray(strings.Select(static s => new JsValue(s)), realm);
    }

    [JsHostMethod("isWellFormed", Length = 0d)]
    public JsValue IsWellFormed(JsValue thisValue)
    {
        var str = ResolveString(thisValue);

        // Check for lone surrogates
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            // Check if it's a high surrogate (0xD800-0xDBFF)
            if (char.IsHighSurrogate(c))
            {
                // Must be followed by a low surrogate
                if (i + 1 >= str.Length || !char.IsLowSurrogate(str[i + 1]))
                {
                    return false; // Lone high surrogate
                }
                i++; // Skip the low surrogate
            }
            // Check if it's a low surrogate (0xDC00-0xDFFF)
            else if (char.IsLowSurrogate(c))
            {
                return false; // Lone low surrogate
            }
        }

        return true;
    }

    [JsHostMethod("toWellFormed", Length = 0d)]
    public JsValue ToWellFormed(JsValue thisValue)
    {
        var str = ResolveString(thisValue);

        // Replacement character U+FFFD
        const char replacementChar = '\uFFFD';
        var sb = new StringBuilder(str.Length);

        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            // Check if it's a high surrogate
            if (char.IsHighSurrogate(c))
            {
                // Check if followed by a low surrogate
                if (i + 1 < str.Length && char.IsLowSurrogate(str[i + 1]))
                {
                    // Valid surrogate pair
                    sb.Append(c);
                    sb.Append(str[i + 1]);
                    i++; // Skip the low surrogate
                }
                else
                {
                    // Lone high surrogate - replace with U+FFFD
                    sb.Append(replacementChar);
                }
            }
            // Check if it's a low surrogate
            else if (char.IsLowSurrogate(c))
            {
                // Lone low surrogate - replace with U+FFFD
                sb.Append(replacementChar);
            }
            else
            {
                // Normal character
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    [JsHostMethod("toLocaleLowerCase", Length = 0d)]
    public JsValue ToLocaleLowerCase(JsValue thisValue, IReadOnlyList<JsValue> args) =>
        ResolveString(thisValue).ToLower(ResolveCulture(args));

    [JsHostMethod("toLocaleUpperCase", Length = 0d)]
    public JsValue ToLocaleUpperCase(JsValue thisValue, IReadOnlyList<JsValue> args) =>
        ResolveString(thisValue).ToUpper(ResolveCulture(args));

    private static CultureInfo ResolveCulture(IReadOnlyList<JsValue> args)
    {
        if (args.Count > 0 && args[0].TryGetString(out var locale) && !string.IsNullOrEmpty(locale))
        {
            try
            {
                return CultureInfo.GetCultureInfo(locale);
            }
            catch
            {
                // If locale is invalid, fall back to invariant culture
            }
        }

        // Use invariant culture as default (JavaScript behavior)
        return CultureInfo.InvariantCulture;
    }
}
