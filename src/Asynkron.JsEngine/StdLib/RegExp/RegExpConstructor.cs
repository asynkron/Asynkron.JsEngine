#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.RegExpHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("RegExp", PrototypeType = typeof(RegExpPrototype), Length = 2d, DisplayName = "RegExp")]
public sealed partial class RegExpConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("RegExp constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            var target = _constructor ?? ConstructFallback;
            return ConstructRegExp(args, target, target, constructing);
        }

        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() : null;
        return ConstructRegExp(args, targetCtor, targetCtor, providedThis);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.RegExpPrototype ??= Prototype as JsObject;
        Realm.RegExpConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisArg, _, newTarget) =>
        {
            var targetCtor = _constructor ?? constructor;
            IJsCallable effectiveNewTarget;
            var isNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable);
            if (isNewTarget)
            {
                effectiveNewTarget = callable!;
            }
            else
            {
                // Called as function (not new): newTarget = active function object
                effectiveNewTarget = targetCtor;

                // Per spec 22.2.3.1 step 3b: If patternIsRegExp and flags is undefined,
                // check if pattern.constructor === newTarget, and if so return pattern directly.
                if (args.Count > 0)
                {
                    var patternArg = args[0];
                    var flagsArg = args.Count > 1 ? args[1] : JsValue.Undefined;

                    if (flagsArg.IsUndefined && IsRegExpAbrupt(patternArg))
                    {
                        // Get pattern.constructor
                        if (JsOps.TryGetPropertyValue(patternArg, "constructor", out var patternCtor) &&
                            patternCtor.TryGetObject<IJsCallable>(out var ctorCallable) &&
                            ReferenceEquals(ctorCallable, targetCtor))
                        {
                            return patternArg;
                        }
                    }
                }
            }

            var thisObj = thisArg.TryGetObject<JsObject>(out var matchedThisObj)
                ? matchedThisObj
                : null;
            return ConstructRegExp(args, effectiveNewTarget, targetCtor, thisObj);
        });

        DefineLegacyRegExpAccessors(constructor, Realm);

        // RegExp.escape(string) — escapes a string for use in a RegExp
        var escapeFn = new HostFunction(
            (_, args) =>
            {
                var arg = args.GetArgument(0);
                if (!arg.IsString)
                {
                    throw StandardLibrary.ThrowTypeError("RegExp.escape requires a string argument", realm: Realm);
                }

                var str = arg.AsString();
                return (JsValue)EncodeForRegExpEscape(str);
            }, Realm, false);
        escapeFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = (JsValue)"escape", Writable = false, Enumerable = false, Configurable = true
        });
        escapeFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = JsValue.FromDouble(1), Writable = false, Enumerable = false, Configurable = true
        });
        constructor.SetHostedProperty("escape", escapeFn, Realm);
    }

    /// <summary>
    /// Per spec: EncodeForRegExpEscape encodes a string so that it can be used
    /// as a literal pattern in a RegExp constructor.
    /// </summary>
    private static string EncodeForRegExpEscape(string s)
    {
        if (s.Length == 0) return "";

        var sb = new System.Text.StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];

            if (i == 0 && IsAsciiLetterOrDigit(c))
            {
                AppendHexEscape(sb, c);
                continue;
            }

            switch (c)
            {
                // SyntaxCharacter :: one of ^ $ \ . * + ? ( ) [ ] { } |
                case '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                case '/':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                case ' ':
                    sb.Append("\\x20");
                    break;
                case '\u0085':
                case '\u00A0':
                    AppendHexEscape(sb, c);
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\v':
                    sb.Append("\\v");
                    break;
                default:
                    if (IsOtherPunctuator(c))
                    {
                        AppendHexEscape(sb, c);
                    }
                    else if (RequiresUnicodeEscape(c))
                    {
                        AppendUnicodeEscape(sb, c);
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();

        static bool IsAsciiLetterOrDigit(char c)
        {
            return c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        static bool IsOtherPunctuator(char c)
        {
            return c is ',' or '-' or '=' or '<' or '>' or '#' or '&' or '!' or '%' or ':' or ';' or '@' or '~' or '\'' or '"' or '`';
        }

                static bool RequiresUnicodeEscape(char c)
                {
                    return c <= 0x1F ||
                           c == 0x7F ||
                           char.IsSurrogate(c) ||
                           c == 0xFEFF ||
                           c == 0x1680 ||
                           c is >= '\u2000' and <= '\u200A' ||
                           c == '\u2028' ||
                   c == '\u2029' ||
                   c == '\u202F' ||
                   c == '\u205F' ||
                   c == '\u3000';
        }

        static void AppendHexEscape(System.Text.StringBuilder sb, char c)
        {
            sb.Append("\\x");
            sb.Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        static void AppendUnicodeEscape(System.Text.StringBuilder sb, char c)
        {
            sb.Append("\\u");
            sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        // Keep species defaulted to the constructor for subclassing behavior.
        return thisValue;
    }

    private JsValue ConstructRegExp(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor,
        JsObject? thisArg)
    {
        var provided = thisArg != null &&
                       (thisArg.IsConstructing || IsRegExpLikeInstance(thisArg, Realm))
            ? thisArg
            : null;
        var instance = PrepareTargetInstance(provided, newTarget, targetCtor);
        return (JsValue)InitializeRegExp(args, instance);
    }

    private JsObject PrepareTargetInstance(JsObject? provided, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var instance = provided ?? PrepareThisObject(JsValue.Undefined, false);
        if (instance.RealmState is null)
        {
            instance.RealmState = Realm;
        }

        if (instance.Prototype is not null)
        {
            return instance;
        }

        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        instance.SetPrototype(proto);

        return instance;
    }

    private JsObject InitializeRegExp(IReadOnlyList<JsValue> args, JsObject? target)
    {
        if (args.Count == 0)
        {
            return CreateRegExpLiteral("(?:)", "", Realm, target);
        }

        var patternArg = args[0];
        var flagsArg = args.Count > 1 ? args[1] : JsValue.Undefined;

        // Step 2: Let patternIsRegExp be ? IsRegExp(pattern).
        // This must happen before checking [[RegExpMatcher]] for observable behavior.
        var patternIsRegExp = IsRegExpAbrupt(patternArg);

        // Step 3: If pattern is an Object and has [[RegExpMatcher]] internal slot.
        if (patternArg.IsObject && patternArg.AsObject() is { } existingObj &&
            existingObj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex.TryGetObject<JsRegExp>(out var existing))
        {
            // Per spec: if flags argument is provided, use it; otherwise use the RegExp's original flags.
            var flags = flagsArg != JsValue.Undefined
                ? JsOps.ToJsString(flagsArg) ?? string.Empty
                : existing.Flags;
            return CreateRegExpLiteral(existing.Pattern, flags, Realm, target);
        }

        // Step 4: Else if patternIsRegExp is true (object passes IsRegExp but has no internal slot).
        if (patternIsRegExp)
        {
            string p;
            if (JsOps.TryGetPropertyValue(patternArg, "source", out var sourceVal))
                p = JsOps.ToJsString(sourceVal) ?? string.Empty;
            else
                p = string.Empty;

            string f;
            if (flagsArg.IsUndefined)
            {
                if (JsOps.TryGetPropertyValue(patternArg, "flags", out var flagsVal))
                    f = JsOps.ToJsString(flagsVal) ?? string.Empty;
                else
                    f = string.Empty;
            }
            else
            {
                f = JsOps.ToJsString(flagsArg) ?? string.Empty;
            }

            return CreateRegExpLiteral(p, f, Realm, target);
        }

        // Step 5: Else — pattern is not a RegExp.
        var pattern = patternArg.IsUndefined ? string.Empty : JsOps.ToJsString(patternArg) ?? string.Empty;
        var flagsStr = flagsArg.IsUndefined
            ? string.Empty
            : JsOps.ToJsString(flagsArg) ?? string.Empty;
        return CreateRegExpLiteral(pattern, flagsStr, Realm, target);
    }

    /// <summary>
    /// 7.2.8 IsRegExp ( argument ) with abrupt completion propagation.
    /// Reads @@match property which may throw — exceptions propagate naturally.
    /// </summary>
    private static bool IsRegExpAbrupt(JsValue argument)
    {
        if (!argument.IsObject)
            return false;

        // Step 2: Let matcher be ? Get(argument, @@match).
        // TryGetPropertyValue may throw ThrowSignal if the getter throws — that propagates naturally.
        if (JsOps.TryGetPropertyValue(argument, SymbolKeys.Match, out var matchValue))
        {
            // Step 3: If matcher is not undefined, return ! ToBoolean(matcher).
            if (!matchValue.IsUndefined)
                return matchValue.IsTruthy;
        }

        // Step 4: If argument has a [[RegExpMatcher]] internal slot, return true.
        if (argument.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__regex__", out var regexVal) &&
            regexVal.TryGetObject<JsRegExp>(out _))
        {
            return true;
        }

        return argument.TryGetObject<JsRegExp>(out _);
    }
}
