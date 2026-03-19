#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A RegExpStringIterator per the ES2024 specification.
///     Created by RegExp.prototype[@@matchAll].
/// </summary>
internal sealed class JsRegExpStringIterator : JsIteratorBase
{
    private readonly JsValue _regexp;
    private readonly string _string;
    private readonly bool _global;
    private readonly bool _unicode;

    internal JsRegExpStringIterator(
        JsValue regexp,
        string input,
        bool global,
        bool unicode,
        RealmState? realm,
        JsObject? prototype)
        : base(realm, prototype)
    {
        _regexp = regexp;
        _string = input;
        _global = global;
        _unicode = unicode;
    }

    /// <summary>
    ///     Implements %RegExpStringIteratorPrototype%.next().
    /// </summary>
    internal JsValue Next()
    {
        if (_done)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        // Call exec on the regexp.
        var match = RegExpExec(_regexp, _string);

        if (match == JsValue.Null)
        {
            _done = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        if (_global)
        {
            // Get the matched string.
            JsOps.TryGetPropertyValue(match, "0", out var matchStr);
            var matchString = JsOps.ToJsString(matchStr) ?? string.Empty;

            if (matchString.Length == 0)
            {
                // Advance lastIndex to avoid infinite loop on zero-length match.
                JsOps.TryGetPropertyValue(_regexp, "lastIndex", out var thisIndex);
                var nextIndex = AdvanceStringIndex(_string, (int)JsOps.ToNumber(thisIndex), _unicode);
                SetLastIndexOnObject(_regexp, nextIndex);
            }

            return IteratorResultObjectPool.Rent(match, false).AsJsValue;
        }

        // Non-global: return single match, then done.
        _done = true;
        return IteratorResultObjectPool.Rent(match, false).AsJsValue;
    }

    private static JsValue RegExpExec(JsValue rx, string input)
    {
        if (JsOps.TryGetPropertyValue(rx, "exec", out var execProp) &&
            execProp.TryGetObject<IJsCallable>(out var execCallable))
        {
            var result = execCallable.Invoke(new SingleValueArgs(new JsValue(input)), rx);
            if (result == JsValue.Null || result.IsObject)
            {
                return result;
            }

            throw StandardLibrary.ThrowTypeError("exec must return null or an object");
        }

        // Fallback to built-in exec.
        var resolved = RegExpHelper.ResolveRegExpInstance(rx);
        if (resolved is null)
        {
            throw StandardLibrary.ThrowTypeError("RegExp method called on incompatible receiver");
        }

        var execResult = resolved.Exec(input);
        return execResult is null ? JsValue.Null : JsValue.FromObjectUnsafe(execResult);
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

    private static void SetLastIndexOnObject(JsValue target, int value)
    {
        if (target.TryGetObject<JsObject>(out var obj))
        {
            var descriptor = obj.GetOwnPropertyDescriptor("lastIndex");
            if (descriptor is not null)
            {
                if (descriptor.IsAccessorDescriptor)
                {
                    descriptor.Set?.Invoke(new SingleValueArgs(new JsValue((double)value)), target);
                    return;
                }

                if (!descriptor.Writable)
                {
                    throw StandardLibrary.ThrowTypeError("Cannot assign to read only property 'lastIndex'");
                }

                obj["lastIndex"] = (double)value;
                descriptor.JsValue = new JsValue((double)value);
                return;
            }

            obj.SetProperty("lastIndex", (double)value);
        }
    }
}
