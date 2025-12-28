#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.StdLib;

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

public static partial class StandardLibrary
{
    /// <summary>
    /// Creates an array iterator without closure allocations.
    /// </summary>
    internal static object CreateArrayIterator(JsValue thisValue, string methodName, RealmState? realm,
        ArrayIteratorKind kind)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        return CreateArrayIteratorObject(accessor, kind, realm);
    }

    /// <summary>
    /// Legacy overload for backwards compatibility - still allocates closures.
    /// Prefer the ArrayIteratorKind overload for new code.
    /// </summary>
    internal static object CreateArrayIterator(JsValue thisValue, string methodName, RealmState? realm,
        Func<IJsPropertyAccessor, JsValue, Func<uint, JsValue>> projectorFactory)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var projector = projectorFactory(accessor, thisValue);
        return CreateArrayIteratorObject(accessor, projector, realm);
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, ArrayIteratorKind kind,
        RealmState? realm)
    {
        var iterator = new JsObject(realm?.ObjectPrototype);
        var iteratorKey = SymbolKeys.Iterator;

        uint index = 0;
        var exhausted = false;
        var typedAccessor = accessor as TypedArrayBase;

        iterator.SetHostedProperty("next", Next, realm);
        iterator.SetHostedProperty(iteratorKey, ReturnIterator, realm);
        return iterator;

        JsValue Next(JsValue _, IReadOnlyList<JsValue> __, RealmState? ___)
        {
            realm?.Logger?.LogInformation("ArrayIterator.next index={Index}", index);
            if (exhausted)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }

            if (typedAccessor?.IsDetachedOrOutOfBounds() == true)
            {
                throw typedAccessor.CreateOutOfBoundsTypeError();
            }

            // Get length as JsValue, avoiding boxing from ternary expression
            if (!accessor.TryGetProperty("length", out var lenVal))
            {
                lenVal = JsValue.Zero;
            }

            var evalContext = realm?.CreateContext();
            var length = (uint)Math.Min(Math.Max(ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
            if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
            if (index < length)
            {
                // Project value based on kind - no closure allocation
                var valueJs = ProjectIteratorValue(kind, index, accessor, realm);
                index++;
                return new IteratorResultObject(valueJs, false).AsJsValue;
            }

            exhausted = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        JsValue ReturnIterator(JsValue _, IReadOnlyList<JsValue> __, RealmState? ___)
        {
            return new JsValue(iterator);
        }
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, Func<uint, JsValue> projector,
        RealmState? realm)
    {
        var iterator = new JsObject(realm?.ObjectPrototype);
        var iteratorKey = SymbolKeys.Iterator;

        uint index = 0;
        var exhausted = false;
        var typedAccessor = accessor as TypedArrayBase;

        iterator.SetHostedProperty("next", Next, realm);
        iterator.SetHostedProperty(iteratorKey, ReturnIterator, realm);
        return iterator;

        JsValue Next(JsValue _, IReadOnlyList<JsValue> __, RealmState? ___)
        {
            realm?.Logger?.LogInformation("ArrayIterator.next index={Index}", index);
            if (exhausted)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }

            if (typedAccessor?.IsDetachedOrOutOfBounds() == true)
            {
                throw typedAccessor.CreateOutOfBoundsTypeError();
            }

            // Get length as JsValue, avoiding boxing from ternary expression
            if (!accessor.TryGetProperty("length", out var lenVal))
            {
                lenVal = JsValue.Zero;
            }

            var evalContext = realm?.CreateContext();
            var length = (uint)Math.Min(Math.Max(ToLengthOrZero(lenVal, evalContext), 0), uint.MaxValue);
            if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
            if (index < length)
            {
                // Projector now returns JsValue directly - no boxing
                var valueJs = projector(index);
                index++;
                return new IteratorResultObject(valueJs, false).AsJsValue;
            }

            exhausted = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        JsValue ReturnIterator(JsValue _, IReadOnlyList<JsValue> __, RealmState? ___)
        {
            return new JsValue(iterator);
        }
    }

    /// <summary>
    /// Projects the iterator value based on kind. Inlined to avoid delegate allocation.
    /// </summary>
    private static JsValue ProjectIteratorValue(ArrayIteratorKind kind, uint index, IJsPropertyAccessor accessor,
        RealmState? realm)
    {
        return kind switch
        {
            ArrayIteratorKind.Keys => new JsValue((double)index),
            ArrayIteratorKind.Values => GetElementOrUndefinedJsValue(accessor, index),
            ArrayIteratorKind.Entries => CreateIteratorEntryPair(index, accessor, realm),
            _ => JsValue.Undefined
        };
    }

    /// <summary>
    /// Creates an [index, value] pair for entries iterator.
    /// </summary>
    private static JsValue CreateIteratorEntryPair(uint index, IJsPropertyAccessor accessor, RealmState? realm)
    {
        var pair = new JsArray(realm);
        pair.Push((double)index);
        pair.Push(GetElementOrUndefinedJsValue(accessor, index));
        return JsValue.FromJsArray(pair);
    }
}
