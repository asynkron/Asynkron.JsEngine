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
        // charAt(index)
        stringObj.SetProperty("charAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length)
            {
                return "";
            }

            return str[index].ToString();
        }));

        // charCodeAt(index)
        stringObj.SetProperty("charCodeAt", new HostFunction(args =>
        {
            var index = args.Count > 0 && args[0] is double d ? (int)d : 0;
            if (index < 0 || index >= str.Length)
            {
                return double.NaN;
            }

            return (double)str[index];
        }));

        // indexOf(searchString, position?)
        stringObj.SetProperty("indexOf", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            var result = str.IndexOf(searchStr, position, StringComparison.Ordinal);
            return (double)result;
        }));

        // lastIndexOf(searchString, position?)
        stringObj.SetProperty("lastIndexOf", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Min((int)d, str.Length - 1) : str.Length - 1;
            var result = position >= 0 ? str.LastIndexOf(searchStr, position, StringComparison.Ordinal) : -1;
            return (double)result;
        }));

        // substring(start, end?)
        stringObj.SetProperty("substring", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var start = args[0] is double d1 ? Math.Max(0, Math.Min((int)d1, str.Length)) : 0;
            var end = args.Count > 1 && args[1] is double d2 ? Math.Max(0, Math.Min((int)d2, str.Length)) : str.Length;

            // JavaScript substring swaps if start > end
            if (start > end)
            {
                (start, end) = (end, start);
            }

            return str.Substring(start, end - start);
        }));

        // slice(start, end?)
        stringObj.SetProperty("slice", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var start = args[0] is double d1 ? (int)d1 : 0;
            var end = args.Count > 1 && args[1] is double d2 ? (int)d2 : str.Length;

            // Handle negative indices
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

        // substr(start, length?)
        stringObj.SetProperty("substr", new HostFunction(args =>
        {
            var length = str.Length;
            if (args.Count == 0)
            {
                return str;
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

            return str.Substring(start, substrLength);
        }));

        // concat(...strings)
        stringObj.SetProperty("concat", new HostFunction(args =>
        {
            var result = str;
            foreach (var arg in args)
            {
                result += JsValueToString(arg);
            }

            return result;
        }));

        // toLowerCase()
        stringObj.SetProperty("toLowerCase", new HostFunction(_ => str.ToLowerInvariant()));

        // toUpperCase()
        stringObj.SetProperty("toUpperCase", new HostFunction(_ => str.ToUpperInvariant()));

        // trim()
        stringObj.SetProperty("trim", new HostFunction(_ => str.Trim()));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(_ => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(_ => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(_ => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(_ => str.TrimEnd()));

        // split(separator, limit?)
        stringObj.SetProperty("split", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateArrayFromStrings([str], realm);
            }

            var separator = args[0]?.ToString();
            var limit = args.Count > 1 && args[1] is double d ? (int)d : int.MaxValue;

            if (separator is null or "")
            {
                // Split into individual characters
                var chars = str.Select(c => c.ToString()).Take(limit).ToArray();
                return CreateArrayFromStrings(chars, realm);
            }

            var parts = str.Split([separator], StringSplitOptions.None);
            if (limit < parts.Length)
            {
                parts = parts.Take(limit).ToArray();
            }

            return CreateArrayFromStrings(parts, realm);
        }));

        // replace(searchValue, replaceValue)
        stringObj.SetProperty("replace", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return str;
            }

            var search = args[0];
            var replacement = args[1];

            // Function-replacer form: str.replace(pattern, (match) => ...)
            if (replacement is IJsCallable replacer)
            {
                // Regex search
                if (search is JsObject regexObj &&
                    regexObj.TryGetProperty("__regex__", out var regexValue) &&
                    regexValue is JsRegExp regex)
                {
                    var dotNetRegex = new Regex(regex.Pattern);
                    var result = new StringBuilder();
                    var lastIndex = 0;

                    if (regex.Global)
                    {
                        var matches = dotNetRegex.Matches(str);
                        if (matches.Count == 0)
                        {
                            return str;
                        }

                        foreach (Match match in matches)
                        {
                            if (!match.Success)
                            {
                                continue;
                            }

                            if (match.Index > lastIndex)
                            {
                                result.Append(str.AsSpan(lastIndex, match.Index - lastIndex));
                            }

                            var replacementValue = replacer.Invoke([match.Value], str);
                            var replacementString = replacementValue.ToJsString();
                            result.Append(replacementString);

                            lastIndex = match.Index + match.Length;
                        }
                    }
                    else
                    {
                        var match = dotNetRegex.Match(str);
                        if (!match.Success)
                        {
                            return str;
                        }

                        if (match.Index > 0)
                        {
                            result.Append(str.AsSpan(0, match.Index));
                        }

                        var replacementValue = replacer.Invoke([match.Value], str);
                        var replacementString = replacementValue.ToJsString();
                        result.Append(replacementString);

                        lastIndex = match.Index + match.Length;
                    }

                    if (lastIndex < str.Length)
                    {
                        result.Append(str.AsSpan(lastIndex));
                    }

                    return result.ToString();
                }

                // String search with function replacer: only first occurrence
                var searchValueFunc = search?.ToString() ?? "";
                if (searchValueFunc.Length == 0)
                {
                    var replacementValue = replacer.Invoke([""], str);
                    var replacementString = replacementValue.ToJsString();
                    return replacementString + str;
                }

                var idx = str.IndexOf(searchValueFunc, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return str;
                }

                var prefix = str[..idx];
                var suffix = str[(idx + searchValueFunc.Length)..];
                var replacedSegment = replacer.Invoke([searchValueFunc], str).ToJsString();
                return prefix + replacedSegment + suffix;
            }

            // Non-function replacer: existing behavior.

            // Check if first argument is a RegExp (JsObject with __regex__ property)
            if (search is JsObject regexObj2 && regexObj2.TryGetProperty("__regex__", out var regexValue2) &&
                regexValue2 is JsRegExp regex2)
            {
                var replaceValue = replacement?.ToString() ?? "";
                if (regex2.Global)
                {
                    return Regex.Replace(str, regex2.Pattern, replaceValue);
                }

                var match = Regex.Match(str, regex2.Pattern);
                if (match.Success)
                {
                    return string.Concat(str.AsSpan(0, match.Index), replaceValue,
                        str.AsSpan(match.Index + match.Length));
                }

                return str;
            }

            // String replacement (only first occurrence)
            var searchValue = search?.ToString() ?? "";
            var replaceStr = replacement?.ToString() ?? "";
            var index = str.IndexOf(searchValue, StringComparison.Ordinal);
            if (index == -1)
            {
                return str;
            }

            return string.Concat(str.AsSpan(0, index), replaceStr, str.AsSpan(index + searchValue.Length));
        }));

        // match(regexp)
        stringObj.SetProperty("match", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is not JsObject regexObj || !regexObj.TryGetProperty("__regex__", out var regexValue) ||
                regexValue is not JsRegExp regex)
            {
                return null;
            }

            if (regex.Global)
            {
                return regex.MatchAll(str);
            }

            return regex.Exec(str);

        }));

        // search(regexp)
        stringObj.SetProperty("search", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return -1d;
            }

            if (args[0] is not JsObject regexObj || !regexObj.TryGetProperty("__regex__", out var regexValue) ||
                regexValue is not JsRegExp regex)
            {
                return -1d;
            }

            var result = regex.Exec(str);
            if (result is JsArray arr && arr.TryGetProperty("index", out var indexObj) &&
                indexObj is double d)
            {
                return d;
            }

            return -1d;
        }));

        // startsWith(searchString, position?)
        stringObj.SetProperty("startsWith", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? (int)d : 0;
            if (position < 0 || position >= str.Length)
            {
                return false;
            }

            return str[position..].StartsWith(searchStr, StringComparison.Ordinal);
        }));

        // endsWith(searchString, length?)
        stringObj.SetProperty("endsWith", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var length = args.Count > 1 && args[1] is double d ? (int)d : str.Length;
            if (length < 0)
            {
                return false;
            }

            length = Math.Min(length, str.Length);
            return str[..length].EndsWith(searchStr, StringComparison.Ordinal);
        }));

        // includes(searchString, position?)
        stringObj.SetProperty("includes", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return true;
            }

            var searchStr = args[0]?.ToString() ?? "";
            var position = args.Count > 1 && args[1] is double d ? Math.Max(0, (int)d) : 0;
            if (position >= str.Length)
            {
                return searchStr?.Length == 0;
            }

            return str.IndexOf(searchStr, position, StringComparison.Ordinal) >= 0;
        }));

        // repeat(count)
        stringObj.SetProperty("repeat", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return "";
            }

            var count = (int)d;
            if (count is < 0 or int.MaxValue)
            {
                return ""; // JavaScript throws RangeError, we return empty
            }

            if (count == 0)
            {
                return "";
            }

            return string.Concat(Enumerable.Repeat(str, count));
        }));

        // padStart(targetLength, padString?)
        stringObj.SetProperty("padStart", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length)
            {
                return str;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return str;
            }

            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(padding.AsSpan(0, padLength), str);
        }));

        // padEnd(targetLength, padString?)
        stringObj.SetProperty("padEnd", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return str;
            }

            var targetLength = args[0] is double d ? (int)d : 0;
            if (targetLength <= str.Length)
            {
                return str;
            }

            var padString = args.Count > 1 ? args[1]?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return str;
            }

            var padLength = targetLength - str.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return string.Concat(str, padding.AsSpan(0, padLength));
        }));

        // replaceAll(searchValue, replaceValue)
        stringObj.SetProperty("replaceAll", new HostFunction(args =>
        {
            if (args.Count < 2)
            {
                return str;
            }

            var searchValue = args[0]?.ToString() ?? "";
            var replaceValue = args[1]?.ToString() ?? "";
            return str.Replace(searchValue, replaceValue);
        }));

        // at(index)
        stringObj.SetProperty("at", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            // Handle negative indices
            if (index < 0)
            {
                index = str.Length + index;
            }

            if (index < 0 || index >= str.Length)
            {
                return null;
            }

            return str[index].ToString();
        }));

        // trimStart() / trimLeft()
        stringObj.SetProperty("trimStart", new HostFunction(_ => str.TrimStart()));
        stringObj.SetProperty("trimLeft", new HostFunction(_ => str.TrimStart()));

        // trimEnd() / trimRight()
        stringObj.SetProperty("trimEnd", new HostFunction(_ => str.TrimEnd()));
        stringObj.SetProperty("trimRight", new HostFunction(_ => str.TrimEnd()));

        // codePointAt(index)
        stringObj.SetProperty("codePointAt", new HostFunction(args =>
        {
            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            if (index < 0 || index >= str.Length)
            {
                return null;
            }

            // Get the code point at the given position
            // Handle surrogate pairs for characters outside the BMP (Basic Multilingual Plane)
            var c = str[index];
            if (!char.IsHighSurrogate(c) || index + 1 >= str.Length)
            {
                return (double)c;
            }

            var low = str[index + 1];
            if (!char.IsLowSurrogate(low))
            {
                return (double)c;
            }

            // Calculate the code point from the surrogate pair
            var high = (int)c;
            var lowInt = (int)low;
            var codePoint = ((high - 0xD800) << 10) + (lowInt - 0xDC00) + 0x10000;
            return (double)codePoint;

        }));

        // localeCompare(compareString)
        stringObj.SetProperty("localeCompare", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return 0d;
            }

            var compareString = args[0]?.ToString() ?? "";
            var result = string.Compare(str, compareString, StringComparison.CurrentCulture);
            return (double)result;
        }));

        // normalize(form) - Unicode normalization
        stringObj.SetProperty("normalize", new HostFunction(args =>
        {
            var form = args.Count > 0 && args[0] != null ? args[0]!.ToString() : "NFC";

            try
            {
                return form switch
                {
                    "NFC" => str.Normalize(NormalizationForm.FormC),
                    "NFD" => str.Normalize(NormalizationForm.FormD),
                    "NFKC" => str.Normalize(NormalizationForm.FormKC),
                    "NFKD" => str.Normalize(NormalizationForm.FormKD),
                    _ => throw new Exception(
                        "RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
                };
            }
            catch
            {
                return str;
            }
        }));

        // matchAll(regexp) - returns an array of all matches
        stringObj.SetProperty("matchAll", new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return null;
            }

            if (args[0] is JsObject regexObj && regexObj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp regex)
            {
                return regex.MatchAll(str);
            }

            // If not a RegExp, convert to one
            var pattern = args[0]?.ToString() ?? "";
            var tempRegex = new JsRegExp(pattern, "g");
            return tempRegex.MatchAll(str);
        }));

        // anchor(name) - deprecated HTML wrapper method
        stringObj.SetProperty("anchor", new HostFunction(args =>
        {
            var name = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            // Escape quotes in name
            name = name.Replace("\"", "&quot;");
            return $"<a name=\"{name}\">{str}</a>";
        }));

        // link(url) - deprecated HTML wrapper method
        stringObj.SetProperty("link", new HostFunction(args =>
        {
            var url = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            // Escape quotes in url
            url = url.Replace("\"", "&quot;");
            return $"<a href=\"{url}\">{str}</a>";
        }));

        // Set up Symbol.iterator for string
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        // Create iterator function that returns an iterator object
        var iteratorFunction = new HostFunction((_, _) =>
        {
            // Use array to hold index so it can be mutated in closure
            var indexHolder = new[] { 0 };
            var iterator = new JsObject();

            // Add next() method to iterator
            iterator.SetProperty("next", new HostFunction((_, _) =>
            {
                var result = new JsObject();
                if (indexHolder[0] < str.Length)
                {
                    result.SetProperty("value", str[indexHolder[0]].ToString());
                    result.SetProperty("done", false);
                    indexHolder[0]++;
                }
                else
                {
                    result.SetProperty("value", Symbols.Undefined);
                    result.SetProperty("done", true);
                }

                return result;
            }));

            return iterator;
        });

        stringObj.SetProperty(iteratorKey, iteratorFunction);
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
