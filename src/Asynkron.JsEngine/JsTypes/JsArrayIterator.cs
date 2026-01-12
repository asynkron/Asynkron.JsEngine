#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Specifies the kind of array iterator to create.
/// Using an enum avoids closure allocations compared to passing Func delegates.
/// </summary>
internal enum ArrayIteratorKind
{
    /// <summary>Iterator yields [index, value] pairs.</summary>
    Entries,
    /// <summary>Iterator yields index values only.</summary>
    Keys,
    /// <summary>Iterator yields element values only.</summary>
    Values
}

internal sealed class JsArrayIterator : JsIteratorBase
{
    private readonly IJsPropertyAccessor _accessor;
    private readonly ArrayIteratorKind _kind;
    private uint _index;

    internal JsArrayIterator(IJsPropertyAccessor accessor, ArrayIteratorKind kind, RealmState? realm, JsObject? prototype)
        : base(realm, prototype)
    {
        _accessor = accessor;
        _kind = kind;
    }

    internal JsValue Next()
    {
        if (_done)
        {
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        // Check for detached typed arrays
        var typedArray = _accessor as TypedArrayBase;
        if (typedArray is not null)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            typedArray.AssertInvariants(nameof(Next));
        }

        // Get length as JsValue, avoiding boxing from ternary expression
        if (!_accessor.TryGetProperty("length", out var lenVal))
        {
            lenVal = JsValue.Zero;
        }

        var evalContext = _realm?.CreateContext();
        var length = (uint)Math.Min(Math.Max(StandardLibrary.ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (_index < length)
        {
            var valueJs = ProjectIteratorValue();
            _index++;
            if (typedArray?.Buffer.Resizable == true)
            {
                var result = new IteratorResultObject(valueJs, false);
                result.Capture();
                return result.AsJsValue;
            }

            return IteratorResultObjectPool.Rent(valueJs, false).AsJsValue;
        }

        _done = true;
        return IteratorResultObject.DoneUndefined.AsJsValue;
    }

    private JsValue ProjectIteratorValue()
    {
        return _kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)_index),
            ArrayIteratorKind.Values => GetElementOrUndefinedJsValue(_accessor, _index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(_index, _accessor, _realm),
            _ => JsValue.Undefined
        };
    }

    private static JsValue GetElementOrUndefinedJsValue(IJsPropertyAccessor accessor, uint index)
    {
        if (accessor.TryGetProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var val))
        {
            return val;
        }

        return JsValue.Undefined;
    }

    private static JsValue CreateIteratorEntryPair(uint index, IJsPropertyAccessor accessor, RealmState? realm)
    {
        var pair = new JsArray(realm);
        pair.Push((double)index);
        pair.Push(GetElementOrUndefinedJsValue(accessor, index));
        return JsValue.FromJsArray(pair);
    }
}
