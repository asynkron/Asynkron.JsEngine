#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static object CreateArrayIterator(JsValue thisValue, string methodName, RealmState? realm,
        Func<IJsPropertyAccessor, JsValue, Func<uint, JsValue>> projectorFactory)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var projector = projectorFactory(accessor, thisValue);
        return CreateArrayIteratorObject(accessor, projector, realm);
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

            var length = (uint)Math.Min(Math.Max(ToLengthOrZero(lenVal), 0), uint.MaxValue);
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
}
