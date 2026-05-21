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

[JsPrototype("RegExp")]
public sealed partial class RegExpPrototype
{
    [JsHostMethod("test", Length = 1d)]
    public JsValue Test(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);

        // Per spec 22.2.5.13 step 3: Let S be ? ToString(string).
        // When called with no arguments, ToString(undefined) = "undefined".
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : "undefined";
        return new JsValue(resolved.Test(input));
    }

    [JsHostMethod("exec", Length = 1d)]
    public JsValue Exec(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);

        // Per spec 22.2.5.2 step 3: Let S be ? ToString(string).
        // When called with no arguments, ToString(undefined) = "undefined".
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : "undefined";
        var result = resolved.Exec(input);
        return result is null ? JsValue.Null : JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var sourceValue = Source(thisValue);
        var flagsValue = Flags(thisValue);
        var sourceText = JsOps.ToJsString(sourceValue);
        var flagsText = JsOps.ToJsString(flagsValue);
        var result = $"/{sourceText}/{flagsText}";
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
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return new JsValue("(?:)");
        }

        var resolved = RequireRegExp(thisValue);
        var result = string.IsNullOrEmpty(resolved.Pattern) ? "(?:)" : EscapeRegExpPattern(resolved.Pattern);
        return new JsValue(result);
    }

    [JsHostGetter("global")]
    public JsValue Global(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).Global);
    }

    [JsHostGetter("ignoreCase")]
    public JsValue IgnoreCase(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).IgnoreCase);
    }

    [JsHostGetter("multiline")]
    public JsValue Multiline(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).Multiline);
    }

    [JsHostGetter("dotAll")]
    public JsValue DotAll(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).DotAll);
    }

    [JsHostGetter("hasIndices")]
    public JsValue HasIndices(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).HasIndices);
    }

    [JsHostGetter("unicode")]
    public JsValue Unicode(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).Unicode);
    }

    [JsHostGetter("unicodeSets")]
    public JsValue UnicodeSets(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).UnicodeSets);
    }

    [JsHostGetter("sticky")]
    public JsValue Sticky(JsValue thisValue)
    {
        if (IsRegExpPrototypeReceiver(thisValue))
        {
            return JsValue.Undefined;
        }

        return new JsValue(RequireRegExp(thisValue).Sticky);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.RegExpPrototype ??= Prototype as JsObject;
    }

    private JsRegExp RequireRegExp(JsValue receiver)
    {
        if (IsRegExpPrototypeReceiver(receiver))
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var resolved = ResolveRegExpInstance(receiver);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        return resolved;
    }

    private bool IsRegExpPrototypeReceiver(JsValue receiver)
    {
        return receiver.TryGetObject<JsObject>(out var obj) && ReferenceEquals(obj, Realm.RegExpPrototype);
    }

    private static string EscapeRegExpPattern(string pattern)
    {
        if (pattern.Length == 0)
        {
            return "(?:)";
        }

        var builder = new StringBuilder(pattern.Length);
        foreach (var ch in pattern)
        {
            switch (ch)
            {
                case '/':
                    builder.Append("\\/");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\u2028':
                    builder.Append("\\u2028");
                    break;
                case '\u2029':
                    builder.Append("\\u2029");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    // =====================================================================
    // ECMAScript spec: 22.2.5.8 RegExp.prototype[@@match] (string)
    // =====================================================================
    [JsSymbolMethod("match", Length = 1d)]
    private JsValue MatchSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Step 1: If Type(rx) is not Object, throw a TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp.prototype[Symbol.match] requires an object receiver", realm: Realm);
        }

        // Step 2: Let S be ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;

        // Step 3: Let flags be ToString(Get(rx, "flags")).
        JsOps.TryGetPropertyValue(thisValue, "flags", out var flagsValue);
        var flags = JsOps.ToJsString(flagsValue);

        // Step 4: If flags does not contain "g", return RegExpExec(rx, S).
        var isGlobal = flags.Contains('g', StringComparison.Ordinal);

        if (!isGlobal)
        {
            return RegExpExec(thisValue, input);
        }

        // Step 5a: Let fullUnicode be flags contains "u".
        var fullUnicode = flags.Contains('u', StringComparison.Ordinal);

        // Step 5b: Set lastIndex to 0.
        SetProperty(thisValue, "lastIndex", new JsValue(0d));

        // Step 5c: Let A be ArrayCreate(0).
        var results = new JsArray(Realm);
        var n = 0;

        // Step 5d: Repeat
        while (true)
        {
            var result = RegExpExec(thisValue, input);
            if (result == JsValue.Null)
            {
                return n == 0 ? JsValue.Null : JsValue.FromJsArray(results);
            }

            // Get matched string
            JsOps.TryGetPropertyValue(result, "0", out var matchValue);
            var matchStr = JsOps.ToJsString(matchValue);
            results.Push(matchStr);
            n++;

            if (matchStr.Length == 0)
            {
                // Advance lastIndex to avoid infinite loop on zero-length match
                JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var lastIndexValue);
                var thisIndex = ToLengthOrZero(lastIndexValue);
                var nextIndex = AdvanceStringIndexDouble(input, thisIndex, fullUnicode);
                SetProperty(thisValue, "lastIndex", new JsValue(nextIndex));
            }
        }
    }

    [JsSymbolMethod("matchAll", Length = 1d)]
    private JsValue MatchAllSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // ES2024 22.2.5.8 RegExp.prototype[@@matchAll](string)
        // Step 2: If Type(R) is not Object, throw TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp.prototype[@@matchAll] requires an object receiver", realm: Realm);
        }

        // Step 3: Let S be ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;

        // Step 4: Let C be SpeciesConstructor(R, %RegExp%).
        var speciesCtor = GetSpeciesConstructorForMatchAll(thisValue);

        // Step 5: Let flags be ToString(Get(R, "flags")).
        JsOps.TryGetPropertyValue(thisValue, "flags", out var flagsValue);
        var flags = JsOps.ToJsString(flagsValue) ?? string.Empty;

        // Step 6: Let matcher be Construct(C, [R, flags]).
        var flagsArg = new JsValue(flags);
        JsValue matcherValue;
        if (speciesCtor is not null)
        {
            matcherValue = ReflectHelper.Construct(speciesCtor, [thisValue, flagsArg], speciesCtor, Realm);
        }
        else if (Realm.RegExpConstructor is IJsCallable regExpCtor)
        {
            // Default: Construct(%RegExp%, [R, flags]) — invokes full constructor path
            // which calls IsRegExp on the pattern and handles all observable operations
            matcherValue = ReflectHelper.Construct(regExpCtor, [thisValue, flagsArg], regExpCtor, Realm);
        }
        else
        {
            // Fallback: create directly if RegExp constructor not available
            var resolved = ResolveRegExpFromThisValue(thisValue);
            var pattern = resolved?.Pattern ?? JsOps.ToJsString(thisValue) ?? string.Empty;
            var matcherObj = RegExpHelper.CreateRegExpLiteral(pattern, flags, Realm);
            matcherValue = matcherObj.AsJsValue;
        }

        // Step 7-8: Let lastIndex = ToLength(Get(R, "lastIndex")); Set(matcher, "lastIndex", lastIndex).
        JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var lastIndexValue);
        var lastIndex = ToLengthOrZero(lastIndexValue);
        if (matcherValue.TryGetObjectLike(out var matcherAccessor))
        {
            matcherAccessor.SetProperty("lastIndex", new JsValue((double)lastIndex));
        }

        // Step 9-10: Determine global and fullUnicode from flags.
        var isGlobal = flags.Contains('g', StringComparison.Ordinal);
        var fullUnicode = flags.Contains('u', StringComparison.Ordinal) ||
                          flags.Contains('v', StringComparison.Ordinal);

        // Step 11: Return CreateRegExpStringIterator(matcher, S, global, fullUnicode).
        var prototype = GetOrCreateRegExpStringIteratorPrototype();
        var iterator = new JsRegExpStringIterator(
            matcherValue, input, isGlobal, fullUnicode, Realm, prototype);
        return iterator.AsJsValue;
    }

    private IJsCallable? GetSpeciesConstructorForMatchAll(JsValue thisValue)
    {
        // SpeciesConstructor(O, defaultConstructor)
        // 1. Let C be Get(O, "constructor").
        if (!JsOps.TryGetPropertyValue(thisValue, "constructor", out var ctorValue) ||
            ctorValue.IsUndefined)
        {
            return null; // Step 3: If C is undefined, return defaultConstructor.
        }

        // Step 4: If Type(C) is not Object, throw a TypeError exception.
        if (!ctorValue.IsObject)
        {
            throw ThrowTypeError("constructor is not an object", realm: Realm);
        }

        // Step 5: Let S be Get(C, @@species).
        if (!JsOps.TryGetPropertyValue(ctorValue, SymbolKeys.Species, out var speciesValue) ||
            speciesValue.IsNullOrUndefined)
        {
            return null; // Step 6: If S is undefined or null, return defaultConstructor.
        }

        // 4. If IsConstructor(S), return S.
        if (speciesValue.TryGetObject<IJsCallable>(out var speciesCtor))
        {
            return speciesCtor;
        }

        throw ThrowTypeError("Species constructor is not a constructor", realm: Realm);
    }

    private static JsRegExp? ResolveRegExpFromThisValue(JsValue thisValue)
    {
        if (thisValue.TryGetObject<JsRegExp>(out var direct))
        {
            return direct;
        }

        if (thisValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__regex__", out var regexVal) &&
            regexVal.TryGetObject<JsRegExp>(out var stored))
        {
            return stored;
        }

        return null;
    }

    private JsObject? GetOrCreateRegExpStringIteratorPrototype()
    {
        return Realm.RegExpStringIteratorPrototype ??=
            (JsObject)RegExpStringIteratorPrototype.CreatePrototype(Realm);
    }

    // =====================================================================
    // ECMAScript spec: 22.2.5.10 RegExp.prototype[@@replace] (string, replaceValue)
    // =====================================================================
    [JsSymbolMethod("replace", Length = 2d)]
    private JsValue ReplaceSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Step 1: If Type(rx) is not Object, throw a TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp.prototype[Symbol.replace] requires an object receiver", realm: Realm);
        }

        // Step 2: Let S be ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;
        var replaceValue = args.GetArgument(1);

        // Step 3: Let lengthS be the length of S.
        var lengthS = input.Length;

        // Step 4: Let functionalReplace be IsCallable(replaceValue).
        var functionalReplace = replaceValue.TryGetObject<IJsCallable>(out var replaceFn);

        // Step 5: If functionalReplace is false, let replaceValue be ToString(replaceValue).
        var replaceStr = functionalReplace ? string.Empty : JsOps.ToJsString(replaceValue);

        // Step 6: Let flags be ToString(Get(rx, "flags")).
        JsOps.TryGetPropertyValue(thisValue, "flags", out var flagsValue);
        var flags = JsOps.ToJsString(flagsValue);

        // Step 7: If flags contains "g", let global be true, else let global be false.
        var isGlobal = flags.Contains('g', StringComparison.Ordinal);

        if (!functionalReplace &&
            isGlobal &&
            TryReplaceLegacyGlobalNonWhitespacePlus(thisValue, input, replaceStr, out var fastReplaceResult))
        {
            return new JsValue(fastReplaceResult);
        }

        bool fullUnicode = false;
        if (isGlobal)
        {
            // Step 8: If global is true, let fullUnicode be ToBoolean(Get(rx, "unicode")).
            fullUnicode = flags.Contains('u', StringComparison.Ordinal);
            // Step 8b: Set lastIndex to 0.
            SetProperty(thisValue, "lastIndex", new JsValue(0d));
        }

        // Step 8: Let results be a new empty List.
        var results = new List<JsValue>();

        // Step 9: Let done be false.
        // Step 10: Repeat, while done is false
        while (true)
        {
            var result = RegExpExec(thisValue, input);
            if (result == JsValue.Null)
            {
                break;
            }

            results.Add(result);

            if (!isGlobal)
            {
                break;
            }

            // Get matched string to check for zero-length match
            JsOps.TryGetPropertyValue(result, "0", out var matchValue);
            var matchStr = JsOps.ToJsString(matchValue);
            if (matchStr.Length == 0)
            {
                JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var lastIndexValue);
                var thisIndex = ToLengthOrZero(lastIndexValue);
                var nextIndex = AdvanceStringIndexDouble(input, thisIndex, fullUnicode);
                SetProperty(thisValue, "lastIndex", new JsValue(nextIndex));
            }
        }

        // Step 14: Let accumulatedResult be the empty String.
        var accumulatedResult = new StringBuilder();
        // Step 15: Let nextSourcePosition be 0.
        var nextSourcePosition = 0;

        // Step 16: For each element result of results
        foreach (var result in results)
        {
            // Step 16a: Let nCaptures be ToLength(Get(result, "length")).
            JsOps.TryGetPropertyValue(result, "length", out var lengthValue);
            var nCaptures = (int)ToLengthOrZero(lengthValue);
            nCaptures = Math.Max(nCaptures - 1, 0);

            // Step 16b: Let matched be ToString(Get(result, "0")).
            JsOps.TryGetPropertyValue(result, "0", out var matchedValue);
            var matched = JsOps.ToJsString(matchedValue);

            // Step 16c: Let matchLength be the length of matched.
            var matchLength = matched.Length;

            // Step 16d: Let position be ToInteger(Get(result, "index")).
            JsOps.TryGetPropertyValue(result, "index", out var positionValue);
            var position = (int)JsOps.ToNumber(positionValue);
            position = Math.Max(Math.Min(position, lengthS), 0);

            // Step 16e-f: Build captures list.
            var captures = new List<JsValue>(nCaptures);
            for (var n = 1; n <= nCaptures; n++)
            {
                JsOps.TryGetPropertyValue(result, n.ToString(CultureInfo.InvariantCulture), out var captureN);
                if (captureN != JsValue.Undefined)
                {
                    captureN = new JsValue(JsOps.ToJsString(captureN));
                }

                captures.Add(captureN);
            }

            // Step 16g: Let namedCaptures be Get(result, "groups").
            JsOps.TryGetPropertyValue(result, "groups", out var namedCaptures);

            string replacement;
            if (functionalReplace)
            {
                // Step 16h: Build arguments: matched, captures..., position, S, [groups]
                var fnArgs = new List<JsValue>(nCaptures + 4) { new(matched) };
                fnArgs.AddRange(captures);
                fnArgs.Add(new JsValue((double)position));
                fnArgs.Add(new JsValue(input));
                if (namedCaptures != JsValue.Undefined)
                {
                    fnArgs.Add(namedCaptures);
                }

                var replResult = replaceFn!.Invoke(fnArgs, JsValue.Undefined);
                replacement = JsOps.ToJsString(replResult);
            }
            else
            {
                // Step 16l.i: If namedCaptures is not undefined, set namedCaptures to ToObject(namedCaptures).
                if (namedCaptures != JsValue.Undefined)
                {
                    if (namedCaptures == JsValue.Null)
                    {
                        throw ThrowTypeError("Cannot convert null to object", realm: Realm);
                    }
                }

                // Step 16l.ii: Let replacement be GetSubstitution(matched, S, position, captures, namedCaptures, replaceValue).
                replacement = GetSubstitution(matched, input, position, captures, namedCaptures, replaceStr);
            }

            // Step 16j: If position >= nextSourcePosition
            if (position >= nextSourcePosition)
            {
                accumulatedResult.Append(input.AsSpan(nextSourcePosition, position - nextSourcePosition));
                accumulatedResult.Append(replacement);
                nextSourcePosition = position + matchLength;
            }
        }

        // Step 17: Append remainder of string.
        if (nextSourcePosition < lengthS)
        {
            accumulatedResult.Append(input.AsSpan(nextSourcePosition));
        }

        return new JsValue(accumulatedResult.ToString());
    }

    private bool TryReplaceLegacyGlobalNonWhitespacePlus(
        JsValue thisValue,
        string input,
        string replacement,
        out string result)
    {
        result = string.Empty;
        if (replacement.Contains('$', StringComparison.Ordinal))
        {
            return false;
        }

        var regex = ResolveRegExpFromThisValue(thisValue);
        if (regex is null ||
            !string.Equals(regex.Pattern, @"\S+", StringComparison.Ordinal) ||
            !string.Equals(regex.Flags, "g", StringComparison.Ordinal))
        {
            return false;
        }

        SetProperty(thisValue, "lastIndex", new JsValue(0d));

        StringBuilder? builder = null;
        var sourcePosition = 0;
        var lastMatchIndex = -1;
        var lastMatchLength = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (IsEcmaWhitespace(input[i]))
            {
                continue;
            }

            var end = i + 1;
            while (end < input.Length && !IsEcmaWhitespace(input[end]))
            {
                end++;
            }

            builder ??= new StringBuilder(input.Length + replacement.Length);
            builder.Append(input.AsSpan(sourcePosition, i - sourcePosition));
            builder.Append(replacement);
            sourcePosition = end;
            lastMatchIndex = i;
            lastMatchLength = end - i;
            i = end - 1;
        }

        if (builder is null)
        {
            result = input;
            return true;
        }

        builder.Append(input.AsSpan(sourcePosition));
        result = builder.ToString();
        Realm.UpdateRegExpStatics(input, lastMatchIndex, lastMatchLength);
        return true;
    }

    private static bool IsEcmaWhitespace(char c)
    {
        switch (c)
        {
            case '\t':
            case '\n':
            case '\v':
            case '\f':
            case '\r':
            case ' ':
            case '\u00a0':
            case '\u1680':
            case '\u2028':
            case '\u2029':
            case '\u202f':
            case '\u205f':
            case '\u3000':
            case '\ufeff':
                return true;
            default:
                return c >= '\u2000' && c <= '\u200a';
        }
    }

    [JsSymbolMethod("search", Length = 1d)]
    private JsValue SearchSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // ES spec 22.2.5.11 RegExp.prototype[@@search](string)
        // Step 1-2: Type check
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp.prototype[Symbol.search] requires an object receiver", realm: Realm);
        }

        // Step 3: Let string be ? ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;

        // Step 4: Let previousLastIndex be ? Get(rx, "lastIndex").
        JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var previousLastIndex);

        // Step 5: If SameValue(previousLastIndex, +0) is false, then
        //   a. Perform ? Set(rx, "lastIndex", +0, true).
        if (!JsOps.SameValue(previousLastIndex, new JsValue(0d)))
        {
            SetPropertyOrThrow(thisValue, "lastIndex", new JsValue(0d));
        }

        // Step 6: Let result be ? RegExpExec(rx, string).
        var result = RegExpExec(thisValue, input);

        // Step 7: Let currentLastIndex be ? Get(rx, "lastIndex").
        JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var currentLastIndex);

        // Step 8: If SameValue(currentLastIndex, previousLastIndex) is false, then
        //   a. Perform ? Set(rx, "lastIndex", previousLastIndex, true).
        if (!JsOps.SameValue(currentLastIndex, previousLastIndex))
        {
            SetPropertyOrThrow(thisValue, "lastIndex", previousLastIndex);
        }

        // Step 9: If result is null, return -1.
        if (result == JsValue.Null)
        {
            return new JsValue(-1d);
        }

        // Step 10: Return ? Get(result, "index").
        JsOps.TryGetPropertyValue(result, "index", out var indexValue);
        return indexValue;
    }

    // =====================================================================
    // ECMAScript spec: 22.2.5.13 RegExp.prototype[@@split] (string, limit)
    // =====================================================================
    [JsSymbolMethod("split", Length = 2d)]
    private JsValue SplitSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Step 1: If Type(rx) is not Object, throw a TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp.prototype[Symbol.split] requires an object receiver", realm: Realm);
        }

        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;
        var limitValue = args.GetArgument(1);

        // Step 4: Let C be SpeciesConstructor(rx, %RegExp%).
        var constructor = SpeciesConstructor(thisValue);

        // Step 5: Let flags be ToString(Get(rx, "flags")).
        JsOps.TryGetPropertyValue(thisValue, "flags", out var flagsValue);
        var flags = JsOps.ToJsString(flagsValue);

        // Step 6-8: Check for unicode and sticky flags, ensure y flag is present.
        var unicodeMatching = flags.Contains('u', StringComparison.Ordinal);
        var newFlags = flags.Contains('y', StringComparison.Ordinal) ? flags : flags + "y";

        // Step 9: Let splitter be Construct(C, rx, newFlags).
        var splitter = ConstructSplitter(constructor, thisValue, newFlags);

        // Step 10: Let A be ArrayCreate(0).
        var resultArray = new JsArray(Realm);

        // Step 11: Let lengthA be 0.
        // Step 12: Let lim
        var limit = limitValue == JsValue.Undefined
            ? uint.MaxValue
            : ToUint32(limitValue);

        if (limit == 0)
        {
            return JsValue.FromJsArray(resultArray);
        }

        var size = input.Length;
        if (size == 0)
        {
            var z = RegExpExec(splitter, input);
            if (z == JsValue.Null)
            {
                resultArray.Push(input);
            }

            return JsValue.FromJsArray(resultArray);
        }

        var p = 0;
        var q = p;

        while (q < size)
        {
            // Set lastIndex to q.
            SetProperty(splitter, "lastIndex", new JsValue((double)q));

            var z = RegExpExec(splitter, input);
            if (z == JsValue.Null)
            {
                q = AdvanceStringIndex(input, q, unicodeMatching);
                continue;
            }

            // Get e from lastIndex
            JsOps.TryGetPropertyValue(splitter, "lastIndex", out var eValue);
            var e = (int)ToLengthOrZero(eValue);
            e = Math.Min(e, size);

            if (e == p)
            {
                q = AdvanceStringIndex(input, q, unicodeMatching);
                continue;
            }

            // Add substring from p to q
            resultArray.Push(input.Substring(p, q - p));
            if (resultArray.Length >= limit)
            {
                return JsValue.FromJsArray(resultArray);
            }

            p = e;

            // Add capturing groups
            JsOps.TryGetPropertyValue(z, "length", out var numberOfCapturesValue);
            var numberOfCaptures = Math.Max((int)ToLengthOrZero(numberOfCapturesValue) - 1, 0);

            for (var i = 1; i <= numberOfCaptures; i++)
            {
                JsOps.TryGetPropertyValue(z, i.ToString(CultureInfo.InvariantCulture), out var cap);
                resultArray.Push(cap);
                if (resultArray.Length >= limit)
                {
                    return JsValue.FromJsArray(resultArray);
                }
            }

            q = p;
        }

        // Add tail
        resultArray.Push(input[p..]);
        return JsValue.FromJsArray(resultArray);
    }

    // =====================================================================
    // RegExpExec abstract operation (ES2024 22.2.5.2.1)
    // =====================================================================
    private JsValue RegExpExec(JsValue r, string s)
    {
        // Step 1: Let exec be Get(R, "exec").
        JsOps.TryGetPropertyValue(r, "exec", out var exec);

        // Step 2: If IsCallable(exec) is true, then
        if (exec.TryGetObject<IJsCallable>(out var callable))
        {
            var result = callable.Invoke(new SingleValueArgs(new JsValue(s)), r);
            // Step 2c: If Type(result) is neither Object nor Null, throw TypeError.
            if (result == JsValue.Null)
            {
                return JsValue.Null;
            }

            if (!result.IsObject)
            {
                throw ThrowTypeError("RegExp exec method returned a non-object or non-null result", realm: Realm);
            }

            return result;
        }

        // Step 3: If R does not have a [[RegExpMatcher]] internal slot, throw TypeError.
        var resolved = ResolveRegExpInstance(r);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp.prototype method called on incompatible receiver", realm: Realm);
        }

        // Step 4: Return RegExpBuiltinExec(R, S).
        var builtinResult = resolved.Exec(s);
        return builtinResult is null ? JsValue.Null : JsValue.FromObjectUnsafe(builtinResult);
    }

    // =====================================================================
    // GetSubstitution (ES2024 22.1.3.18.1)
    // With full support for $n, $nn, $<name>, $&, $`, $', $$
    // =====================================================================
    private static string GetSubstitution(string matched, string str, int position,
        IReadOnlyList<JsValue> captures, JsValue namedCaptures, string replacement)
    {
        var result = new StringBuilder(replacement.Length);
        var m = captures.Count; // number of capturing groups

        for (var i = 0; i < replacement.Length; i++)
        {
            var ch = replacement[i];
            if (ch != '$' || i + 1 >= replacement.Length)
            {
                result.Append(ch);
                continue;
            }

            var next = replacement[i + 1];
            switch (next)
            {
                case '$':
                    result.Append('$');
                    i++;
                    break;
                case '&':
                    result.Append(matched);
                    i++;
                    break;
                case '`':
                    result.Append(str.AsSpan(0, position));
                    i++;
                    break;
                case '\'':
                    var afterMatch = position + matched.Length;
                    if (afterMatch < str.Length)
                    {
                        result.Append(str.AsSpan(afterMatch));
                    }

                    i++;
                    break;
                case '<':
                {
                    // Named group reference: $<name>
                    if (namedCaptures == JsValue.Undefined || namedCaptures.IsNullOrUndefined)
                    {
                        result.Append("$<");
                        i++;
                        break;
                    }

                    var closeBracket = replacement.IndexOf('>', i + 2);
                    if (closeBracket == -1)
                    {
                        result.Append("$<");
                        i++;
                        break;
                    }

                    var groupName = replacement.Substring(i + 2, closeBracket - (i + 2));
                    JsOps.TryGetPropertyValue(namedCaptures, groupName, out var capture);
                    if (capture != JsValue.Undefined)
                    {
                        result.Append(JsOps.ToJsString(capture));
                    }

                    i = closeBracket;
                    break;
                }
                default:
                {
                    if (next is >= '0' and <= '9')
                    {
                        var digit1 = next - '0';
                        // Try two-digit reference first
                        if (i + 2 < replacement.Length && replacement[i + 2] is >= '0' and <= '9')
                        {
                            var digit2 = replacement[i + 2] - '0';
                            var twoDigit = (digit1 * 10) + digit2;
                            if (twoDigit >= 1 && twoDigit <= m)
                            {
                                var capVal = captures[twoDigit - 1];
                                if (capVal != JsValue.Undefined)
                                {
                                    result.Append(JsOps.ToJsString(capVal));
                                }

                                i += 2;
                                break;
                            }
                        }

                        // Single-digit reference (only valid for 1-9)
                        if (digit1 >= 1 && digit1 <= m)
                        {
                            var capVal = captures[digit1 - 1];
                            if (capVal != JsValue.Undefined)
                            {
                                result.Append(JsOps.ToJsString(capVal));
                            }

                            i++;
                        }
                        else
                        {
                            // No valid reference, output literal $
                            result.Append('$');
                        }
                    }
                    else
                    {
                        result.Append('$');
                    }

                    break;
                }
            }
        }

        return result.ToString();
    }

    // =====================================================================
    // SpeciesConstructor - get @@species or fall back to %RegExp%
    // =====================================================================
    private JsValue SpeciesConstructor(JsValue thisValue)
    {
        JsOps.TryGetPropertyValue(thisValue, "constructor", out var ctor);
        // Per spec step 4: If C is undefined, return defaultConstructor.
        if (ctor.IsUndefined)
        {
            return Realm.RegExpConstructor is not null
                ? JsValue.FromObjectUnsafe(Realm.RegExpConstructor)
                : JsValue.Undefined;
        }

        // Per spec step 5: If Type(C) is not Object, throw a TypeError exception.
        if (!ctor.IsObject)
        {
            throw ThrowTypeError("Species constructor is not an object", realm: Realm);
        }

        // Per spec step 6-8: Get species, check if undefined/null → return default.
        JsOps.TryGetPropertyValue(ctor, SymbolKeys.Species, out var species);
        if (species.IsUndefined || species.IsNull)
        {
            return Realm.RegExpConstructor is not null
                ? JsValue.FromObjectUnsafe(Realm.RegExpConstructor)
                : JsValue.Undefined;
        }

        // Per spec step 7: If IsConstructor(S) is true, return S.
        // Step 8: Throw a TypeError exception.
        if (!JsOps.IsConstructor(species))
        {
            throw ThrowTypeError("Species constructor is not a constructor", realm: Realm);
        }

        return species;
    }

    private JsValue ConstructSplitter(JsValue constructor, JsValue rx, string newFlags)
    {
        // If the constructor is the built-in RegExp constructor, optimize by creating directly
        if (constructor.TryGetObject<IJsCallable>(out var callable))
        {
            if (Realm.RegExpConstructor is not null &&
                ReferenceEquals(callable, Realm.RegExpConstructor))
            {
                // Preserve the observable IsRegExp / @@match side effect that the built-in
                // RegExp constructor performs before it reads the source from the receiver.
                JsOps.TryGetPropertyValue(rx, SymbolKeys.Match, out _);

                // Use the fast path: extract pattern from rx and construct with newFlags
                var resolved = ResolveRegExpInstance(rx);
                if (resolved is not null)
                {
                    var splitterObj = CreateRegExpLiteral(resolved.Pattern, newFlags, Realm);
                    return JsValue.FromJsObject(splitterObj);
                }
            }

            return ReflectHelper.Construct(callable, [rx, new JsValue(newFlags)], callable, Realm);
        }

        // Fallback: create a new RegExp from the resolved instance
        var resolved2 = ResolveRegExpInstance(rx);
        if (resolved2 is not null)
        {
            var splitterObj = CreateRegExpLiteral(resolved2.Pattern, newFlags, Realm);
            return JsValue.FromJsObject(splitterObj);
        }

        throw ThrowTypeError("Species constructor is not a constructor", realm: Realm);
    }

    private static double AdvanceStringIndexDouble(string input, double index, bool unicode)
    {
        // Per spec: AdvanceStringIndex returns index + 1 or index + 2 for surrogate pairs
        var intIndex = (int)Math.Min(index, input.Length);
        if (!unicode || intIndex + 1 >= input.Length)
        {
            return index + 1;
        }

        var first = input[intIndex];
        if (char.IsHighSurrogate(first) && intIndex + 1 < input.Length && char.IsLowSurrogate(input[intIndex + 1]))
        {
            return index + 2;
        }

        return index + 1;
    }

    private static int AdvanceStringIndex(string input, int index, bool unicode)
    {
        // Spec 22.2.7.2: AdvanceStringIndex always returns index + 1 (or +2 for surrogate pairs).
        // No clamping to string length — the caller handles out-of-bounds.
        if (!unicode || index + 1 >= input.Length)
        {
            return index + 1;
        }

        var first = input[index];
        if (char.IsHighSurrogate(first) && index + 1 < input.Length && char.IsLowSurrogate(input[index + 1]))
        {
            return index + 2;
        }

        return index + 1;
    }

    /// <summary>
    /// ES spec Set(O, P, V, Throw=true): invokes setters, throws on non-writable or no-setter.
    /// </summary>
    private void SetPropertyOrThrow(JsValue target, string name, JsValue value)
    {
        if (target.TryGetObject<IJsObjectLike>(out var obj))
        {
            ObjectHelper.SetPropertyOrThrow(obj, name, value, target, Realm);
        }
    }

    private void SetProperty(JsValue target, string name, JsValue value)
    {
        if (target.TryGetObject<JsObject>(out var obj))
        {
            // Check for non-writable property to throw TypeError (Set with Throw=true per spec)
            var descriptor = obj.GetOwnPropertyDescriptor(name);
            if (descriptor is not null && !descriptor.IsAccessorDescriptor && !descriptor.Writable)
            {
                throw ThrowTypeError($"Cannot assign to read only property '{name}'", realm: Realm);
            }

            obj.SetProperty(name, value);
        }
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
}
