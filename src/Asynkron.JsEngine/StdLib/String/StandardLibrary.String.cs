using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static JsObject InitializeStringWrapper(string str, JsObject wrapper, RealmState? realm = null)
    {
        wrapper.SetProperty("__value__", str);

        wrapper.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)str.Length,
                Writable = false,
                Enumerable = false,
                Configurable = false,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });
        wrapper.SetVirtualPropertyProvider(new StringVirtualPropertyProvider(str));
        wrapper.RealmState ??= realm;
        return wrapper;
    }

    internal static string RequireStringReceiver(object? receiver, RealmState? realm = null)
    {
        return receiver switch
        {
            string s => s,
            JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner.TryGetString(out var s) => s,
            IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var inner)
                                              && inner.TryGetString(out var s) => s,
            _ => throw ThrowTypeError("String.prototype valueOf called on non-string object", realm: realm)
        };
    }

    /// <summary>
    ///     Creates a string wrapper object with string methods attached.
    ///     This allows string primitives to have methods like toLowerCase(), substring(), etc.
    /// </summary>
    public static JsObject CreateStringWrapper(string str, EvaluationContext? context = null, RealmState? realm = null)
    {
        var stringObj = InitializeStringWrapper(str, new JsObject(), realm);

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
            AddStringMethods(stringObj, realmState, true);
        }

        return stringObj;
    }

    private static void EnsureStringPrototypeMethods(JsObject prototype, RealmState? realm)
    {
        AddStringMethods(prototype, realm);
    }

    /// <summary>
    ///     Attaches string instance methods to a target object (typically String.prototype).
    /// </summary>
    internal static void AddStringMethods(JsObject stringObj, RealmState? realm, bool forceAttach = false)
    {
        var matchKey = SymbolKeys.GetMatch(realm);
        var matchAllKey = SymbolKeys.GetMatchAll(realm);
        var replaceKey = SymbolKeys.GetReplace(realm);
        var searchKey = SymbolKeys.GetSearch(realm);
        var splitKey = SymbolKeys.GetSplit(realm);
        if (!forceAttach &&
            realm is { StringPrototype: { } proto, StringPrototypeMethodsInitialized: true } &&
            ReferenceEquals(stringObj, proto))
        {
            return;
        }

        DefineBuiltinFunction(stringObj, "charAt", new HostFunction(CharAt, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "charCodeAt", new HostFunction(CharCodeAt, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "indexOf", new HostFunction(IndexOf, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "lastIndexOf", new HostFunction(LastIndexOf, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "substring", new HostFunction(Substring, realm, isConstructor: false), 2);
        DefineBuiltinFunction(stringObj, "slice", new HostFunction(Slice, realm, isConstructor: false), 2);
        var substrFn = new HostFunction(Substr, realm, isConstructor: false);
        DefineBuiltinFunction(stringObj, "substr", substrFn, 2);
        DefineBuiltinFunction(stringObj, "concat", new HostFunction(Concat, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "toLowerCase", new HostFunction(ToLowerCase, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "toUpperCase", new HostFunction(ToUpperCase, realm, isConstructor: false), 0);
        var trimStartFn = new HostFunction(TrimStart, realm, isConstructor: false);
        DefineBuiltinFunction(stringObj, "trimStart", trimStartFn, 0);
        stringObj.DefineProperty("trimLeft",
            new PropertyDescriptor { Value = trimStartFn, Writable = true, Enumerable = false, Configurable = true });

        var trimEndFn = new HostFunction(TrimEnd, realm, isConstructor: false);
        DefineBuiltinFunction(stringObj, "trimEnd", trimEndFn, 0);
        stringObj.DefineProperty("trimRight",
            new PropertyDescriptor { Value = trimEndFn, Writable = true, Enumerable = false, Configurable = true });

        DefineBuiltinFunction(stringObj, "trim", new HostFunction(Trim, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "split", new HostFunction((thisValue, args) => Split(thisValue, args, realm), realm, isConstructor: false), 2);
        DefineBuiltinFunction(stringObj, "replace", new HostFunction(Replace, realm, isConstructor: false), 2);
        DefineBuiltinFunction(stringObj, "match", new HostFunction(Match, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "search", new HostFunction(Search, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "startsWith", new HostFunction(StartsWith, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "endsWith", new HostFunction(EndsWith, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "includes", new HostFunction(Includes, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "repeat", new HostFunction(Repeat, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "padStart", new HostFunction(PadStart, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "padEnd", new HostFunction(PadEnd, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "replaceAll", new HostFunction(ReplaceAll, realm, isConstructor: false), 2);
        DefineBuiltinFunction(stringObj, "at", new HostFunction(At, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "codePointAt", new HostFunction(CodePointAt, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "localeCompare", new HostFunction(LocaleCompare, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "normalize", new HostFunction(Normalize, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "matchAll", new HostFunction(MatchAll, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "small", new HostFunction(Small, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "strike", new HostFunction(Strike, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "sub", new HostFunction(Sub, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "sup", new HostFunction(Sup, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "anchor", new HostFunction(Anchor, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "big", new HostFunction(Big, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "blink", new HostFunction(Blink, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "bold", new HostFunction(Bold, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "fixed", new HostFunction(Fixed, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "fontcolor", new HostFunction(FontColor, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "fontsize", new HostFunction(FontSize, realm, isConstructor: false), 1);
        DefineBuiltinFunction(stringObj, "italics", new HostFunction(Italics, realm, isConstructor: false), 0);
        DefineBuiltinFunction(stringObj, "link", new HostFunction(Link, realm, isConstructor: false), 1);

        var iteratorKey = SymbolKeys.GetIterator(realm);

        DefineBuiltinFunction(stringObj, iteratorKey, new HostFunction(CreateIterator, realm, isConstructor: false), 0);

        if (!forceAttach && realm is not null && ReferenceEquals(stringObj, realm.StringPrototype))
        {
            realm.StringPrototypeMethodsInitialized = true;
        }

        return;

        string ResolveString(JsValue thisValue)
        {
            var context = realm?.CreateContext();
            if (thisValue.IsUndefined || thisValue.IsNull)
            {
                throw ThrowTypeError("Cannot convert undefined or null to object", realm: realm);
            }

            var str = thisValue.ToJsString(context, realm);
            return str;
        }

        string CoerceToString(JsValue value)
        {
            var context = realm?.CreateContext();
            var result = value.ToJsString(context, realm);
            return result;
        }

        string JsValueToString(JsValue value)
        {
            return value.ToJsString(realm?.CreateContext(), realm);
        }

        JsValue CharAt(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var index = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            if (index < 0 || index >= value.Length)
            {
                return new JsValue("");
            }

            return new JsValue(value[index].ToString());
        }

        JsValue CharCodeAt(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var index = args.Count > 0 && args[0].TryGetDouble(out var d) ? (int)d : 0;
            if (index < 0 || index >= value.Length)
            {
                return new JsValue(double.NaN);
            }

            return new JsValue((double)value[index]);
        }

        JsValue IndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(-1d);
            }

            var searchStr = args[0].TryGetString(out var s) ? s : args[0].ToObject()?.ToString() ?? "";
            var position = args.Count > 1 && args[1].TryGetDouble(out var d) ? Math.Max(0, (int)d) : 0;
            var result = value.IndexOf(searchStr, position, StringComparison.Ordinal);
            return new JsValue((double)result);
        }

        JsValue LastIndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(-1d);
            }

            var searchStr = args[0].TryGetString(out var s) ? s : args[0].ToObject()?.ToString() ?? "";
            var position = args.Count > 1 && args[1].TryGetDouble(out var d)
                ? Math.Min((int)d, value.Length - 1)
                : value.Length - 1;
            var result = position >= 0 ? value.LastIndexOf(searchStr, position, StringComparison.Ordinal) : -1;
            return new JsValue((double)result);
        }

        JsValue Substring(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(value);
            }

            var start = args[0].TryGetDouble(out var d1) ? Math.Max(0, Math.Min((int)d1, value.Length)) : 0;
            var end = args.Count > 1 && args[1].TryGetDouble(out var d2)
                ? Math.Max(0, Math.Min((int)d2, value.Length))
                : value.Length;

            if (start > end)
            {
                (start, end) = (end, start);
            }

            return new JsValue(value.Substring(start, end - start));
        }

        JsValue Slice(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(value);
            }

            var start = args[0].TryGetDouble(out var d1) ? (int)d1 : 0;
            var end = args.Count > 1 && args[1].TryGetDouble(out var d2) ? (int)d2 : value.Length;

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
                return new JsValue("");
            }

            return new JsValue(value.Substring(start, end - start));
        }

        JsValue Substr(JsValue thisValue, IReadOnlyList<JsValue> args)
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

            double ConvertToNumber(JsValue input)
            {
                if (input.TryGetObject<Symbol>(out _) || input.TryGetObject<TypedAstSymbol>(out _))
                {
                    throw new ThrowSignal(JsValue.FromObject(CreateTypeError("Cannot convert a Symbol value to a number",
                        null, realm)));
                }

                var numericContext = realm?.CreateContext();
                var primitive = JsOps.ToPrimitive(input.ToObject(), ToPrimitiveHint.Number, numericContext);
                if (numericContext?.IsThrow == true)
                {
                    throw new ThrowSignal(numericContext.FlowValue);
                }

                var number = JsOps.ToNumberWithContext(primitive, numericContext);
                if (numericContext?.IsThrow == true)
                {
                    var flowValue = numericContext.FlowValue;
                    throw new ThrowSignal(!flowValue.IsUndefined
                        ? flowValue
                        : JsValue.FromObject(CreateTypeError("Cannot convert object to primitive value", numericContext, realm)));
                }

                return number;
            }
        }

        JsValue Concat(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var result = ResolveString(thisValue);
            foreach (var arg in args)
            {
                result += JsValueToString(arg);
            }

            return new JsValue(result);
        }

        JsValue ToLowerCase(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            return new JsValue(ResolveString(thisValue).ToLowerInvariant());
        }

        JsValue ToUpperCase(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            return new JsValue(ResolveString(thisValue).ToUpperInvariant());
        }

        JsValue Trim(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            return new JsValue(ResolveString(thisValue).Trim());
        }

        JsValue TrimStart(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            return new JsValue(ResolveString(thisValue).TrimStart());
        }

        JsValue TrimEnd(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            return new JsValue(ResolveString(thisValue).TrimEnd());
        }

        JsValue Split(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realmState)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.FromObject(CreateArrayFromStrings([value], realmState ?? realm));
            }

            var separatorValue = args[0];
            var splitMethod = GetMethod(separatorValue, splitKey, "@@split");
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
                var chars = value.Select(c => c.ToString()).Take(limit).ToArray();
                return JsValue.FromObject(CreateArrayFromStrings(chars, realmState ?? realm));
            }

            var parts = value.Split([separator], StringSplitOptions.None);
            if (limit < parts.Length)
            {
                parts = parts.Take(limit).ToArray();
            }

            return JsValue.FromObject(CreateArrayFromStrings(parts, realmState ?? realm));
        }

        JsValue Replace(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var search = args.GetArgument(0);
            var replacement = args.GetArgument(1);

            var replaceMethod = GetMethod(search, replaceKey, "@@replace");
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

                            // Per ES spec, call replacer with `undefined` as this, not the string
                            var replacementValue = replacer.Invoke([new JsValue(match.Value)], JsValue.Undefined);
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

                        // Per ES spec, call replacer with `undefined` as this, not the string
                        var replacementValue = replacer.Invoke([new JsValue(match.Value)], JsValue.Undefined);
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
                    // Per ES spec, call replacer with `undefined` as this, not the string
                    var replacementValue = replacer.Invoke([new JsValue("")], JsValue.Undefined);
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
                // Per ES spec, call replacer with `undefined` as this, not the string
                var replacedSegment = replacer.Invoke([new JsValue(searchValueFunc)], JsValue.Undefined).ToJsString();
                return new JsValue(prefix + replacedSegment + suffix);
            }

            if (TryResolveRegExp(search, out var regex2))
            {
                var replaceValue = replacement.ToObject()?.ToString() ?? "";
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

        JsValue Match(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.Null;
            }

            var searchValue = args[0];
            var matcher = GetMethod(searchValue, matchKey, "@@match");
            if (matcher is not null)
            {
                return matcher.Invoke([new JsValue(value)], searchValue);
            }

            var regex = ToRegExpValue(searchValue, string.Empty, false);
            return JsValue.FromObject(regex.Global ? regex.MatchAll(value) : regex.Exec(value));
        }

        JsValue Search(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(-1d);
            }

            var searchValue = args[0];
            var searchMethod = GetMethod(searchValue, searchKey, "@@search");
            if (searchMethod is not null)
            {
                return searchMethod.Invoke([new JsValue(value)], searchValue);
            }

            var regex = ToRegExpValue(searchValue, string.Empty, false);
            var result = regex.Exec(value);
            if (result is JsArray arr && arr.TryGetProperty("index", out var indexObj) &&
                indexObj.TryGetDouble(out var d))
            {
                return new JsValue(d);
            }

            return new JsValue(-1d);
        }

        JsValue StartsWith(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.True;
            }

            var searchStr = args[0].ToObject()?.ToString() ?? "";
            var position = args.Count > 1 && args[1].TryGetDouble(out var d) ? (int)d : 0;
            if (position < 0 || position >= value.Length)
            {
                return JsValue.False;
            }

            return new JsValue(value[position..].StartsWith(searchStr, StringComparison.Ordinal));
        }

        JsValue EndsWith(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.True;
            }

            var searchStr = args[0].ToObject()?.ToString() ?? "";
            var length = args.Count > 1 && args[1].TryGetDouble(out var d) ? (int)d : value.Length;
            if (length < 0)
            {
                return JsValue.False;
            }

            length = Math.Min(length, value.Length);
            return new JsValue(value[..length].EndsWith(searchStr, StringComparison.Ordinal));
        }

        JsValue Includes(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.True;
            }

            var searchStr = args[0].ToObject()?.ToString() ?? "";
            var position = args.Count > 1 && args[1].TryGetDouble(out var d)? Math.Max(0, (int)d) : 0;
            if (position >= value.Length)
            {
                return new JsValue(searchStr.Length == 0);
            }

            return new JsValue(value.IndexOf(searchStr, position, StringComparison.Ordinal) >= 0);
        }

        JsValue Repeat(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || !args[0].TryGetDouble(out var d))
            {
                return new JsValue("");
            }

            var count = (int)d;
            if (count is < 0 or int.MaxValue)
            {
                return new JsValue("");
            }

            if (count == 0)
            {
                return new JsValue("");
            }

            return new JsValue(string.Concat(Enumerable.Repeat(value, count)));
        }

        JsValue PadStart(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(value);
            }

            var targetLength = args[0].TryGetDouble(out var d) ? (int)d : 0;
            if (targetLength <= value.Length)
            {
                return new JsValue(value);
            }

            var padString = args.Count > 1 ? args[1].ToObject()?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return new JsValue(value);
            }

            var padLength = targetLength - value.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return new JsValue(string.Concat(padding.AsSpan(0, padLength), value));
        }

        JsValue PadEnd(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(value);
            }

            var targetLength = args[0].TryGetDouble(out var d) ? (int)d : 0;
            if (targetLength <= value.Length)
            {
                return new JsValue(value);
            }

            var padString = args.Count > 1 ? args[1].ToObject()?.ToString() ?? " " : " ";
            if (padString.Length == 0)
            {
                return new JsValue(value);
            }

            var padLength = targetLength - value.Length;
            var padCount = (int)Math.Ceiling((double)padLength / padString.Length);
            var padding = string.Concat(Enumerable.Repeat(padString, padCount));
            return new JsValue(string.Concat(value, padding.AsSpan(0, padLength)));
        }

        JsValue ReplaceAll(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var searchValue = args.GetArgument(0);
            var replaceValue = args.GetArgument(1);

            var replaceMethod = GetMethod(searchValue, replaceKey, "@@replace");
            if (replaceMethod is not null)
            {
                return replaceMethod.Invoke([new JsValue(value), replaceValue], searchValue);
            }

            if (TryResolveRegExp(searchValue, out var regex))
            {
                if (!regex.Global)
                {
                    throw ThrowTypeError("String.prototype.replaceAll called with a non-global RegExp", realm: realm);
                }

                var replaceStr = CoerceToString(replaceValue);
                return new JsValue(Regex.Replace(value, regex.Pattern, replaceStr));
            }

            if (replaceValue.TryGetObject<IJsCallable>(out var replacer))
            {
                var searchStrFunc = CoerceToString(searchValue);
                if (searchStrFunc.Length == 0)
                {
                    var replacementValue = replacer.Invoke([new JsValue("")], new JsValue(value)).ToJsString();
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
                    var replacementValue = replacer.Invoke([new JsValue(searchStrFunc)], new JsValue(value));
                    var replacementString = replacementValue.ToJsString();
                    result.Append(replacementString);
                    currentIndex = idx + searchStrFunc.Length;
                }

                return new JsValue(result.ToString());
            }

            var searchStr = CoerceToString(searchValue);
            var replaceStrPlain = CoerceToString(replaceValue);
            return new JsValue(value.Replace(searchStr, replaceStrPlain));
        }

        JsValue At(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || !args[0].TryGetDouble(out var d))
            {
                return JsValue.Undefined;
            }

            var index = (int)d;
            if (index < 0)
            {
                index = value.Length + index;
            }

            if (index < 0 || index >= value.Length)
            {
                return JsValue.Undefined;
            }

            return new JsValue(value[index].ToString());
        }

        JsValue CodePointAt(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0 || !args[0].TryGetDouble(out var d))
            {
                return JsValue.Undefined;
            }

            var index = (int)d;
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

        JsValue LocaleCompare(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return new JsValue(0d);
            }

            var compareString = args[0].ToObject()?.ToString() ?? "";
            var result = string.Compare(value, compareString, StringComparison.CurrentCulture);
            return new JsValue((double)result);
        }

        JsValue Normalize(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var form = args.Count > 0 && !args[0].IsUndefined ? args[0].ToObject()?.ToString() : "NFC";

            try
            {
                return new JsValue(form switch
                {
                    "NFC" => value.Normalize(NormalizationForm.FormC),
                    "NFD" => value.Normalize(NormalizationForm.FormD),
                    "NFKC" => value.Normalize(NormalizationForm.FormKC),
                    "NFKD" => value.Normalize(NormalizationForm.FormKD),
                    _ => throw new Exception(
                        "RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
                });
            }
            catch
            {
                return new JsValue(value);
            }
        }

        JsValue MatchAll(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            if (args.Count == 0)
            {
                return JsValue.Null;
            }

            var matcher = args[0];
            var method = GetMethod(matcher, matchAllKey, "@@matchAll");
            if (method is not null)
            {
                return method.Invoke([new JsValue(value)], matcher);
            }

            var regex = ToRegExpValue(matcher, "g", true);
            return JsValue.FromObject(regex.MatchAll(value));
        }

        JsValue Anchor(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var name = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return new JsValue($"<a name=\"{EscapeAttr(name)}\">{value}</a>");
        }

        JsValue Link(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var url = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return new JsValue($"<a href=\"{EscapeAttr(url)}\">{value}</a>");
        }

        JsValue Bold(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<b>{value}</b>");
        }

        JsValue Italics(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<i>{value}</i>");
        }

        JsValue Fixed(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<tt>{value}</tt>");
        }

        JsValue Blink(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<blink>{value}</blink>");
        }

        JsValue Big(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<big>{value}</big>");
        }

        JsValue FontColor(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var color = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return new JsValue($"<font color=\"{EscapeAttr(color)}\">{value}</font>");
        }

        JsValue FontSize(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            var size = args.Count > 0 ? CoerceToString(args[0]) : string.Empty;
            return new JsValue($"<font size=\"{EscapeAttr(size)}\">{value}</font>");
        }

        JsValue Small(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<small>{value}</small>");
        }

        JsValue Strike(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<strike>{value}</strike>");
        }

        JsValue Sub(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<sub>{value}</sub>");
        }

        JsValue Sup(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var value = ResolveString(thisValue);
            return new JsValue($"<sup>{value}</sup>");
        }

        bool TryResolveRegExp(JsValue candidate, out JsRegExp regex)
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

        IJsCallable? GetMethod(JsValue value, string methodKey, string opName)
        {
            if (!JsOps.TryGetPropertyValue(value.ToObject(), methodKey, out var method))
            {
                return null;
            }

            if (method is null || ReferenceEquals(method, Symbol.Undefined))
            {
                return null;
            }

            if (method is not IJsCallable callable)
            {
                throw ThrowTypeError($"{opName} is not callable", realm: realm);
            }

            return callable;
        }

        JsRegExp ToRegExpValue(JsValue candidate, string defaultFlags, bool requireGlobal)
        {
            if (candidate.TryGetObject<JsRegExp>(out var direct))
            {
                if (requireGlobal && !direct.Global)
                {
                    throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
                }

                return direct;
            }

            if (candidate.TryGetObject<JsObject>(out var obj) &&
                obj.TryGetProperty("__regex__", out var regexValue) &&
                regexValue.TryGetObject<JsRegExp>(out var stored))
            {
                if (requireGlobal && !stored.Global)
                {
                    throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
                }

                return stored;
            }

            var ctx = realm?.CreateContext();
            var pattern = candidate.IsUndefined
                ? string.Empty
                : candidate.ToJsString(ctx, realm);

            var created = new JsRegExp(pattern, defaultFlags ?? string.Empty, realm);
            if (requireGlobal && !created.Global)
            {
                throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: realm);
            }

            return created;
        }

        static string EscapeAttr(string input)
        {
            return input.Replace("\"", "&quot;");
        }

        JsValue CreateIterator(JsValue thisValue, IReadOnlyList<JsValue> _)
        {
            var value = ResolveString(thisValue);
            var index = 0;
            var iterator = new JsObject();

            iterator.SetHostedProperty("next", new HostFunction(Next, realm, isConstructor: false));

            return new JsValue(iterator);

            JsValue Next(JsValue _, IReadOnlyList<JsValue> __)
            {
                var result = new JsObject();
                if (index < value.Length)
                {
                    // Per ES spec 22.1.5.2.1 %StringIteratorPrototype%.next():
                    // Strings are iterated by code point, not UTF-16 code unit.
                    // Surrogate pairs should be returned as a single string.
                    var first = value[index];
                    string currentValue;
                    if (char.IsHighSurrogate(first) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        // High surrogate followed by low surrogate - return both as single string
                        currentValue = value.Substring(index, 2);
                        index += 2;
                    }
                    else
                    {
                        // Single code unit (BMP character or unpaired surrogate)
                        currentValue = first.ToString();
                        index++;
                    }
                    result.SetProperty("value", currentValue);
                    result.SetProperty("done", false);
                }
                else
                {
                    result.SetProperty("value", JsValue.FromObject(Symbol.Undefined));
                    result.SetProperty("done", true);
                }

                return new JsValue(result);
            }
        }
    }

    private static JsArray CreateArrayFromStrings(string[] strings, RealmState? realm)
    {
        var array = new JsArray(realm);
        foreach (var s in strings)
        {
            array.Push(s);
        }

        return array;
    }

    internal static object? StringFromCodePoint(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg.ToObject());
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

    internal static object? StringFromCharCode(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var result = new StringBuilder();
        foreach (var arg in args)
        {
            var num = JsOps.ToNumber(arg.ToObject());
            if (double.IsNaN(num) || double.IsInfinity(num))
            {
                continue;
            }

            var charCode = (int)num & 0xFFFF;
            result.Append((char)charCode);
        }

        return result.ToString();
    }

    internal static object? StringRaw(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        if (!args[0].TryGetObject<IJsPropertyAccessor>(out var template))
        {
            return "";
        }

        if (!template.TryGetProperty("raw", out var rawValue) || !rawValue.TryGetObject<IJsPropertyAccessor>(out var rawAccessor))
        {
            return "";
        }

        // Get items from the raw accessor - could be JsArray or JsObject
        IReadOnlyList<JsValue>? rawItems = null;
        if (rawAccessor is JsArray rawArray)
        {
            rawItems = rawArray.Items;
        }
        else if (rawAccessor is JsObject rawObj && rawObj.TryGetProperty("length", out var lengthVal))
        {
            var length = (int)JsOps.ToNumber(lengthVal.ToObject());
            var items = new List<JsValue>(length);
            for (var i = 0; i < length; i++)
            {
                if (rawObj.TryGetProperty(i.ToString(), out var item))
                {
                    items.Add(item);
                }
                else
                {
                    items.Add(JsValue.Undefined);
                }
            }
            rawItems = items;
        }

        if (rawItems == null)
        {
            return "";
        }

        var result = new StringBuilder();
        var rawCount = rawItems.Count;

        for (var i = 0; i < rawCount; i++)
        {
            var rawPart = rawItems[i].ToObject()?.ToString() ?? "";
            result.Append(rawPart);

            if (i >= args.Count - 1)
            {
                break;
            }

            var substitution = args[i + 1].ToObject()?.ToString() ?? "";
            result.Append(substitution);
        }

        return result.ToString();
    }

    internal static object? StringEscape(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return "";
        }

        var value = args[0].ToObject()?.ToString() ?? "";
        var result = new StringBuilder();

        foreach (var ch in value)
        {
            switch (ch)
            {
                case ' ':
                    result.Append("%20");
                    break;
                case '!':
                    result.Append("%21");
                    break;
                case '"':
                    result.Append("%22");
                    break;
                case '#':
                    result.Append("%23");
                    break;
                case '$':
                    result.Append("%24");
                    break;
                case '%':
                    result.Append("%25");
                    break;
                case '&':
                    result.Append("%26");
                    break;
                case '\'':
                    result.Append("%27");
                    break;
                case '(':
                    result.Append("%28");
                    break;
                case ')':
                    result.Append("%29");
                    break;
                case '*':
                    result.Append("%2A");
                    break;
                case '+':
                    result.Append("%2B");
                    break;
                case ',':
                    result.Append("%2C");
                    break;
                case '/':
                    result.Append("%2F");
                    break;
                case ':':
                    result.Append("%3A");
                    break;
                case ';':
                    result.Append("%3B");
                    break;
                case '<':
                    result.Append("%3C");
                    break;
                case '=':
                    result.Append("%3D");
                    break;
                case '>':
                    result.Append("%3E");
                    break;
                case '?':
                    result.Append("%3F");
                    break;
                case '@':
                    result.Append("%40");
                    break;
                case '[':
                    result.Append("%5B");
                    break;
                case '\\':
                    result.Append("%5C");
                    break;
                case ']':
                    result.Append("%5D");
                    break;
                case '^':
                    result.Append("%5E");
                    break;
                case '_':
                    result.Append("%5F");
                    break;
                case '`':
                    result.Append("%60");
                    break;
                case '{':
                    result.Append("%7B");
                    break;
                case '|':
                    result.Append("%7C");
                    break;
                case '}':
                    result.Append("%7D");
                    break;
                case '~':
                    result.Append("%7E");
                    break;
                default:
                    if (ch <= 0x7F)
                    {
                        result.Append(ch);
                    }
                    else
                    {
                        result.Append("%u");
                        result.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    ///     Creates the String constructor with static methods.
    /// </summary>
    public static HostFunction CreateStringConstructor(RealmState realm)
    {
        return StringConstructor.CreateConstructor(realm);
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
}
