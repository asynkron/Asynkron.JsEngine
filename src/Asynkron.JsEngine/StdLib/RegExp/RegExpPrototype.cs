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
    public object? Test(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return false;
        }

        if (args.Count == 0)
        {
            return false;
        }

        var input = args[0]?.ToString() ?? string.Empty;
        return resolved.Test(input);
    }

    [JsHostMethod("exec", Length = 1d)]
    public object? Exec(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return null;
        }

        if (args.Count == 0)
        {
            return null;
        }

        var input = args[0]?.ToString() ?? string.Empty;
        return resolved.Exec(input);
    }

    [JsHostMethod("toString", Length = 0d)]
    public object ToString(object? thisValue, IReadOnlyList<object?> _)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        return resolved is null ? "/undefined/" : $"/{resolved.Pattern}/{resolved.Flags}";
    }

    [JsHostMethod("compile", Length = 2d)]
    public object? Compile(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsObject target ||
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
        if (patternArg is TypedAstSymbol ||
            (!ReferenceEquals(flagsArg, Symbol.Undefined) && flagsArg is TypedAstSymbol))
        {
            throw ThrowTypeError("Cannot convert a Symbol value to a string", realm: Realm);
        }

        JsRegExp? providedRegExp = null;
        if (patternArg is JsObject patternObj &&
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
            if (!ReferenceEquals(flagsArg, Symbol.Undefined))
            {
                throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: Realm);
            }

            pattern = otherRegExp.Pattern;
            flags = otherRegExp.Flags;
        }
        else
        {
            pattern = ReferenceEquals(patternArg, Symbol.Undefined)
                ? string.Empty
                : JsOps.ToJsString(patternArg);
            flags = ReferenceEquals(flagsArg, Symbol.Undefined) ? string.Empty : JsOps.ToJsString(flagsArg);
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

        return target;
    }

    [JsHostGetter("flags")]
    public object Flags(object? thisValue)
    {
        return GetSortedFlags(RequireRegExp(thisValue));
    }

    [JsHostGetter("source")]
    public object Source(object? thisValue)
    {
        var resolved = RequireRegExp(thisValue);
        return string.IsNullOrEmpty(resolved.Pattern) ? "(?:)" : resolved.Pattern;
    }

    [JsHostGetter("global")]
    public object Global(object? thisValue)
    {
        return RequireRegExp(thisValue).Global;
    }

    [JsHostGetter("ignoreCase")]
    public object IgnoreCase(object? thisValue)
    {
        return RequireRegExp(thisValue).IgnoreCase;
    }

    [JsHostGetter("multiline")]
    public object Multiline(object? thisValue)
    {
        return RequireRegExp(thisValue).Multiline;
    }

    [JsHostGetter("dotAll")]
    public object DotAll(object? thisValue)
    {
        return RequireRegExp(thisValue).DotAll;
    }

    [JsHostGetter("unicode")]
    public object Unicode(object? thisValue)
    {
        return RequireRegExp(thisValue).Unicode;
    }

    [JsHostGetter("sticky")]
    public object Sticky(object? thisValue)
    {
        return RequireRegExp(thisValue).Sticky;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.RegExpPrototype ??= Prototype as JsObject;

        var splitKey = TypedAstSymbol.PropertyKey("Symbol.split");
        if (Prototype is JsObject obj)
        {
            obj.SetHostedProperty(splitKey, Split);
        }
    }

    private JsRegExp RequireRegExp(object? receiver)
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

    private object? Split(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var matchKey = TypedAstSymbol.PropertyKey("Symbol.match");
        var receiver = thisValue ?? resolved.JsObject;
        JsOps.TryGetPropertyValue(receiver, matchKey, out _);

        resolved = ResolveRegExpInstance(receiver) ?? resolved;

        var input = JsOps.ToJsString(args.Count > 0 ? args[0] : string.Empty);
        var limitValue = args.GetArgument(1);
        var forcedFlags = resolved.Flags.Contains('g') ? resolved.Flags : resolved.Flags + "g";
        var splitter = new JsRegExp(resolved.Pattern, forcedFlags, Realm);
        splitter.SetProperty("lastIndex", 0d);

        var limit = ReferenceEquals(limitValue, Symbol.Undefined)
            ? uint.MaxValue
            : ToUint32(limitValue);

        var resultArray = new JsArray(Realm);
        if (limit == 0)
        {
            return resultArray;
        }

        var position = 0;
        while (resultArray.Length < limit)
        {
            var matchObj = splitter.Exec(input) as JsArray;
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

        return resultArray;
    }
}
