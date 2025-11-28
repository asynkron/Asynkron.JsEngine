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

        // Define non-enumerable length like native String objects.
        stringObj.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)str.Length, Writable = false, Enumerable = false, Configurable = false,
                HasValue = true, HasWritable = true, HasEnumerable = true, HasConfigurable = true
            });

        // Expose indexed characters as enumerable, non-writable virtual properties to
        // avoid allocating per-character descriptors for large strings.
        stringObj.SetVirtualPropertyProvider(new StringVirtualPropertyProvider(str));

        var realmState = realm ?? context?.RealmState;
        var prototype = realmState?.StringPrototype;
        if (prototype is not null)
        {
            EnsureStringPrototypeMethods(prototype, realmState);
            stringObj.SetPrototype(prototype);
        }
        else
        {
            // Fallback when no realm prototype is available yet.
            AddStringMethods(stringObj, realmState, forceAttach: true);
        }

        return stringObj;
    }

    private sealed class StringVirtualPropertyProvider(string value) : IVirtualPropertyProvider
    {
        public bool TryGetOwnProperty(string name, out object? valueOut, out PropertyDescriptor? descriptor)
        {
            valueOut = null;
            descriptor = null;

            if (!IsArrayIndex(name, out var index) || index < 0 || index >= value.Length)
            {
                return false;
            }

            var ch = value[index].ToString();
            valueOut = ch;
            descriptor = new PropertyDescriptor
            {
                Value = ch,
                Writable = false,
                Enumerable = true,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            };
            return true;
        }

        public IEnumerable<string> GetEnumerableKeys()
        {
            for (var i = 0; i < value.Length; i++)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool IsArrayIndex(string key, out int index)
        {
            return int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
        }
    }

    private static void EnsureStringPrototypeMethods(JsObject prototype, RealmState? realm)
    {
        AddStringMethods(prototype, realm);
    }

    /// <summary>
    ///     Attaches string instance methods to a target object (typically String.prototype).
    /// </summary>
    private static void AddStringMethods(JsObject stringObj, RealmState? realm, bool forceAttach = false)
    {
        var matchKey = $"@@symbol:{TypedAstSymbol.For("Symbol.match").GetHashCode()}";
        var matchAllKey = $"@@symbol:{TypedAstSymbol.For("Symbol.matchAll").GetHashCode()}";
        var replaceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.replace").GetHashCode()}";
        var searchKey = $"@@symbol:{TypedAstSymbol.For("Symbol.search").GetHashCode()}";
        var splitKey = $"@@symbol:{TypedAstSymbol.For("Symbol.split").GetHashCode()}";
        if (!forceAttach &&
            realm is { StringPrototype: { } proto, StringPrototypeMethodsInitialized: true } &&
            ReferenceEquals(stringObj, proto))
        {
            return;
        }

        stringObj.SetHostedProperty("charAt", CharAt);
        stringObj.SetHostedProperty("charCodeAt", CharCodeAt);
        stringObj.SetHostedProperty("indexOf", IndexOf);
        stringObj.SetHostedProperty("lastIndexOf", LastIndexOf);
        stringObj.SetHostedProperty("substring", Substring);
        stringObj.SetHostedProperty("slice", Slice);
        var substrFn = new HostFunction(Substr) { IsConstructor = false };
        DefineBuiltinFunction(stringObj, "substr", substrFn, 2, isConstructor: false);
        stringObj.SetHostedProperty("concat", Concat);
        stringObj.SetHostedProperty("toLowerCase", ToLowerCase);
        stringObj.SetHostedProperty("toUpperCase", ToUpperCase);
        var trimStartFn = new HostFunction(TrimStart) { IsConstructor = false };
        DefineBuiltinFunction(stringObj, "trimStart", trimStartFn, 0, isConstructor: false);
        stringObj.DefineProperty("trimLeft",
            new PropertyDescriptor { Value = trimStartFn, Writable = true, Enumerable = false, Configurable = true });

        var trimEndFn = new HostFunction(TrimEnd) { IsConstructor = false };
        DefineBuiltinFunction(stringObj, "trimEnd", trimEndFn, 0, isConstructor: false);
        stringObj.DefineProperty("trimRight",
            new PropertyDescriptor { Value = trimEndFn, Writable = true, Enumerable = false, Configurable = true });

        stringObj.SetHostedProperty("trim", Trim);
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
        DefineBuiltinFunction(stringObj, "small", new HostFunction(Small), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "strike", new HostFunction(Strike), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "sub", new HostFunction(Sub), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "sup", new HostFunction(Sup), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "anchor", new HostFunction(Anchor), 1, isConstructor: false);
        DefineBuiltinFunction(stringObj, "big", new HostFunction(Big), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "blink", new HostFunction(Blink), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "bold", new HostFunction(Bold), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "fixed", new HostFunction(Fixed), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "fontcolor", new HostFunction(FontColor), 1, isConstructor: false);
        DefineBuiltinFunction(stringObj, "fontsize", new HostFunction(FontSize), 1, isConstructor: false);
        DefineBuiltinFunction(stringObj, "italics", new HostFunction(Italics), 0, isConstructor: false);
        DefineBuiltinFunction(stringObj, "link", new HostFunction(Link), 1, isConstructor: false);

        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        stringObj.SetHostedProperty(iteratorKey, CreateIterator);

        if (!forceAttach && realm is not null && ReferenceEquals(stringObj, realm.StringPrototype))
        {
            realm.StringPrototypeMethodsInitialized = true;
        }
        return;

        string ResolveString(object? thisValue)
        {
            var context = realm is not null ? new EvaluationContext(realm) : null;
            if (ReferenceEquals(thisValue, Symbols.Undefined) || thisValue is null)
            {
                throw ThrowTypeError("Cannot convert undefined or null to object", realm: realm);
            }

            var str = JsOps.ToJsString(thisValue, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return str;
        }

        string CoerceToString(object? value)
        {
            var context = realm is not null ? new EvaluationContext(realm) : null;
            var result = JsOps.ToJsString(value, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return result;
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

            var ctx = realm is not null ? new EvaluationContext(realm) : null;
            var startNumber = ToIntegerOrInfinityLocal(args[0], ctx);
            if (ctx?.IsThrow == true)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            var start = double.IsNegativeInfinity(startNumber) ? 0 : (int)startNumber;
            if (start < 0)
            {
                start = Math.Max(0, length + start);
            }
            else if (start >= length)
            {
                return "";
            }

            double lengthNumber;
            if (args.Count > 1)
            {
                lengthNumber = ToIntegerOrInfinityLocal(args[1], ctx);
                if (ctx?.IsThrow == true)
                {
                    throw new ThrowSignal(ctx.FlowValue);
                }
            }
            else
            {
                lengthNumber = double.PositiveInfinity;
            }

            if (double.IsNaN(lengthNumber) || lengthNumber <= 0)
            {
                return "";
            }

            var substrLength = (int)Math.Min(lengthNumber, length - start);
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

            var separatorValue = args[0];
            var splitMethod = GetMethod(separatorValue, splitKey, "@@split");
            if (splitMethod is not null)
            {
                var limitArg = args.Count > 1 ? args[1] : Symbols.Undefined;
                return splitMethod.Invoke([value, limitArg], separatorValue);
            }

            var separator = ReferenceEquals(separatorValue, Symbols.Undefined)
                ? null
                : CoerceToString(separatorValue);
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
            var search = args.Count > 0 ? args[0] : Symbols.Undefined;
            var replacement = args.Count > 1 ? args[1] : Symbols.Undefined;

            var replaceMethod = GetMethod(search, replaceKey, "@@replace");
            if (replaceMethod is not null)
            {
                return replaceMethod.Invoke([value, replacement], search);
            }

            if (replacement is IJsCallable replacer)
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

                var searchValueFunc = CoerceToString(search);
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

            if (TryResolveRegExp(search, out var regex2))
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

            var searchValue = CoerceToString(search);
            var replaceStr = CoerceToString(replacement);
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

            var searchValue = args[0];
            var matcher = GetMethod(searchValue, matchKey, "@@match");
            if (matcher is not null)
            {
                return matcher.Invoke([value], searchValue);
            }

            var regex = ToRegExpValue(searchValue, string.Empty, requireGlobal: false);
            return regex.Global ? regex.MatchAll(value) : regex.Exec(value);
        }

        object? Search(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return -1d;
            }

            var searchValue = args[0];
            var searchMethod = GetMethod(searchValue, searchKey, "@@search");
            if (searchMethod is not null)
            {
                return searchMethod.Invoke([value], searchValue);
            }

            var regex = ToRegExpValue(searchValue, string.Empty, requireGlobal: false);
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
            var searchValue = args.Count > 0 ? args[0] : Symbols.Undefined;
            var replaceValue = args.Count > 1 ? args[1] : Symbols.Undefined;

            var replaceMethod = GetMethod(searchValue, replaceKey, "@@replace");
            if (replaceMethod is not null)
            {
                return replaceMethod.Invoke([value, replaceValue], searchValue);
            }

            if (TryResolveRegExp(searchValue, out var regex))
            {
                if (!regex.Global)
                {
                    throw ThrowTypeError("String.prototype.replaceAll called with a non-global RegExp", realm: realm);
                }

                var replaceStr = CoerceToString(replaceValue);
                return Regex.Replace(value, regex.Pattern, replaceStr);
            }

            if (replaceValue is IJsCallable replacer)
            {
                var searchStrFunc = CoerceToString(searchValue);
                if (searchStrFunc.Length == 0)
                {
                    var replacementValue = replacer.Invoke([""], value).ToJsString();
                    var builder = new StringBuilder();
                    builder.Append(replacementValue);
                    foreach (var ch in value)
                    {
                        builder.Append(ch);
                        builder.Append(replacementValue);
                    }

                    return builder.ToString();
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
                    var replacementValue = replacer.Invoke([searchStrFunc], value);
                    var replacementString = replacementValue.ToJsString();
                    result.Append(replacementString);
                    currentIndex = idx + searchStrFunc.Length;
                }

                return result.ToString();
            }

            var searchStr = CoerceToString(searchValue);
            var replaceStrPlain = CoerceToString(replaceValue);
            return value.Replace(searchStr, replaceStrPlain);
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

            var matcher = args[0];
            var method = GetMethod(matcher, matchAllKey, "@@matchAll");
            if (method is not null)
            {
                return method.Invoke([value], matcher);
            }

            var regex = ToRegExpValue(matcher, "g", requireGlobal: true);
            return regex.MatchAll(value);
        }

        object? Anchor(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var name = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return $"<a name=\"{EscapeAttr(name)}\">{value}</a>";
        }

        object? Link(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var url = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return $"<a href=\"{EscapeAttr(url)}\">{value}</a>";
        }

        object? Bold(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<b>{value}</b>";
        }

        object? Italics(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<i>{value}</i>";
        }

        object? Fixed(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<tt>{value}</tt>";
        }

        object? Blink(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<blink>{value}</blink>";
        }

        object? Big(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<big>{value}</big>";
        }

        object? FontColor(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var color = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return $"<font color=\"{EscapeAttr(color)}\">{value}</font>";
        }

        object? FontSize(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            var size = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return $"<font size=\"{EscapeAttr(size)}\">{value}</font>";
        }

        object? Small(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<small>{value}</small>";
        }

        object? Strike(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<strike>{value}</strike>";
        }

        object? Sub(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<sub>{value}</sub>";
        }

        object? Sup(object? thisValue, IReadOnlyList<object?> args)
        {
            var value = ResolveString(thisValue);
            return $"<sup>{value}</sup>";
        }

        bool TryResolveRegExp(object? candidate, out JsRegExp regex)
        {
            if (candidate is JsRegExp direct)
            {
                regex = direct;
                return true;
            }

            if (candidate is JsObject obj &&
                obj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp stored)
            {
                regex = stored;
                return true;
            }

            regex = null!;
            return false;
        }

        IJsCallable? GetMethod(object? value, string methodKey, string opName)
        {
            if (!JsOps.TryGetPropertyValue(value, methodKey, out var method, null))
            {
                return null;
            }

            if (method is null || ReferenceEquals(method, Symbols.Undefined))
            {
                return null;
            }

            if (method is not IJsCallable callable)
            {
                throw ThrowTypeError($"{opName} is not callable", realm: realm);
            }

            return callable;
        }

        JsRegExp ToRegExpValue(object? candidate, string defaultFlags, bool requireGlobal)
        {
            if (candidate is JsRegExp direct)
            {
                if (requireGlobal && !direct.Global)
                {
                    throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
                }

                return direct;
            }

            if (candidate is JsObject obj &&
                obj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue is JsRegExp stored)
            {
                if (requireGlobal && !stored.Global)
                {
                    throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
                }

                return stored;
            }

            var ctx = realm is not null ? new EvaluationContext(realm) : null;
            var pattern = ReferenceEquals(candidate, Symbols.Undefined) ? string.Empty : JsOps.ToJsString(candidate, ctx);
            if (ctx?.IsThrow == true)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            var created = new JsRegExp(pattern, defaultFlags ?? string.Empty, realm);
            if (requireGlobal && !created.Global)
            {
                throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
            }

            return created;
        }

        static double ToIntegerOrInfinityLocal(object? value, EvaluationContext? context)
        {
            var number = JsOps.ToNumberWithContext(value, context);
            if (context is not null && context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            if (double.IsNaN(number))
            {
                return 0;
            }

            if (double.IsInfinity(number) || number == 0)
            {
                return number;
            }

            return Math.Sign(number) * Math.Floor(Math.Abs(number));
        }

        static string EscapeAttr(string input)
        {
            return input.Replace("\"", "&quot;");
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
            var context = realm is not null ? new EvaluationContext(realm) : null;
            var str = JsOps.ToJsString(value, context);

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

            EnsureStringPrototypeMethods(stringProtoObj, realm);

            DefineBuiltinFunction(stringProtoObj, "toString", new HostFunction(StringPrototypeToString), 0,
                isConstructor: false);
            DefineBuiltinFunction(stringProtoObj, "valueOf", new HostFunction(StringPrototypeValueOf), 0,
                isConstructor: false);
            DefineBuiltinFunction(stringProtoObj, "parseJSON",
                new HostFunction((thisValue, args, realmState) =>
                {
                    realmState ??= realm;
                    var context = realmState is not null ? new EvaluationContext(realmState) : null;
                    var source = JsOps.ToJsString(thisValue, context);
                    var reviver = args.Count > 0 ? args[0] : null;
                    return ParseJsonWithReviver(source, realmState!, context, reviver);
                }, realm), 1, isConstructor: false, writable: false, enumerable: false, configurable: true);
        }

        // String.fromCodePoint(...codePoints)
        stringConstructor.SetHostedProperty("fromCodePoint", StringFromCodePoint);

        // String.fromCharCode(...charCodes) - for compatibility
        stringConstructor.SetHostedProperty("fromCharCode", StringFromCharCode);

        // String.raw(template, ...substitutions)
        // This is a special method used with tagged templates
        stringConstructor.SetHostedProperty("raw", StringRaw);

        // String.escape(string) - deprecated but used in some old code
        // Escapes special characters for use in URIs or HTML
        stringConstructor.SetHostedProperty("escape", StringEscape);

        return stringConstructor;

        object? StringPrototypeToString(object? thisValue, IReadOnlyList<object?> _)
        {
            return RequireStringReceiver(thisValue);
        }

        object? StringPrototypeValueOf(object? thisValue, IReadOnlyList<object?> _)
        {
            return RequireStringReceiver(thisValue);
        }

        string RequireStringReceiver(object? receiver)
        {
            return receiver switch
            {
                string s => s,
                JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is string s => s,
                IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var inner)
                    && inner is string s => s,
                _ => throw ThrowTypeError("String.prototype valueOf called on non-string object", realm: realm)
            };
        }

        object? StringFromCodePoint(IReadOnlyList<object?> args)
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
                if (codePoint is < 0 or > 0x10FFFF)
                {
                    throw new Exception("RangeError: Invalid code point " + codePoint);
                }

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
        }

        object? StringFromCharCode(IReadOnlyList<object?> args)
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

                var charCode = (int)num & 0xFFFF;
                result.Append((char)charCode);
            }

            return result.ToString();
        }

        object? StringRaw(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return "";
            }

            if (args[0] is not JsObject template)
            {
                return "";
            }

            if (!template.TryGetProperty("raw", out var rawValue) || rawValue is not JsArray rawStrings)
            {
                return "";
            }

            var result = new StringBuilder();
            var rawCount = rawStrings.Items.Count;

            for (var i = 0; i < rawCount; i++)
            {
                var rawPart = rawStrings.GetElement(i)?.ToString() ?? "";
                result.Append(rawPart);

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
        }

        object? StringEscape(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
            {
                return "";
            }

            var str = args[0]?.ToString() ?? "";

            var result = new StringBuilder();
            foreach (var ch in str)
            {
                if (ch is >= 'A' and <= 'Z' ||
                    ch is >= 'a' and <= 'z' ||
                    ch is >= '0' and <= '9' ||
                    ch == '@' || ch == '*' || ch == '_' ||
                    ch == '+' || ch == '-' || ch == '.' || ch == '/')
                {
                    result.Append(ch);
                }
                else if (ch < 256)
                {
                    result.Append('%');
                    result.Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    result.Append("%u");
                    result.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return result.ToString();
        }
    }
}
