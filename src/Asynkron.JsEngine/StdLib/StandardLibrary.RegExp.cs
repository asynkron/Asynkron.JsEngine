using System.Collections.Generic;
using System.Globalization;
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
        AddRegExpPrototypeMethods(prototype, realm);
        var splitKey = $"@@symbol:{TypedAstSymbol.For("Symbol.split").GetHashCode()}";
        prototype.SetHostedProperty(splitKey, (thisValue, args) =>
        {
            var resolved = ResolveRegExpInstance(thisValue);
            if (resolved is null)
            {
                throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm);
            }

            // Trigger IsRegExp style side-effects (e.g., Symbol.match getter) before cloning flags/pattern.
            var matchKey = $"@@symbol:{TypedAstSymbol.For("Symbol.match").GetHashCode()}";
            JsOps.TryGetPropertyValue(thisValue ?? resolved.JsObject, matchKey, out _, null);

            var input = JsOps.ToJsString(args.Count > 0 ? args[0] : string.Empty);
            var limitValue = args.Count > 1 ? args[1] : Symbols.Undefined;
            var limit = ReferenceEquals(limitValue, Symbols.Undefined)
                ? uint.MaxValue
                : ToUint32(limitValue);

            // The limit coercion may have side-effects; refresh the resolved regex afterwards.
            resolved = ResolveRegExpInstance(thisValue) ?? resolved;
            var forcedFlags = resolved.Flags.Contains('g') ? resolved.Flags : resolved.Flags + "g";
            var splitter = new JsRegExp(resolved.Pattern, forcedFlags, realm);
            splitter.SetProperty("lastIndex", 0d);

            var resultArray = new JsArray(realm);
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

                if (position > input.Length)
                {
                    position = input.Length;
                    break;
                }
            }

            if (resultArray.Length < limit)
            {
                resultArray.Push(input[position..]);
            }

            return resultArray;
        });
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
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
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
            var normalized = JsRegExp.NormalizeGroupNameToken(name);
            if (!seenNames.Add(normalized))
            {
                throw new ParseException("Invalid regular expression: duplicate group name.");
            }
            index = end + 1;
        }
    }

    /// <summary>
    ///     Adds RegExp prototype methods.
    /// </summary>
    private static void AddRegExpPrototypeMethods(JsObject prototype, RealmState realm)
    {
        // test(string) - returns boolean
        prototype.SetHostedProperty("test", RegExpTest);

        // exec(string) - returns array with match details or null
        prototype.SetHostedProperty("exec", RegExpExec);

        // toString() - returns `/pattern/flags`
        prototype.SetHostedProperty("toString", RegExpToString);

        // RegExp.prototype.compile (Annex B)
        var compileFn = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsObject target ||
                !ReferenceEquals(target.Prototype, realm.RegExpPrototype) ||
                target.TryGetProperty("__regex__", out var existingInner) != true ||
                existingInner is not JsRegExp existingRegExp ||
                !ReferenceEquals(existingRegExp.RealmState, realm) ||
                !ReferenceEquals(existingRegExp.JsObject, target))
            {
                throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: realm);
            }

            var patternArg = args.Count > 0 ? args[0] : Symbols.Undefined;
            var flagsArg = args.Count > 1 ? args[1] : Symbols.Undefined;
            string pattern;
            string flags;

            if (patternArg is TypedAstSymbol || (!ReferenceEquals(flagsArg, Symbols.Undefined) && flagsArg is TypedAstSymbol))
            {
                throw ThrowTypeError("Cannot convert a Symbol value to a string", realm: realm);
            }

            if (patternArg is JsObject { } other &&
                other.TryGetProperty("__regex__", out var inner) &&
                inner is JsRegExp otherRegExp)
            {
                if (!ReferenceEquals(flagsArg, Symbols.Undefined))
                {
                    throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: realm);
                }

                pattern = otherRegExp.Pattern;
                flags = otherRegExp.Flags;
            }
            else
            {
                pattern = ReferenceEquals(patternArg, Symbols.Undefined) ? string.Empty : JsOps.ToJsString(patternArg);
                flags = ReferenceEquals(flagsArg, Symbols.Undefined) ? string.Empty : JsOps.ToJsString(flagsArg);
            }

            // Reinitialize the existing RegExp instance per RegExpInitialize(O, P, F)
            try
            {
                ValidateGroupNames(pattern);

                if (!target.TryGetProperty("constructor", out var ctor) ||
                    !ReferenceEquals(ctor, realm.RegExpConstructor))
                {
                    throw ThrowTypeError("RegExp.prototype.compile called on incompatible receiver", realm: realm);
                }

                var lastIndexDescriptor = target.GetOwnPropertyDescriptor("lastIndex");

                var reinitialized = new JsRegExp(pattern, flags, realm, target);
                target.SetProperty("__regex__", reinitialized);

                if (lastIndexDescriptor is { IsAccessorDescriptor: false, Writable: false })
                {
                    throw ThrowTypeError("Cannot assign to read only property 'lastIndex'", realm: realm);
                }

                target.SetProperty("lastIndex", 0d);
            }
            catch (ThrowSignal)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ThrowSignal(CreateSyntaxError(ex.Message, realm));
            }
            return target;
        });
        DefineBuiltinFunction(prototype, "compile", compileFn, 2, isConstructor: false);
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
        RegExpStatics EnsureRegExpReceiver(object? thisValue)
        {
            if (!ReferenceEquals(thisValue, realm.RegExpConstructor))
            {
                throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm);
            }

            return realm.RegExpStatics;
        }

        PropertyDescriptor MakeAccessor(Func<RegExpStatics, object?> getter)
        {
            return new PropertyDescriptor
            {
                Get = new HostFunction((thisValue, _) =>
                {
                    var statics = EnsureRegExpReceiver(thisValue);
                    return getter(statics);
                })
                { IsConstructor = false },
                Set = null,
                Enumerable = false,
                Configurable = true
            };
        }

        object? GetCapture(RegExpStatics s, int index) => index < s.Captures.Length ? s.Captures[index] : string.Empty;

        var inputDescriptor = new PropertyDescriptor
        {
            Get = new HostFunction((thisValue, _) =>
            {
                var statics = EnsureRegExpReceiver(thisValue);
                return statics.Input;
            })
            { IsConstructor = false },
            Set = new HostFunction((thisValue, args) =>
            {
                var statics = EnsureRegExpReceiver(thisValue);
                var value = args.Count > 0 ? args[0] : Symbols.Undefined;
                statics.Input = value?.ToString() ?? string.Empty;
                return null;
            })
            { IsConstructor = false },
            Enumerable = false,
            Configurable = true
        };

        var lastMatchDescriptor = MakeAccessor(s => s.LastMatch);
        var lastParenDescriptor = MakeAccessor(s => s.LastParen);
        var leftDescriptor = MakeAccessor(s => s.LeftContext);
        var rightDescriptor = MakeAccessor(s => s.RightContext);

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
            var captureDescriptor = MakeAccessor(s => GetCapture(s, idx - 1));
            constructor.DefineProperty($"${idx}", captureDescriptor);
        }

        // RegExp.multiline legacy accessor aliases RegExp.prototype.flags? Treat as global statics flag.
        var multilineDescriptor = new PropertyDescriptor
        {
            Get = new HostFunction((thisValue, _) =>
            {
                if (!ReferenceEquals(thisValue, realm.RegExpConstructor))
                {
                    throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm);
                }

                return false;
            })
            { IsConstructor = false },
            Set = null,
            Enumerable = false,
            Configurable = true
        };
        constructor.DefineProperty("multiline", multilineDescriptor);
    }

    private static void DefineRegExpAccessors(JsObject prototype, RealmState realm)
    {
        static string GetSortedFlags(JsRegExp r)
        {
            Span<char> buffer = stackalloc char[6];
            var length = 0;
            if (r.Global) buffer[length++] = 'g';
            if (r.IgnoreCase) buffer[length++] = 'i';
            if (r.Multiline) buffer[length++] = 'm';
            if (r.DotAll) buffer[length++] = 's';
            if (r.Unicode) buffer[length++] = 'u';
            if (r.Sticky) buffer[length++] = 'y';
            return new string(buffer[..length]);
        }

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

        prototype.DefineProperty("flags", MakeGetter(GetSortedFlags));
        prototype.DefineProperty("source", MakeGetter(r => string.IsNullOrEmpty(r.Pattern) ? "(?:)" : r.Pattern));
        prototype.DefineProperty("global", MakeGetter(r => r.Global));
        prototype.DefineProperty("ignoreCase", MakeGetter(r => r.IgnoreCase));
        prototype.DefineProperty("multiline", MakeGetter(r => r.Multiline));
    }

    private static uint ToUint32(object? value)
    {
        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return 0;
        }

        var int64 = (long)number;
        return (uint)(int64 & 0xFFFFFFFF);
    }
}
