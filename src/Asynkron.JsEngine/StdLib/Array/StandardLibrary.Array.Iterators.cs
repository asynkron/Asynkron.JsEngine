#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.StdLib;

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

    /// <summary>
    /// Gets or creates the ArrayIteratorPrototype for the realm.
    /// </summary>
    private static JsObject? GetOrCreateArrayIteratorPrototype(RealmState? realm)
    {
        if (realm is null)
        {
            return null;
        }

        return realm.ArrayIteratorPrototype ??= (JsObject)ArrayIteratorPrototype.CreatePrototype(realm);
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, ArrayIteratorKind kind,
        RealmState? realm)
    {
        var prototype = GetOrCreateArrayIteratorPrototype(realm);
        var iterator = new JsArrayIterator(accessor, kind, realm, prototype);
        return iterator;
    }

    internal static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, Func<uint, JsValue> projector,
        RealmState? realm)
    {
        // Legacy path for custom projectors - we still need the closure-based approach for these
        var prototype = GetOrCreateArrayIteratorPrototype(realm) ?? realm?.ObjectPrototype;
        var iterator = new JsObject(prototype);
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
            if (evalContext?.IsThrow == true)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            if (index < length)
            {
                // Projector now returns JsValue directly - no boxing
                var valueJs = projector(index);
                index++;
                return IteratorResultObjectPool.Rent(valueJs, false).AsJsValue;
            }

            exhausted = true;
            return IteratorResultObject.DoneUndefined.AsJsValue;
        }

        JsValue ReturnIterator(JsValue _, IReadOnlyList<JsValue> __, RealmState? ___)
        {
            return new JsValue(iterator);
        }
    }
}
