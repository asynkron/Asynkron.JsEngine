using System.Text.RegularExpressions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateRegExpConstructor(RealmState realm)
    {
        return RegExpConstructor.CreateConstructor(realm);
    }

    internal static JsObject CreateRegExpLiteral(string pattern, string flags, RealmState? realm = null,
        JsObject? existingInstance = null)
    {
        try
        {
            ValidateGroupNames(pattern);
            var regex = new JsRegExp(pattern, flags, realm, existingInstance);
            var target = regex.JsObject;
            target["__regex__"] = regex;

            if (existingInstance is null && realm?.RegExpPrototype is not null)
            {
                target.SetPrototype(realm.RegExpPrototype);
            }

            var lastIndexDescriptor = target.GetOwnPropertyDescriptor("lastIndex");
            if (lastIndexDescriptor is null)
            {
                target.DefineProperty("lastIndex",
                    new PropertyDescriptor { Value = 0d, Writable = true, Enumerable = false, Configurable = false });
            }

            return target;
        }
        catch (ParseException ex)
        {
            throw new ThrowSignal(CreateSyntaxError(ex.Message, realm: realm));
        }
        catch (ArgumentException ex)
        {
            throw new ThrowSignal(CreateSyntaxError(ex.Message, realm: realm));
        }
    }

    internal static void ValidateGroupNames(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;
        var inCharClass = false;
        var escaped = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (inCharClass)
            {
                if (c == ']')
                {
                    inCharClass = false;
                }

                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                continue;
            }

            if (c == '|' && depth == 0)
            {
                seenNames.Clear();
                continue;
            }

            if (c == '(')
            {
                depth++;
                if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<')
                {
                    if (i + 3 < pattern.Length && (pattern[i + 3] == '=' || pattern[i + 3] == '!'))
                    {
                        continue;
                    }

                    var end = pattern.IndexOf('>', i + 3);
                    if (end == -1)
                    {
                        break;
                    }

                    var name = pattern.Substring(i + 3, end - (i + 3));
                    var normalized = JsRegExp.NormalizeGroupNameToken(name);
                    if (!seenNames.Add(normalized))
                    {
                        throw new ParseException("Invalid regular expression: duplicate group name.");
                    }

                    i = end;
                }

                continue;
            }

            if (c == ')' && depth > 0)
            {
                depth--;
            }
        }
    }

    internal static JsRegExp? ResolveRegExpInstance(JsValue thisValue)
    {
        if (thisValue.TryGetObject<JsRegExp>(out var direct))
        {
            return direct;
        }

        if (thisValue.TryGetObject<JsObject>(out var obj) &&
            obj is not null &&
            obj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex is JsRegExp stored)
        {
            return stored;
        }

        return null;
    }

    internal static bool IsRegExpLikeInstance(JsObject obj, RealmState realm)
    {
        var current = obj.Prototype;
        while (current is not null)
        {
            if (ReferenceEquals(current, realm.RegExpPrototype))
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

    internal static uint ToUint32(JsValue value)
    {
        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return 0;
        }

        var int64 = (long)number;
        return (uint)(int64 & 0xFFFFFFFF);
    }

    internal static void ResetLastIndex(RealmState realm, JsObject target)
    {
        var descriptor = target.GetOwnPropertyDescriptor("lastIndex");
        if (descriptor is not null)
        {
            if (descriptor.IsAccessorDescriptor)
            {
                descriptor.Set?.Invoke([new JsValue(0d)], target);
                return;
            }

            if (!descriptor.Writable)
            {
                throw ThrowTypeError("Cannot assign to read only property 'lastIndex'", realm: realm);
            }

            target["lastIndex"] = 0d;
            return;
        }

        target.SetProperty("lastIndex", 0d);
    }

    internal static void UpdateRegExpStatics(this RealmState? realm, string input, Match match)
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

    internal static void DefineLegacyRegExpAccessors(HostFunction constructor, RealmState realm)
    {
        RegExpStatics EnsureRegExpReceiver(JsValue thisValue)
        {
            if (!ReferenceEquals(thisValue.ObjectValue, realm.RegExpConstructor))
            {
                throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm);
            }

            return realm.RegExpStatics;
        }

        PropertyDescriptor MakeAccessor(Func<RegExpStatics, string> getter)
        {
            return new PropertyDescriptor
            {
                Get = new HostFunction((thisValue, _) =>
                {
                    var statics = EnsureRegExpReceiver(thisValue);
                    return new JsValue(getter(statics));
                }, isConstructor: false),
                Set = null,
                Enumerable = false,
                Configurable = true
            };
        }

        string GetCapture(RegExpStatics s, int index)
        {
            return index < s.Captures.Length ? s.Captures[index] : string.Empty;
        }

        var inputDescriptor = new PropertyDescriptor
        {
            Get = new HostFunction((thisValue, _) =>
            {
                var statics = EnsureRegExpReceiver(thisValue);
                return new JsValue(statics.Input);
            }, isConstructor: false),
            Set = new HostFunction((thisValue, args) =>
            {
                var statics = EnsureRegExpReceiver(thisValue);
                var value = args.GetArgument(0);
                statics.Input = JsOps.ToString(value);
                return JsValue.Undefined;
            }, isConstructor: false),
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

        var multilineDescriptor = new PropertyDescriptor
        {
            Get = new HostFunction((thisValue, _) =>
                !ReferenceEquals(thisValue.ObjectValue, realm.RegExpConstructor)
                    ? throw ThrowTypeError("RegExp method called on incompatible receiver", realm: realm)
                    : JsValue.False, isConstructor: false),
            Set = null,
            Enumerable = false,
            Configurable = true
        };
        constructor.DefineProperty("multiline", multilineDescriptor);
    }
}
