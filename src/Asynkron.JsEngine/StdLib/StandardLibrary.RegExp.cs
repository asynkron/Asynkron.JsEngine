using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateRegExpConstructor(RealmState realm)
    {
        var prototype = new JsObject();
        realm.RegExpPrototype = prototype;

        var constructor = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return CreateRegExpLiteral("(?:)", "", realm);
            }

            if (args is [JsObject { } existingObj] &&
                existingObj.TryGetProperty("__regex__", out var internalRegex) &&
                internalRegex is JsRegExp existing)
            {
                return CreateRegExpLiteral(existing.Pattern, existing.Flags, realm);
            }

            var pattern = args[0]?.ToString() ?? "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return CreateRegExpLiteral(pattern, flags, realm);
        })
        {
            IsConstructor = true,
            RealmState = realm
        };

        constructor.SetProperty("prototype", prototype);
        prototype.SetProperty("constructor", constructor);
        realm.RegExpConstructor = constructor;
        DefineLegacyRegExpAccessors(constructor, prototype, realm);
        DefineRegExpAccessors(prototype, realm);
        return constructor;
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags, RealmState? realm = null)
    {
        try
        {
            ValidateGroupNames(pattern);
            var regex = new JsRegExp(pattern, flags, realm);
            regex.JsObject["__regex__"] = regex;
            if (realm?.RegExpPrototype is not null)
            {
                regex.JsObject.SetPrototype(realm.RegExpPrototype);
            }
            AddRegExpMethods(regex, realm);
            return regex.JsObject;
        }
        catch (ParseException ex)
        {
            var error = CreateSyntaxError(ex.Message, realm);
            throw new ThrowSignal(error);
        }
        catch (ArgumentException ex)
        {
            var error = CreateSyntaxError(ex.Message, realm);
            throw new ThrowSignal(error);
        }
    }

    private static void ValidateGroupNames(string pattern)
    {
        var index = 0;
        while (true)
        {
            var start = pattern.IndexOf("(?<", index, StringComparison.Ordinal);
            if (start == -1)
            {
                break;
            }

            var end = pattern.IndexOf('>', start + 3);
            if (end == -1)
            {
                break;
            }

            var name = pattern.Substring(start + 3, end - (start + 3));
            JsRegExp.NormalizeGroupNameToken(name);
            index = end + 1;
        }
    }

    /// <summary>
    ///     Adds RegExp instance methods to a JsRegExp object.
    /// </summary>
    private static void AddRegExpMethods(JsRegExp regex, RealmState? realm)
    {
        // test(string) - returns boolean
        regex.JsObject.SetHostedProperty("test", RegExpTest);

        // exec(string) - returns array with match details or null
        regex.JsObject.SetHostedProperty("exec", RegExpExec);

        // toString() - returns `/pattern/flags`
        regex.JsObject.SetHostedProperty("toString", RegExpToString);

        // RegExp.prototype.compile (Annex B)
        var compileFn = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsObject target)
            {
                throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver");
            }

            string pattern;
            string flags;

            if (args.Count > 0 &&
                args[0] is JsObject { } other &&
                other.TryGetProperty("__regex__", out var inner) &&
                inner is JsRegExp otherRegExp &&
                (args.Count < 2 || ReferenceEquals(args[1], Symbols.Undefined)))
            {
                pattern = otherRegExp.Pattern;
                flags = otherRegExp.Flags;
            }
            else
            {
                pattern = args.Count > 0 ? JsOps.ToJsString(args[0]) : regex.Pattern;
                flags = args.Count > 1 ? JsOps.ToJsString(args[1]) : regex.Flags;
            }

            var newInstance = CreateRegExpLiteral(pattern, flags, realm);
            if (!newInstance.TryGetProperty("__regex__", out var compiledObj) || compiledObj is not JsRegExp compiled)
            {
                return target;
            }

            target.SetProperty("__regex__", compiled);
            target.SetProperty("source", compiled.Pattern);
            target.SetProperty("flags", compiled.Flags);
            target.SetProperty("global", compiled.Global);
            target.SetProperty("ignoreCase", compiled.IgnoreCase);
            target.SetProperty("multiline", compiled.Multiline);
            target.SetProperty("lastIndex", 0d);
            return target;
        });
        DefineBuiltinFunction(regex.JsObject, "compile", compileFn, 1, isConstructor: false);
    }

    private static object CreateSyntaxError(string message, RealmState? realm)
    {
        if (realm?.SyntaxErrorConstructor is { } ctor)
        {
            try
            {
                return ctor.Invoke([message], Symbols.Undefined);
            }
            catch (ThrowSignal signal)
            {
                return signal.ThrownValue ?? message;
            }
        }

        return message;
    }

    private static object? RegExpTest(object? thisValue, IReadOnlyList<object?> args)
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

    private static object? RegExpExec(object? thisValue, IReadOnlyList<object?> args)
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

    private static JsRegExp? ResolveRegExpInstance(object? thisValue)
    {
        if (thisValue is JsRegExp direct)
        {
            return direct;
        }

        if (thisValue is JsObject obj &&
            obj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex is JsRegExp stored)
        {
            return stored;
        }

        return null;
    }

    private static object RegExpToString(object? thisValue, IReadOnlyList<object?> args)
    {
        var resolved = ResolveRegExpInstance(thisValue);
        if (resolved is null)
        {
            return "/undefined/";
        }

        return $"/{resolved.Pattern}/{resolved.Flags}";
    }

    internal static void UpdateRegExpStatics(RealmState? realm, string input, System.Text.RegularExpressions.Match match)
    {
        if (realm is null)
        {
            return;
        }

        var statics = realm.RegExpStatics;
        statics.Input = input ?? string.Empty;
        statics.LastMatch = match.Value;
        statics.LeftContext = input[..match.Index];
        statics.RightContext = input[(match.Index + match.Length)..];

        statics.LastParen = string.Empty;
        Array.Clear(statics.Captures, 0, statics.Captures.Length);

        for (var i = 1; i < match.Groups.Count && i <= 9; i++)
        {
            var group = match.Groups[i];
            var value = group.Success ? group.Value : string.Empty;
            statics.Captures[i - 1] = value;
            if (group.Success && group.Index + group.Length == match.Index + match.Length)
            {
                statics.LastParen = value;
            }
        }
    }

    private static void DefineLegacyRegExpAccessors(HostFunction constructor, JsObject prototype, RealmState realm)
    {
        static PropertyDescriptor MakeAccessor(Func<RegExpStatics, object?> getter,
            Action<RegExpStatics, object?> setter, RealmState realm)
        {
            return new PropertyDescriptor
            {
                Get = new HostFunction((_, _) => getter(realm.RegExpStatics)) { IsConstructor = false },
                Set = new HostFunction((_, args) =>
                {
                    var value = args.Count > 0 ? args[0] : Symbols.Undefined;
                    setter(realm.RegExpStatics, value);
                    return null;
                })
                { IsConstructor = false },
                Enumerable = false,
                Configurable = true
            };
        }

        object? GetCapture(RegExpStatics s, int index) => index < s.Captures.Length ? s.Captures[index] : string.Empty;
        void SetInput(RegExpStatics s, object? v) => s.Input = v?.ToString() ?? string.Empty;

        var inputDescriptor = MakeAccessor(s => s.Input, SetInput, realm);
        var lastMatchDescriptor = MakeAccessor(s => s.LastMatch, (_, __) => { }, realm);
        var lastParenDescriptor = MakeAccessor(s => s.LastParen, (_, __) => { }, realm);
        var leftDescriptor = MakeAccessor(s => s.LeftContext, (_, __) => { }, realm);
        var rightDescriptor = MakeAccessor(s => s.RightContext, (_, __) => { }, realm);

        constructor.DefineProperty("input", inputDescriptor);
        constructor.DefineProperty("$_", inputDescriptor);
        constructor.DefineProperty("lastMatch", lastMatchDescriptor);
        constructor.DefineProperty("$&", lastMatchDescriptor);
        constructor.DefineProperty("lastParen", lastParenDescriptor);
        constructor.DefineProperty("$+", lastParenDescriptor);
        constructor.DefineProperty("leftContext", leftDescriptor);
        constructor.DefineProperty("$`", leftDescriptor);
        constructor.DefineProperty("rightContext", rightDescriptor);
        constructor.DefineProperty("$'", rightDescriptor);

        for (var i = 1; i <= 9; i++)
        {
            var idx = i;
            var captureDescriptor = MakeAccessor(s => GetCapture(s, idx - 1), (_, __) => { }, realm);
            constructor.DefineProperty($"${idx}", captureDescriptor);
        }

        // Mirror accessors on the prototype as well.
        prototype.DefineProperty("input", inputDescriptor);
        prototype.DefineProperty("$_", inputDescriptor);
        prototype.DefineProperty("lastMatch", lastMatchDescriptor);
        prototype.DefineProperty("$&", lastMatchDescriptor);
        prototype.DefineProperty("lastParen", lastParenDescriptor);
        prototype.DefineProperty("$+", lastParenDescriptor);
        prototype.DefineProperty("leftContext", leftDescriptor);
        prototype.DefineProperty("$`", leftDescriptor);
        prototype.DefineProperty("rightContext", rightDescriptor);
        prototype.DefineProperty("$'", rightDescriptor);
        for (var i = 1; i <= 9; i++)
        {
            var captureDescriptor = MakeAccessor(s => GetCapture(s, i - 1), (_, __) => { }, realm);
            prototype.DefineProperty($"${i}", captureDescriptor);
        }

        // RegExp.multiline legacy accessor aliases RegExp.prototype.flags? Treat as global statics flag.
        var multilineDescriptor = MakeAccessor(
            s => false, // legacy flag not tracked; return false
            (_, __) => { },
            realm);
        constructor.DefineProperty("multiline", multilineDescriptor);
        prototype.DefineProperty("multiline", multilineDescriptor);
    }

    private static void DefineRegExpAccessors(JsObject prototype, RealmState realm)
    {
        PropertyDescriptor MakeGetter(Func<JsRegExp, object?> getter)
        {
            return new PropertyDescriptor
            {
                Get = new HostFunction((thisValue, _) =>
                {
                    var resolved = ResolveRegExpInstance(thisValue);
                    if (resolved is null)
                    {
                        throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm);
                    }

                    return getter(resolved);
                })
                { IsConstructor = false },
                Enumerable = false,
                Configurable = true
            };
        }

        prototype.DefineProperty("flags", MakeGetter(r => r.Flags));
        prototype.DefineProperty("source", MakeGetter(r => string.IsNullOrEmpty(r.Pattern) ? "(?:)" : r.Pattern));
        prototype.DefineProperty("global", MakeGetter(r => r.Global));
        prototype.DefineProperty("ignoreCase", MakeGetter(r => r.IgnoreCase));
        prototype.DefineProperty("multiline", MakeGetter(r => r.Multiline));
    }
}
