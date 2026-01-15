#region

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.RegExpHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("RegExp", ToStringTag = "RegExp")]
public sealed partial class RegExpPrototype
{
    [JsHostMethod("test", Length = 1d)]
    public JsValue Test(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return new JsValue(false);
        }

        if (args.Count == 0)
        {
            return new JsValue(false);
        }

        var input = JsOps.ToJsString(args[0]) ?? string.Empty;
        return new JsValue(resolved.Test(input));
    }

    [JsHostMethod("exec", Length = 1d)]
    public JsValue Exec(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return JsValue.Null;
        }

        if (args.Count == 0)
        {
            return JsValue.Null;
        }

        var input = JsOps.ToJsString(args[0]) ?? string.Empty;
        var result = resolved.Exec(input);
        return result is null ? JsValue.Null : JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        var flagsValue = Flags(thisValue);
        var flagsText = JsOps.ToJsString(flagsValue);
        var result = resolved is null ? "/undefined/" : $"/{resolved.Pattern}/{flagsText}";
        return new JsValue(result);
    }

    [JsHostMethod("compile", Length = 2d)]
    public JsValue Compile(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<JsObject>(out var target) ||
            !ReferenceEquals(target.Prototype, Realm.RegExpPrototype) ||
            !target.TryGetValue("__regex__", out var existingInner) ||
            existingInner is not JsRegExp existingRegExp ||
            !ReferenceEquals(existingRegExp.RealmState, Realm) ||
            !ReferenceEquals(existingRegExp.JsObject, target))
        {
            throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: Realm);
        }

        var patternArg = args.GetArgument(0);
        var flagsArg = args.GetArgument(1);
        if (patternArg.TryUnwrap<JsSymbol>(out _) ||
            (flagsArg != JsValue.Undefined && flagsArg.TryUnwrap<JsSymbol>(out _)))
        {
            throw ThrowTypeError("Cannot convert a Symbol value to a string", realm: Realm);
        }

        JsRegExp? providedRegExp;
        if (patternArg.TryGetObject<JsObject>(out var patternObj) &&
            patternObj.TryGetValue("__regex__", out var innerVal) &&
            innerVal is JsRegExp regExpFromSlot &&
            ReferenceEquals(regExpFromSlot.JsObject, patternObj))
        {
            providedRegExp = regExpFromSlot;
        }
        else
        {
            providedRegExp = ResolveRegExpInstance(patternArg);
        }

        string pattern;
        string flags;

        if (providedRegExp is { } otherRegExp)
        {
            if (flagsArg != JsValue.Undefined)
            {
                throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: Realm);
            }

            pattern = otherRegExp.Pattern;
            flags = otherRegExp.Flags;
        }
        else
        {
            pattern = patternArg == JsValue.Undefined
                ? string.Empty
                : JsOps.ToJsString(patternArg);
            flags = flagsArg == JsValue.Undefined ? string.Empty : JsOps.ToJsString(flagsArg);
        }

        try
        {
            ValidateGroupNames(pattern);

            if (!target.TryGetProperty("constructor", out var ctor) ||
                !ReferenceEquals(ctor, Realm.RegExpConstructor))
            {
                throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: Realm);
            }

            var reinitialized = new JsRegExp(pattern, flags, Realm, target);
            target.SetProperty("__regex__", reinitialized);

            ResetLastIndex(Realm, target);
        }
        catch (ThrowSignal)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ThrowSignal(CreateSyntaxError(ex.Message, realm: Realm));
        }

        return new JsValue(target);
    }

    [JsHostGetter("flags")]
    public JsValue Flags(JsValue thisValue)
    {
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var context = Realm?.CreateContext();
        var builder = new StringBuilder();

        AppendFlag(builder, thisValue, "hasIndices", 'd', context);
        AppendFlag(builder, thisValue, "global", 'g', context);
        AppendFlag(builder, thisValue, "ignoreCase", 'i', context);
        AppendFlag(builder, thisValue, "multiline", 'm', context);
        AppendFlag(builder, thisValue, "dotAll", 's', context);
        AppendFlag(builder, thisValue, "unicode", 'u', context);
        AppendFlag(builder, thisValue, "unicodeSets", 'v', context);
        AppendFlag(builder, thisValue, "sticky", 'y', context);

        return new JsValue(builder.ToString());
    }

    [JsHostGetter("source")]
    public JsValue Source(JsValue thisValue)
    {
        var resolved = RequireRegExp(thisValue);
        var result = string.IsNullOrEmpty(resolved.Pattern) ? "(?:)" : resolved.Pattern;
        return new JsValue(result);
    }

    [JsHostGetter("global")]
    public JsValue Global(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Global);
    }

    [JsHostGetter("ignoreCase")]
    public JsValue IgnoreCase(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).IgnoreCase);
    }

    [JsHostGetter("multiline")]
    public JsValue Multiline(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Multiline);
    }

    [JsHostGetter("dotAll")]
    public JsValue DotAll(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).DotAll);
    }

    [JsHostGetter("hasIndices")]
    public JsValue HasIndices(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).HasIndices);
    }

    [JsHostGetter("unicode")]
    public JsValue Unicode(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Unicode);
    }

    [JsHostGetter("unicodeSets")]
    public JsValue UnicodeSets(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).UnicodeSets);
    }

    [JsHostGetter("sticky")]
    public JsValue Sticky(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Sticky);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.RegExpPrototype ??= Prototype as JsObject;

        // [Symbol.split] is registered via code generation from [JsSymbolMethod] attribute
    }

    private JsRegExp RequireRegExp(JsValue receiver)
    {
        var resolved = ResolveRegExpInstance(receiver);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        return resolved;
    }

    [JsSymbolMethod("match", Length = 1d)]
    private JsValue MatchSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;

        if (!resolved.Global)
        {
            var single = resolved.Exec(input);
            return single is null ? JsValue.Null : JsValue.FromObjectUnsafe(single);
        }

        resolved.SetLastIndex(0);
        var matches = new JsArray(Realm);

        while (true)
        {
            var match = resolved.Exec(input);
            if (match is null)
            {
                break;
            }

            var matchText = match.Items.Count > 0 ? match.Items[0].ToJsString() : string.Empty;
            matches.Push(matchText);

            if (matchText.Length != 0)
            {
                continue;
            }

            // Avoid infinite loops on zero-length matches.
            var nextIndex = AdvanceStringIndex(input, resolved.GetLastIndex(), resolved.Unicode);
            resolved.SetLastIndex(nextIndex);
        }

        return matches.Length == 0 ? JsValue.Null : JsValue.FromJsArray(matches);
    }

    [JsSymbolMethod("matchAll", Length = 1d)]
    private JsValue MatchAllSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);
        if (!resolved.Global)
        {
            throw ThrowTypeError("RegExp.prototype.matchAll requires a global RegExp", realm: Realm);
        }

        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;
        return JsValue.FromJsArray(resolved.MatchAll(input));
    }

    [JsSymbolMethod("replace", Length = 2d)]
    private JsValue ReplaceSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;
        var replacement = args.GetArgument(1);
        var regex = resolved.GetRegex();

        if (replacement.TryGetObject<IJsCallable>(out var replacer))
        {
            var builder = new StringBuilder();
            var lastIndex = 0;

            foreach (Match match in regex.Matches(input))
            {
                if (!match.Success)
                {
                    continue;
                }

                if (!resolved.Global && lastIndex > 0)
                {
                    break;
                }

                if (match.Index > lastIndex)
                {
                    builder.Append(input.AsSpan(lastIndex, match.Index - lastIndex));
                }

                var replaceArgs = BuildReplaceArguments(match, regex, input);
                var replacementValue = replacer.Invoke(replaceArgs, JsValue.Undefined);
                builder.Append(replacementValue.ToJsString());

                lastIndex = match.Index + match.Length;
                if (!resolved.Global)
                {
                    break;
                }
            }

            if (lastIndex < input.Length)
            {
                builder.Append(input.AsSpan(lastIndex));
            }

            return new JsValue(builder.ToString());
        }

        var replaceText = JsOps.ToJsString(replacement);
        if (resolved.Global)
        {
            return new JsValue(regex.Replace(input, replaceText));
        }

        var singleMatch = regex.Match(input);
        if (!singleMatch.Success)
        {
            return new JsValue(input);
        }

        return new JsValue(string.Concat(input.AsSpan(0, singleMatch.Index), replaceText,
            input.AsSpan(singleMatch.Index + singleMatch.Length)));
    }

    [JsSymbolMethod("search", Length = 1d)]
    private JsValue SearchSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;

        resolved.SetLastIndex(0);
        var result = resolved.Exec(input);
        if (result is not null && result.TryGetProperty("index", out var indexValue) &&
            indexValue.TryGetDouble(out var indexNumber))
        {
            return new JsValue(indexNumber);
        }

        return new JsValue(-1d);
    }

    private static int AdvanceStringIndex(string input, int index, bool unicode)
    {
        if (!unicode || index + 1 >= input.Length)
        {
            return Math.Min(index + 1, input.Length);
        }

        var first = input[index];
        if (char.IsHighSurrogate(first) && index + 1 < input.Length && char.IsLowSurrogate(input[index + 1]))
        {
            return Math.Min(index + 2, input.Length);
        }

        return Math.Min(index + 1, input.Length);
    }

    private static IReadOnlyList<JsValue> BuildReplaceArguments(Match match, Regex regex, string input)
    {
        var args = new List<JsValue>(match.Groups.Count + 3)
        {
            new(match.Value)
        };

        for (var i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            args.Add(group.Success ? new JsValue(group.Value) : JsValue.Undefined);
        }

        args.Add(new JsValue((double)match.Index));
        args.Add(new JsValue(input));

        var groups = BuildGroupsObject(match, regex);
        if (groups is not null)
        {
            args.Add(JsValue.FromJsObject(groups));
        }

        return args;
    }

    private static JsObject? BuildGroupsObject(Match match, Regex regex)
    {
        JsObject? groups = null;

        foreach (var name in match.Groups.Keys)
        {
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var groupNumber = regex.GroupNumberFromName(name);
            var group = match.Groups[groupNumber];
            groups ??= new JsObject();
            groups.SetProperty(name, group.Success ? new JsValue(group.Value) : JsValue.Undefined);
        }

        return groups;
    }

    private static void AppendFlag(StringBuilder builder, JsValue receiver, string propertyName, char flag,
        EvaluationContext? context)
    {
        if (TryGetFlag(receiver, propertyName, context))
        {
            builder.Append(flag);
        }
    }

    private static bool TryGetFlag(JsValue receiver, string propertyName, EvaluationContext? context)
    {
        if (!JsOps.TryGetPropertyValue(receiver, propertyName, out var value, context))
        {
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return false;
        }

        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return value.IsTruthy;
    }

    [JsSymbolMethod("split", Length = 2d)]
    private JsValue Split(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var matchKey = SymbolKeys.Match;
        var receiver = thisValue != JsValue.Null ? thisValue : new JsValue(resolved.JsObject);
        JsOps.TryGetPropertyValue(receiver, matchKey, out _);

        resolved = ResolveRegExpInstance(receiver) ?? resolved;

        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) : string.Empty;
        var limitValue = args.GetArgument(1);
        var forcedFlags = resolved.Flags.Contains('g', StringComparison.Ordinal)
            ? resolved.Flags
            : resolved.Flags + "g";
        var splitter = new JsRegExp(resolved.Pattern, forcedFlags, Realm);

        var limit = limitValue == JsValue.Undefined
            ? uint.MaxValue
            : ToUint32(limitValue);

        var resultArray = new JsArray(Realm);
        if (limit == 0)
        {
            return JsValue.FromJsArray(resultArray);
        }

        var size = input.Length;
        if (size == 0)
        {
            // Empty string: if regex matches empty string, return empty array; otherwise return [input]
            splitter.SetProperty("lastIndex", 0d);
            var matchObj = splitter.Exec(input);
            if (matchObj is null)
            {
                resultArray.Push(input);
            }

            return JsValue.FromJsArray(resultArray);
        }

        // ES spec 21.2.5.11 steps 11-24
        // p = end of last match, q = current search position
        var p = 0; // End position of last match
        var q = 0; // Current search position

        while (q < size)
        {
            // Step 24.a-b: Set lastIndex to q
            splitter.SetProperty("lastIndex", (double)q);

            // Step 24.c-d: Execute regex
            var matchObj = splitter.Exec(input);

            // Step 24.e: If no match, advance q and continue
            if (matchObj is null)
            {
                q = AdvanceStringIndex(input, q);
                continue;
            }

            // Step 24.f: Match found
            // Get e = end position of match (lastIndex after exec)
            var e = splitter.GetLastIndex();
            e = Math.Min(e, size);

            // Step 24.f.iii: If e = p (empty match at same position), advance q and continue
            if (e == p)
            {
                q = AdvanceStringIndex(input, q);
                continue;
            }

            // Step 24.f.iv-ix: Add substring and capture groups to result
            if (!matchObj.TryGetProperty("index", out var idxVal))
            {
                break;
            }

            var matchIndex = (int)JsOps.ToNumber(idxVal);

            // Add substring from p to matchIndex
            resultArray.Push(input.Substring(p, matchIndex - p));
            if (resultArray.Length >= limit)
            {
                return JsValue.FromJsArray(resultArray);
            }

            // Add capture groups (indices 1 to numberOfCaptures)
            for (var i = 1; i < matchObj.Items.Count; i++)
            {
                resultArray.Push(matchObj.Items[i]);
                if (resultArray.Length >= limit)
                {
                    return JsValue.FromJsArray(resultArray);
                }
            }

            // Update p and q to e
            p = e;
            q = e;
        }

        // Step 25: Add the tail of the string
        resultArray.Push(input[p..]);

        return JsValue.FromJsArray(resultArray);
    }

    /// <summary>
    /// Advances string index by one code point (handling surrogate pairs for Unicode).
    /// </summary>
    private static int AdvanceStringIndex(string s, int index)
    {
        // For simplicity, advance by 1. A full implementation would handle Unicode surrogate pairs.
        return index + 1;
    }
}
