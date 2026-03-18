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

        // Per spec: ToString(string) where string defaults to undefined => "undefined"
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : "undefined";
        return new JsValue(resolved.Test(input));
    }

    [JsHostMethod("exec", Length = 1d)]
    public JsValue Exec(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);

        // Per spec: ToString(string) where string defaults to undefined => "undefined"
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

            // Per spec B.2.5.1, we only need to check that the object has [[RegExpMatcher]] internal slot
            // and is not RegExp.prototype itself. These checks are done above (lines 69-77).
            // The constructor check is not required by the spec.

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

        // [Symbol.split] is registered via code generation from [JsSymbolMethod] attribute
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
        // ES2024 22.2.5.8 RegExp.prototype[@@matchAll](string)

        // Step 2: If Type(R) is not Object, throw TypeError.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        // Step 3: Let S be ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : "undefined";

        // Step 4: Let C be SpeciesConstructor(R, %RegExp%).
        var constructor = GetSpeciesConstructor(thisValue);

        // Step 5: Let flags be ToString(Get(R, "flags")).
        var context = Realm?.CreateContext();
        JsOps.TryGetPropertyValue(thisValue, "flags", out var flagsRaw, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var flags = JsOps.ToJsString(flagsRaw) ?? string.Empty;

        // Step 6: Let matcher be Construct(C, [R, flags]).
        JsValue matcher;
        if (constructor.TryGetCallable(out var ctor) && JsOps.IsConstructor(constructor))
        {
            matcher = ReflectHelper.Construct(ctor, [thisValue, new JsValue(flags)], ctor, Realm);
        }
        else
        {
            throw ThrowTypeError("Species constructor is not a constructor", realm: Realm);
        }

        // Step 7: Let lastIndex be ToLength(Get(R, "lastIndex")).
        JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var lastIndexRaw, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var lastIndex = StandardLibrary.ToLengthOrZero(lastIndexRaw);

        // Step 8: Perform Set(matcher, "lastIndex", lastIndex, true).
        SetPropertyStrict(matcher, "lastIndex", new JsValue((double)lastIndex));

        // Step 9-10: Check flags for global and unicode.
        var global = flags.Contains('g', StringComparison.Ordinal);
        var fullUnicode = flags.Contains('u', StringComparison.Ordinal) ||
                          flags.Contains('v', StringComparison.Ordinal);

        // Step 11: Return CreateRegExpStringIterator(matcher, S, global, fullUnicode).
        return CreateRegExpStringIterator(matcher, input, global, fullUnicode);
    }

    /// <summary>
    /// Abstract operation SpeciesConstructor(O, defaultConstructor).
    /// Returns the species constructor or falls back to %RegExp%.
    /// </summary>
    private JsValue GetSpeciesConstructor(JsValue obj)
    {
        var defaultConstructor = Realm.RegExpConstructor is not null
            ? JsValue.FromObjectUnsafe(Realm.RegExpConstructor)
            : throw new InvalidOperationException("RegExp constructor not initialized.");

        var context = Realm?.CreateContext();
        if (!JsOps.TryGetPropertyValue(obj, "constructor", out var constructorValue, context))
        {
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return defaultConstructor;
        }

        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (constructorValue == JsValue.Undefined)
        {
            return defaultConstructor;
        }

        if (!constructorValue.IsObject)
        {
            throw ThrowTypeError("Constructor must be an object", realm: Realm);
        }

        if (!JsOps.TryGetPropertyValue(constructorValue, SymbolKeys.Species, out var speciesValue, context))
        {
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return defaultConstructor;
        }

        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (speciesValue.IsNullOrUndefined)
        {
            return defaultConstructor;
        }

        if (!JsOps.IsConstructor(speciesValue))
        {
            throw ThrowTypeError("Species constructor must be a constructor", realm: Realm);
        }

        return speciesValue;
    }

    /// <summary>
    /// Creates a RegExpStringIterator per the ES spec.
    /// </summary>
    private JsValue CreateRegExpStringIterator(JsValue matcher, string input, bool global, bool fullUnicode)
    {
        var iterator = new JsRegExpStringIterator(matcher, input, global, fullUnicode, Realm,
            Realm?.IteratorPrototype);

        // Set up the "next" method.
        var nextFn = new HostFunction((_, _) => iterator.Next(), isConstructor: false);
        nextFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = "next", Writable = false, Enumerable = false, Configurable = true
        });
        nextFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = 0d, Writable = false, Enumerable = false, Configurable = true
        });
        iterator.SetProperty("next", JsValue.FromObjectUnsafe(nextFn));

        // Set Symbol.toStringTag.
        iterator.DefineProperty(SymbolKeys.ToStringTag,
            new PropertyDescriptor
            {
                Value = "RegExp String Iterator", Writable = false, Enumerable = false, Configurable = true
            });

        return iterator.AsJsValue;
    }

    [JsSymbolMethod("replace", Length = 2d)]
    private JsValue ReplaceSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var resolved = RequireRegExp(thisValue);
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : string.Empty;
        var replacement = args.GetArgument(1);
        var regex = resolved.GetRegex();
        var isFuncReplacer = replacement.TryGetObject<IJsCallable>(out var replacer);
        var replaceText = isFuncReplacer ? null : JsOps.ToJsString(replacement);

        var resultBuilder = new StringBuilder();
        var resultLastIndex = 0;

        IEnumerable<Match> matches;
        if (resolved.Global)
        {
            matches = regex.Matches(input);
        }
        else
        {
            var singleMatch = regex.Match(input);
            matches = singleMatch.Success ? [singleMatch] : [];
        }

        foreach (var match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (match.Index > resultLastIndex)
            {
                resultBuilder.Append(input.AsSpan(resultLastIndex, match.Index - resultLastIndex));
            }

            if (isFuncReplacer)
            {
                var replaceArgs = BuildReplaceArguments(match, regex, input);
                var replacementValue = replacer!.Invoke(replaceArgs, JsValue.Undefined);
                resultBuilder.Append(replacementValue.ToJsString());
            }
            else
            {
                var substituted = GetRegExpSubstitution(replaceText!, input, match, regex);
                resultBuilder.Append(substituted);
            }

            resultLastIndex = match.Index + match.Length;

            if (match.Length == 0)
            {
                // Avoid infinite loop on zero-length matches
                if (resultLastIndex < input.Length)
                {
                    resultBuilder.Append(input[resultLastIndex]);
                    resultLastIndex++;
                }
                else
                {
                    break;
                }
            }

            if (!resolved.Global)
            {
                break;
            }
        }

        if (resultLastIndex < input.Length)
        {
            resultBuilder.Append(input.AsSpan(resultLastIndex));
        }

        return new JsValue(resultBuilder.ToString());
    }

    /// <summary>
    /// ECMAScript GetSubstitution for RegExp replace with string replacement.
    /// Handles $$ $&amp; $` $' $n $nn $&lt;name&gt; patterns per spec.
    /// </summary>
    private static string GetRegExpSubstitution(string replacement, string str, Match match, Regex regex)
    {
        var result = new StringBuilder(replacement.Length);

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
                    result.Append(match.Value);
                    i++;
                    break;
                case '`':
                    result.Append(str.AsSpan(0, match.Index));
                    i++;
                    break;
                case '\'':
                    var afterMatch = match.Index + match.Length;
                    if (afterMatch < str.Length)
                    {
                        result.Append(str.AsSpan(afterMatch));
                    }
                    i++;
                    break;
                case '<':
                {
                    var closeAngle = replacement.IndexOf('>', i + 2);
                    if (closeAngle < 0)
                    {
                        result.Append("$<");
                        i++;
                        break;
                    }

                    var groupName = replacement.Substring(i + 2, closeAngle - (i + 2));
                    var group = regex.GroupNumberFromName(groupName);
                    if (group >= 0 && match.Groups[group].Success)
                    {
                        result.Append(match.Groups[group].Value);
                    }

                    i = closeAngle;
                    break;
                }
                default:
                    if (next is >= '0' and <= '9')
                    {
                        var captureCount = match.Groups.Count - 1;
                        var digit1 = next - '0';

                        // Try two-digit reference first (e.g., $01, $12, $99)
                        if (i + 2 < replacement.Length && replacement[i + 2] is >= '0' and <= '9')
                        {
                            var digit2 = replacement[i + 2] - '0';
                            var twoDigit = (digit1 * 10) + digit2;
                            if (twoDigit >= 1 && twoDigit <= captureCount)
                            {
                                var captureGroup = match.Groups[twoDigit];
                                if (captureGroup.Success)
                                {
                                    result.Append(captureGroup.Value);
                                }
                                i += 2;
                                break;
                            }
                        }

                        // Single digit reference (e.g., $1 through $9)
                        if (digit1 >= 1 && digit1 <= captureCount)
                        {
                            var captureGroup = match.Groups[digit1];
                            if (captureGroup.Success)
                            {
                                result.Append(captureGroup.Value);
                            }
                            i++;
                        }
                        else
                        {
                            // No such capture, output literal $n
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

        return result.ToString();
    }

    [JsSymbolMethod("search", Length = 1d)]
    private JsValue SearchSymbol(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // ES2024 22.2.5.11 RegExp.prototype[@@search](string)
        // Step 1-2: Require this to be an object.
        if (!thisValue.IsObject)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        // Step 3: Let S be ToString(string).
        var input = args.Count > 0 ? JsOps.ToJsString(args[0]) ?? string.Empty : "undefined";

        var context = Realm?.CreateContext();

        // Step 4: Let previousLastIndex be Get(rx, "lastIndex").
        if (!JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var previousLastIndex, context))
        {
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            previousLastIndex = JsValue.Undefined;
        }

        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Step 5: If SameValue(previousLastIndex, 0) is false, then
        //   a. Perform Set(rx, "lastIndex", 0, true).
        if (!JsOps.SameValue(previousLastIndex, new JsValue(0d)))
        {
            SetPropertyStrict(thisValue, "lastIndex", new JsValue(0d));
        }

        // Step 6: Let result be RegExpExec(rx, S).
        var result = RegExpExecAbstract(thisValue, input);

        // Step 7: Let currentLastIndex be Get(rx, "lastIndex").
        JsOps.TryGetPropertyValue(thisValue, "lastIndex", out var currentLastIndex, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Step 8: If SameValue(currentLastIndex, previousLastIndex) is false, then
        //   a. Perform Set(rx, "lastIndex", previousLastIndex, true).
        if (!JsOps.SameValue(currentLastIndex, previousLastIndex))
        {
            SetPropertyStrict(thisValue, "lastIndex", previousLastIndex);
        }

        // Step 9: If result is null, return -1.
        if (result == JsValue.Null)
        {
            return new JsValue(-1d);
        }

        // Step 10: Return Get(result, "index").
        if (JsOps.TryGetPropertyValue(result, "index", out var indexValue))
        {
            return indexValue;
        }

        return new JsValue(-1d);
    }

    /// <summary>
    /// Abstract operation RegExpExec(R, S).
    /// Calls the 'exec' method on the object (which may be overridden), per spec.
    /// </summary>
    private JsValue RegExpExecAbstract(JsValue rx, string input)
    {
        var context = Realm?.CreateContext();
        if (JsOps.TryGetPropertyValue(rx, "exec", out var execProp, context) &&
            execProp.TryGetObject<IJsCallable>(out var execCallable))
        {
            var result = execCallable.Invoke(new SingleValueArgs(new JsValue(input)), rx);
            if (result == JsValue.Null || result.IsObject)
            {
                return result;
            }

            throw ThrowTypeError("exec must return null or an object", realm: Realm);
        }

        // Fallback to built-in exec.
        var resolved = ResolveRegExpInstance(rx);
        if (resolved is null)
        {
            throw ThrowTypeError("RegExp method called on incompatible receiver", realm: Realm);
        }

        var execResult = resolved.Exec(input);
        return execResult is null ? JsValue.Null : JsValue.FromObjectUnsafe(execResult);
    }

    /// <summary>
    /// Performs Set(O, P, V, true) - the strict property set that throws on non-writable.
    /// </summary>
    private void SetPropertyStrict(JsValue target, string name, JsValue value)
    {
        if (!target.TryGetObject<JsObject>(out var obj))
        {
            throw ThrowTypeError("Cannot set property on non-object", realm: Realm);
        }

        var descriptor = obj.GetOwnPropertyDescriptor(name);
        if (descriptor is not null)
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Set is not null)
                {
                    descriptor.Set.Invoke(new SingleValueArgs(value), target);
                    return;
                }

                throw ThrowTypeError($"Cannot set property '{name}' which has only a getter", realm: Realm);
            }

            if (!descriptor.Writable)
            {
                throw ThrowTypeError($"Cannot assign to read only property '{name}'", realm: Realm);
            }

            obj[name] = value;
            descriptor.JsValue = value;
            return;
        }

        obj.SetProperty(name, value);
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
    private static int AdvanceStringIndex(string _, int index)
    {
        // For simplicity, advance by 1. A full implementation would handle Unicode surrogate pairs.
        return index + 1;
    }
}
