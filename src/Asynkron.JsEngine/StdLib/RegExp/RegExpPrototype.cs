using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("RegExp", ToStringTag = "RegExp")]
public sealed partial class RegExpPrototype : JsPrototype
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

        var input = JsOps.ToJsString(args[0].ToObject()) ?? string.Empty;
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

        var input = JsOps.ToJsString(args[0].ToObject()) ?? string.Empty;
        var result = resolved.Exec(input);
        return result is null ? JsValue.Null : new JsValue(result);
    }

    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        var result = resolved is null ? "/undefined/" : $"/{resolved.Pattern}/{resolved.Flags}";
        return new JsValue(result);
    }

    [JsHostMethod("compile", Length = 2d)]
    public JsValue Compile(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (!thisValue.TryGetObject<JsObject>(out var target) ||
            target is null ||
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
        if (patternArg.TryUnwrap<TypedAstSymbol>(out _) ||
            (flagsArg != JsValue.Undefined && flagsArg.TryUnwrap<TypedAstSymbol>(out _)))
        {
            throw ThrowTypeError("Cannot convert a Symbol value to a string", realm: Realm);
        }

        JsRegExp? providedRegExp = null;
        if (patternArg.TryGetObject<JsObject>(out var patternObj) &&
            patternObj is not null &&
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
                : JsOps.ToJsString(patternArg.ToObject());
            flags = flagsArg == JsValue.Undefined ? string.Empty : JsOps.ToJsString(flagsArg.ToObject());
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
        return new JsValue(GetSortedFlags(RequireRegExp(thisValue)));
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

    [JsHostGetter("unicode")]
    public JsValue Unicode(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Unicode);
    }

    [JsHostGetter("sticky")]
    public JsValue Sticky(JsValue thisValue)
    {
        return new JsValue(RequireRegExp(thisValue).Sticky);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.RegExpPrototype ??= Prototype as JsObject;

        var splitKey = SymbolKeys.GetSplit(Realm);
        if (Prototype is JsObject obj)
        {
            obj.SetHostedProperty(splitKey, Split);
        }
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

    private static string GetSortedFlags(JsRegExp regex)
    {
        Span<char> buffer = stackalloc char[6];
        var length = 0;
        if (regex.Global)
        {
            buffer[length++] = 'g';
        }

        if (regex.IgnoreCase)
        {
            buffer[length++] = 'i';
        }

        if (regex.Multiline)
        {
            buffer[length++] = 'm';
        }

        if (regex.DotAll)
        {
            buffer[length++] = 's';
        }

        if (regex.Unicode)
        {
            buffer[length++] = 'u';
        }

        if (regex.Sticky)
        {
            buffer[length++] = 'y';
        }

        return new string(buffer[..length]);
    }

    private JsValue Split(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var matchKey = SymbolKeys.GetMatch(Realm);
        var receiver = thisValue != JsValue.Null ? thisValue : new JsValue(resolved.JsObject);
        JsOps.TryGetPropertyValue(receiver.ToObject(), matchKey, out _);

        resolved = ResolveRegExpInstance(receiver) ?? resolved;

        var input = JsOps.ToJsString(args.Count > 0 ? args[0].ToObject() : string.Empty);
        var limitValue = args.GetArgument(1);
        var forcedFlags = resolved.Flags.Contains('g') ? resolved.Flags : resolved.Flags + "g";
        var splitter = new JsRegExp(resolved.Pattern, forcedFlags, Realm);
        splitter.SetProperty("lastIndex", 0d);

        var limit = limitValue == JsValue.Undefined
            ? uint.MaxValue
            : ToUint32(limitValue.ToObject());

        var resultArray = new JsArray(Realm);
        if (limit == 0)
        {
            return new JsValue(resultArray);
        }

        var position = 0;
        while (resultArray.Length < limit)
        {
            var execResult = splitter.Exec(input);
            var matchObj = execResult as JsArray;
            if (matchObj is null)
            {
                break;
            }

            if (!matchObj.TryGetProperty("index", out var idxVal))
            {
                break;
            }

            var matchIndex = (int)JsOps.ToNumber(idxVal);
            var matchText = matchObj.Items.Count > 0
                ? Convert.ToString(matchObj.Items[0], CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
            var matchLength = matchText.Length;

            resultArray.Push(input.Substring(position, Math.Max(0, matchIndex - position)));

            for (var i = 1; i < matchObj.Items.Count && resultArray.Length < limit; i++)
            {
                resultArray.Push(matchObj.Items[i]);
            }

            position = matchIndex + matchLength;
            if (matchLength == 0)
            {
                position++;
                splitter.SetProperty("lastIndex", (double)position);
            }

            if (position <= input.Length)
            {
                continue;
            }

            position = input.Length;
            break;
        }

        if (resultArray.Length < limit)
        {
            resultArray.Push(input[position..]);
        }

        return new JsValue(resultArray);
    }
}
