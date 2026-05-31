using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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

    internal bool IsTypedArrayIterator => _accessor is TypedArrayBase;
    internal string AccessorTypeName => _accessor.GetType().Name;

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

        uint length;
        if (typedArray is not null)
        {
            length = (uint)Math.Max(typedArray.Length, 0);
        }
        else
        {
            // Get length as JsValue, avoiding boxing from ternary expression
            if (!_accessor.TryGetProperty("length", out var lenVal))
            {
                lenVal = JsValue.Zero;
            }

            var evalContext = _realm?.CreateContext();
            length = (uint)Math.Min(Math.Max(StandardLibrary.ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
            if (evalContext?.IsThrow == true)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }
        }

        if (_index < length)
        {
            var valueJs = ProjectIteratorValue(typedArray);
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

    internal bool TryNextValueFast(IJsCallable nextMethod, EvaluationContext context, out JsValue value, out bool done)
    {
        value = JsValue.Undefined;
        done = true;

        if (_kind != ArrayIteratorKind.Values ||
            nextMethod is not HostFunction hostFunction ||
            !hostFunction.HasNativeSourceDisplayName("next"))
        {
            return false;
        }

        if (_done)
        {
            return true;
        }

        var typedArray = _accessor as TypedArrayBase;
        if (typedArray is not null)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            typedArray.AssertInvariants(nameof(TryNextValueFast));
        }

        uint length;
        if (typedArray is not null)
        {
            length = (uint)Math.Max(typedArray.Length, 0);
        }
        else
        {
            if (!_accessor.TryGetProperty("length", out var lenVal))
            {
                lenVal = JsValue.Zero;
            }

            var evalContext = _realm?.CreateContext() ?? context;
            length = (uint)Math.Min(Math.Max(StandardLibrary.ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }
        }

        if (_index < length)
        {
            value = ProjectIteratorValue(typedArray);
            _index++;
            done = false;
            return true;
        }

        _done = true;
        return true;
    }

    private JsValue ProjectIteratorValue(TypedArrayBase? typedArray)
    {
        return _kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)_index),
            ArrayIteratorKind.Values => GetElementOrUndefinedJsValue(_accessor, typedArray, _index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(_index, _accessor, typedArray, _realm),
            _ => JsValue.Undefined
        };
    }

    private static JsValue GetElementOrUndefinedJsValue(IJsPropertyAccessor accessor, TypedArrayBase? typedArray, uint index)
    {
        if (typedArray is not null)
        {
            return typedArray.GetValueForIndex((int)index);
        }

        if (accessor.TryGetProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var val))
        {
            return val;
        }

        return JsValue.Undefined;
    }

    private static JsValue CreateIteratorEntryPair(uint index, IJsPropertyAccessor accessor, TypedArrayBase? typedArray,
        RealmState? realm)
    {
        var pair = new JsArray(realm);
        pair.Push((double)index);
        pair.Push(GetElementOrUndefinedJsValue(accessor, typedArray, index));
        return JsValue.FromJsArray(pair);
    }
}
