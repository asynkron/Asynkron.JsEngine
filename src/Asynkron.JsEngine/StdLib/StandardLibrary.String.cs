using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    /// <summary>
    ///     Creates a string wrapper object with string methods attached.
    ///     This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str, EvaluationContext? context = null, RealmState? realm = null)
    {
        var stringObj = new JsObject();
        stringObj["__value__"] = str;
        stringObj["length"] = (double)str.Length;
        var prototype = context?.RealmState?.StringPrototype ?? realm?.StringPrototype;
        if (prototype is not null)
        {
            stringObj.SetPrototype(prototype);
        }

        AddStringMethods(stringObj, str, realm ?? context?.RealmState);
        return stringObj;
    }

    /// <summary>
    ///     Adds string methods to a string wrapper object.
    /// </summary>
    private static void AddStringMethods(JsObject stringObj, string str, RealmState? realm)
    {
        stringObj.SetHostedProperty("charAt", CharAt);
        stringObj.SetHostedProperty("charCodeAt", CharCodeAt);
        stringObj.SetHostedProperty("indexOf", IndexOf);
        stringObj.SetHostedProperty("lastIndexOf", LastIndexOf);
        stringObj.SetHostedProperty("substring", Substring);
        stringObj.SetHostedProperty("slice", Slice);
        stringObj.SetHostedProperty("substr", Substr);
        stringObj.SetHostedProperty("concat", Concat);
        stringObj.SetHostedProperty("toLowerCase", ToLowerCase);
        stringObj.SetHostedProperty("toUpperCase", ToUpperCase);
        stringObj.SetHostedProperty("trim", Trim);
        stringObj.SetHostedProperty("trimStart", TrimStart);
        stringObj.SetHostedProperty("trimLeft", TrimStart);
        stringObj.SetHostedProperty("trimEnd", TrimEnd);
        stringObj.SetHostedProperty("trimRight", TrimEnd);
        stringObj.SetHostedProperty("split", Split, realm);
        stringObj.SetHostedProperty("replace", Replace);
        stringObj.SetHostedProperty("match", Match);
        stringObj.SetHostedProperty("search", Search);
        stringObj.SetHostedProperty("startsWith", StartsWith);
        stringObj.SetHostedProperty("endsWith", EndsWith);
        stringObj.SetHostedProperty("includes", Includes);
        stringObj.SetHostedProperty("repeat", Repeat);
        stringObj.SetHostedProperty("padStart", PadStart);
        stringObj.SetHostedProperty("padEnd", PadEnd);
        stringObj.SetHostedProperty("replaceAll", ReplaceAll);
        stringObj.SetHostedProperty("at", At);
        stringObj.SetHostedProperty("codePointAt", CodePointAt);
        stringObj.SetHostedProperty("localeCompare", LocaleCompare);
        stringObj.SetHostedProperty("normalize", Normalize);
        stringObj.SetHostedProperty("matchAll", MatchAll);
        stringObj.SetHostedProperty("anchor", Anchor);
        stringObj.SetHostedProperty("link", Link);

        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        stringObj.SetHostedProperty(iteratorKey, CreateIterator);
        return;

        string ResolveString(object? thisValue)
        {
            return JsValueToString(thisValue);
        }

        object? CharAt(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= value.Length)
            {
                return "";
            }

            return value[index].ToString();
        }

        object? CharCodeAt(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= value.Length)
            {
                return double.NaN;
            }

            return (double)value[index];
        }

        object? IndexOf(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            var result = value.IndexOf(searchStr, position, StringComparison.Ordinal);
            return (double)result;
        }

        object? LastIndexOf(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d
                ? Math.Min((int)d, value.Length - 1)
                : value.Length - 1;
            var result = position >= 0 ? value.LastIndexOf(searchStr, position, StringComparison.Ordinal) : -1;
            return (double)result;
        }

        object? Substring(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return value;
            }

            var start = args[0] is double d1 ? Math.Max(0, Math.Min((int)d1, value.Length)) : 0;
            var end = args.Count > 1 && args[1] is double d2
                ? Math.Max(0, Math.Min((int)d2, value.Length))
                : value.Length;

            if (start > end)
            {
                (start, end) = (end, start);
            }

            return value.Substring(start, end - start);
        }

        object? Slice(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return value;
            }

            var start = args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : value.Length;

            if (start < 0)
            {
                start = Math.Max(0, value.Length + start);
            }
            else
            {
                start = Math.Min(start, value.Length);
            }

            if (end < 0)
            {
                end = Math.Max(0, value.Length + end);
            }
            else
            {
                end = Math.Min(end, value.Length);
            }

            if (start >= end)
            {
                return "";
            }

            return value.Substring(start, end - start);
        }

        object? Substr(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var length = value.Length;
            if (args.Count == 0)
            {
                return value;
            }

            var start = args[0] is double d1 ? (int)d1 : 0;
            if (start < 0)
            {
                start = Math.Max(0, length + start);
            }
            else if (start >= length)
            {
                return "";
            }

            int substrLength;
            if (args.Count > 1 && args[1] is double d2)
            {
                if (d2 <= 0)
                {
                    return "";
                }

                substrLength = (int)Math.Min(d2, length - start);
            }
            else
            {
                substrLength = length - start;
            }

            return value.Substring(start, substrLength);
        }

        object? Concat(object? thisValue, IReadOnlyList<object?> args)
        {
            var result = ResolveString(thisValue);
            foreach (var arg in args)
            {
                result += JsValueToString(arg);
            }

            return result;
        }

        object? ToLowerCase(object? thisValue, IReadOnlyList<object?> _)
        {
            return ResolveString(thisValue).ToLowerInvariant();
        }

        object? ToUpperCase(object? thisValue, IReadOnlyList<object?> _)
        {
            return ResolveString(thisValue).ToUpperInvariant();
        }

        object? Trim(object? thisValue, IReadOnlyList<object?> _)
        {
            return ResolveString(thisValue).Trim();
        }

        object? TrimStart(object? thisValue, IReadOnlyList<object?> _)
        {
            return ResolveString(thisValue).TrimStart();
        }

        object? TrimEnd(object? thisValue, IReadOnlyList<object?> _)
        {
            return ResolveString(thisValue).TrimEnd();
        }

        object? Split(object? thisValue, IReadOnlyList<object?> args, RealmState? realmState)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return CreateArrayFromStrings([value], realmState ?? realm);
            }

            var separator = args[0]?.ToString();
            var limit = args.Count > 1 && args[1] is double d ? (int)d : int.MaxValue;

            if (separator is null or "")
            {
                var chars = value.Select(c => c.ToString()).Take(limit).ToArray();
                return CreateArrayFromStrings(chars, realmState ?? realm);
            }

            var parts = value.Split([separator], StringSplitOptions.None);
            if (limit < parts.Length)
            {
                parts = parts.Take(limit).ToArray();
            }

            return CreateArrayFromStrings(parts, realmState ?? realm);
        }

        object? Replace(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count < 2)
            {
                return value;
            }

            var search = args[0];
            var replacement = args[1];

            if (replacement is IJsCallable replacer)
            {
                if (search is JsObject regexObj &&
                    regexObj.TryGetProperty("__regex__", out var regexValue) &&
                    regexValue is JsRegExp regex)
                {
                    var dotNetRegex = new Regex(regex.Pattern);
                    var result = new StringBuilder();
                    var lastIndex = 0;

                    if (regex.Global)
                    {
                        var matches = dotNetRegex.Matches(value);
                        if (matches.Count == 0)
                        {
                            return value;
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

                            var replacementValue = replacer.Invoke([match.Value], value);
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
                            return value;
                        }

                        if (match.Index > 0)
                        {
                            result.Append(value.AsSpan(0, match.Index));
                        }

                        var replacementValue = replacer.Invoke([match.Value], value);
                        var replacementString = replacementValue.ToJsString();
                        result.Append(replacementString);

                        lastIndex = match.Index + match.Length;
                    }

                    if (lastIndex < value.Length)
                    {
                        result.Append(value.AsSpan(lastIndex));
                    }

                    return result.ToString();
                }

                var searchValueFunc = search?.ToString() ?? "";
                if (searchValueFunc.Length == 0)
                {
                    var replacementValue = replacer.Invoke([""], value);
                    var replacementString = replacementValue.ToJsString();
                    return replacementString + value;
                }

                var idx = value.IndexOf(searchValueFunc, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return value;
                }

                var prefix = value[..idx];
                var suffix = value[(idx + searchValueFunc.Length)..];
                var replacedSegment = replacer.Invoke([searchValueFunc], value).ToJsString();
                return prefix + replacedSegment + suffix;
            }

            if (search is JsObject regexObj2 && regexObj2.TryGetProperty("__regex__", out var regexValue2) &&
                regexValue2 is JsRegExp regex2)
            {
                var replaceValue = replacement?.ToString() ?? "";
                if (regex2.Global)
                {
                    return Regex.Replace(value, regex2.Pattern, replaceValue);
                }

                var match = Regex.Match(value, regex2.Pattern);
                if (match.Success)
                {
                    return string.Concat(value.AsSpan(0, match.Index), replaceValue,
                        value.AsSpan(match.Index + match.Length));
                }

                return value;
            }

            var searchValue = search?.ToString() ?? "";
            var replaceStr = replacement?.ToString() ?? "";
            var index = value.IndexOf(searchValue, StringComparison.Ordinal);
            if (index == -1)
            {
                return value;
            }

            return string.Concat(value.AsSpan(0, index), replaceStr, value.AsSpan(index + searchValue.Length));
        }

        object? Match(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is not JsObject regexObj || !regexObj.TryGetProperty("__regex__", out var regexValue) ||
                regexValue is not JsRegExp regex)
            {
                return null;
            }

            return regex.Global ? regex.MatchAll(value) : regex.Exec(value);
        }

        object? Search(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return -1d;
            }

            if (args[0] is not JsObject regexObj || !regexObj.TryGetProperty("__regex__", out var regexValue) ||
                regexValue is not JsRegExp regex)
            {
                return -1d;
            }

            var result = regex.Exec(value);
            if (result is JsArray arr && arr.TryGetProperty("index", out var indexObj) &&
                indexObj is double d)
            {
                return d;
            }

            return -1d;
        }

        object? StartsWith(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? (int)d : 0;
            if (position < 0 || position >= value.Length)
            {
                return false;
            }

            return value[position..].StartsWith(searchStr, StringComparison.Ordinal);
        }

        object? EndsWith(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var length = args.Count > 1 && args[1] is double d ? (int)d : value.Length;
            if (length < 0)
            {
                return false;
            }

            length = Math.Min(length, value.Length);
            return value[..length].EndsWith(searchStr, StringComparison.Ordinal);
        }

        object? Includes(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            if (position >= value.Length)
            {
                return searchStr.Length == 0;
            }

            return value.IndexOf(searchStr, position, StringComparison.Ordinal) >= 0;
        }

        object? Repeat(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || args[0] is not double d)
            {
                return "";
            }

            var count = (int)d;
            if (count is < 0 or int.MaxValue)
            {
                return "";
            }

            if (count == 0)
            {
                return "";
            }

            return string.Concat(Enumerable.Repeat(value, count));
        }

        object? PadStart(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return value;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= value.Length)
            {
                return value;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return value;
            }

            var padLength = targetLength - value.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(padding.AsSpan(0, padLength), value);
        }

        object? PadEnd(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return value;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= value.Length)
            {
                return value;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return value;
            }

            var padLength = targetLength - value.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(value, padding.AsSpan(0, padLength));
        }

        object? ReplaceAll(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count < 2)
            {
                return value;
            }

            var searchValue = args[0]?.ToString() ?? "";
            var replaceValue = args[1]?.ToString() ?? "";
            return value.Replace(searchValue, replaceValue);
        }

        object? At(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            if (index < 0)
            {
                index = value.Length + index;
            }

            if (index < 0 || index >= value.Length)
            {
                return null;
            }

            return value[index].ToString();
        }

        object? CodePointAt(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            if (index < 0 || index >= value.Length)
            {
                return null;
            }

            var c = value[index];
            if (!char.IsHighSurrogate(c) || index + 1 >= value.Length)
            {
                return (double)c;
            }

            var low = value[index + 1];
            if (!char.IsLowSurrogate(low))
            {
                return (double)c;
            }

            var high = (int)c;
            var lowInt = (int)low;
            var codePoint = ((high - 0xD800) << 10) + (lowInt - 0xDC00) + 0x10000;
            return (double)codePoint;
        }

        object? LocaleCompare(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return 0d;
            }

            var compareString = args[0]?.ToString() ?? "";
            var result = string.Compare(value, compareString, StringComparison.CurrentCulture);
            return (double)result;
        }

        object? Normalize(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var form = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "NFC";

            try
            {
                return form switch
                {
                    "NFC" => value.Normalize(NormalizationForm.FormC),
                    "NFD" => value.Normalize(NormalizationForm.FormD),
                    "NFKC" => value.Normalize(NormalizationForm.FormKC),
                    "NFKD" => value.Normalize(NormalizationForm.FormKD),
                    _ => throw new Exception(
                        "RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
                };
            }
            catch
            {
                return value;
            }
        }

        object? MatchAll(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp regex)
            {
                return regex.MatchAll(value);
            }

            var pattern = args[0]?.ToString() ?? "";
            var tempRegex = new JsRegExp(pattern, "g");
            return tempRegex.MatchAll(value);
        }

        object? Anchor(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var name = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            name = name.Replace("\"", "&quot;");
            return $"<a name=\"{name}\">{value}</a>";
        }

        object? Link(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var url = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            url = url.Replace("\"", "&quot;");
            return $"<a href=\"{url}\">{value}</a>";
        }

        object? CreateIterator(object? thisValue, IReadOnlyList<object?> _)
        {
            var value = ResolveString(thisValue);
            var indexHolder = new[] { 0 };
            var iterator = new JsObject();

            iterator.SetHostedProperty("next", Next);

            return iterator;

            object? Next(object? _, IReadOnlyList<object?> __)
            {
                var result = new JsObject();
                if (indexHolder[0] < value.Length)
                {
                    result.SetProperty("value", value[indexHolder[0]].ToString());
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", Symbols.Undefined);
                    result.SetProperty("done", true);
                }

                return result;
            }
        }
    }

    private static JsArray CreateArrayFromStrings(string[] strings, RealmState? realm)
    {
        var array = new JsArray();
        foreach (var s in strings)
        {
            array.Push(s);
        }

        if (realm is not null)
        {
            AddArrayMethods(array, realm);
        }

        return array;
    }

    /// <summary>
    ///     Creates the String constructor with static methods.
    /// </summary>
    public static HostFunction CreateStringConstructor(RealmState realm)
    {
        // String constructor
        var stringConstructor = new HostFunction((thisValue, args) =>
        {
            var value = args.Count > 0 ? args[0] : Symbols.Undefined;
            var str = value switch
            {
                string s => s,
                double d => d.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                null => "null",
                Symbol sym when ReferenceEquals(sym, Symbols.Undefined) => "undefined",
                _ => value?.ToString() ?? string.Empty
            };

            if (thisValue is not JsObject obj)
            {
                return str;
            }

            obj.SetProperty("__value__", str);
            obj.SetProperty("length", (double)str.Length);
            if (realm.StringPrototype is not null)
            {
                obj.SetPrototype(realm.StringPrototype);
            }

            return obj;
        });

        // Remember String.prototype so that string wrapper objects can see
        // methods attached from user code (e.g. String.prototype.toJSONString),
        // and provide a minimal shared implementation of core helpers such as
        // String.prototype.slice for use with call/apply patterns.
        if (stringConstructor.TryGetProperty("prototype", out var stringProto) &&
            stringProto is JsObject stringProtoObj)
        {
            realm.StringPrototype ??= stringProtoObj;
            if (realm.ObjectPrototype is not null && stringProtoObj.Prototype is null)
            {
                stringProtoObj.SetPrototype(realm.ObjectPrototype);
            }

            stringProtoObj.SetProperty("slice", new HostFunction((thisValue, args) =>
            {
                var str = JsValueToString(thisValue);
                if (str is null)
                {
                    return "";
                }

                if (args.Count == 0)
                {
                    return str;
                }

                var start = args[0] is double d1 ? (int)d1 : 0;
                var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : str.Length;

                if (start < 0)
                {
                    start = Math.Max(0, str.Length + start);
                }
                else
                {
                    start = Math.Min(start, str.Length);
                }

                if (end < 0)
                {
                    end = Math.Max(0, str.Length + end);
                }
                else
                {
                    end = Math.Min(end, str.Length);
                }

                if (start >= end)
                {
                    return "";
                }

                return str.Substring(start, end - start);
            }));

            var supFn = new HostFunction((thisValue, _) =>
            {
                if (thisValue is null || (thisValue is Symbol sym && ReferenceEquals(sym, Symbols.Undefined)))
                {
                    throw ThrowTypeError("String.prototype.sup called on null or undefined");
                }

                var s = JsValueToString(thisValue);
                return $"<sup>{s}</sup>";
            }) { IsConstructor = false };

            supFn.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });

            stringProtoObj.SetProperty("sup", supFn);
        }

        // String.fromCodePoint(...codePoints)
        stringConstructor.SetProperty("fromCodePoint", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var result = new StringBuilder();
            foreach (var arg in args)
            {
                var num = JsOps.ToNumber(arg);
                if (double.IsNaN(num) || double.IsInfinity(num))
                {
                    continue;
                }

                var codePoint = (int)num;
                // Validate code point range
                if (codePoint is < 0 or > 0x10FFFF)
                {
                    throw new Exception("RangeError: Invalid code point " + codePoint);
                }

                // Handle surrogate pairs for code points > 0xFFFF
                if (codePoint <= 0xFFFF)
                {
                    result.Append((char)codePoint);
                }
                else
                {
                    codePoint -= 0x10000;
                    result.Append((char)(0xD800 + (codePoint >> 10)));
                    result.Append((char)(0xDC00 + (codePoint & 0x3FF)));
                }
            }

            return result.ToString();
        }));

        // String.fromCharCode(...charCodes) - for compatibility
        stringConstructor.SetProperty("fromCharCode", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var result = new StringBuilder();
            foreach (var arg in args)
            {
                var num = JsOps.ToNumber(arg);
                if (double.IsNaN(num) || double.IsInfinity(num))
                {
                    continue;
                }

                var charCode = (int)num & 0xFFFF; // Limit to 16-bit range
                result.Append((char)charCode);
            }

            return result.ToString();
        }));

        // String.raw(template, ...substitutions)
        // This is a special method used with tagged templates
        stringConstructor.SetProperty("raw", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            // First argument should be a template object with 'raw' property
            if (args[0] is not JsObject template)
            {
                return "";
            }

            // Get the raw strings array
            if (!template.TryGetProperty("raw", out var rawValue) || rawValue is not JsArray rawStrings)
            {
                return "";
            }

            var result = new StringBuilder();
            var rawCount = rawStrings.Items.Count;

            for (var i = 0; i < rawCount; i++)
            {
                // Append the raw string part
                var rawPart = rawStrings.GetElement(i)?.ToString() ?? "";
                result.Append(rawPart);

                // Append the substitution if there is one
                if (i >= args.Count - 1)
                {
                    continue;
                }

                var substitution = args[i + 1];
                if (substitution != null)
                {
                    result.Append(substitution);
                }
            }

            return result.ToString();
        }));

        // String.escape(string) - deprecated but used in some old code
        // Escapes special characters for use in URIs or HTML
        stringConstructor.SetProperty("escape", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return "";
            }

            var str = args[0]?.ToString() ?? "";

            var result = new StringBuilder();
            foreach (var ch in str)
            {
                // Characters that don't need escaping
                if (ch is >= 'A' and <= 'Z' ||
                    ch is >= 'a' and <= 'z' ||
                    ch is >= '0' and <= '9' ||
                    ch == '@' || ch == '*' || ch == '_' ||
                    ch == '+' || ch == '-' || ch == '.' || ch == '/')
                {
                    result.Append(ch);
                }
                // Characters that need hex escaping
                else if (ch < 256)
                {
                    result.Append('%');
                    result.Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                }
                // Unicode characters use %uXXXX format
                else
                {
                    result.Append("%u");
                    result.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return result.ToString();
        }));

        return stringConstructor;
    }
}
