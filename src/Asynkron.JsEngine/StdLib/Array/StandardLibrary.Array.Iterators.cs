using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static object CreateArrayIterator(object? thisValue, string methodName, RealmState? realm,
        Func<IJsPropertyAccessor, object?, Func<uint, object?>> projectorFactory)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var projector = projectorFactory(accessor, thisValue);
        return CreateArrayIteratorObject(accessor, projector, realm);
    }

    private static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, Func<uint, object?> projector,
        RealmState? realm)
    {
        var iterator = new JsObject(realm?.ObjectPrototype);
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        uint index = 0;
        var exhausted = false;
        var typedAccessor = accessor as TypedArrayBase;

        iterator.SetHostedProperty("next", Next, realm);
        iterator.SetHostedProperty(iteratorKey, ReturnIterator, realm);
        return iterator;

        object? Next(object? _, IReadOnlyList<object?> __, RealmState? ___)
        {
            realm?.Logger?.LogInformation("ArrayIterator.next index={Index}", index);
            if (exhausted)
            {
                var doneResult = new JsObject(realm?.ObjectPrototype);
                doneResult.SetProperty("value", Symbol.Undefined);
                doneResult.SetProperty("done", true);
                return doneResult;
            }

            if (typedAccessor is not null && typedAccessor.IsDetachedOrOutOfBounds())
            {
                throw typedAccessor.CreateOutOfBoundsTypeError();
            }

            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (uint)Math.Min(Math.Max(ToLengthOrZero(lengthValue), 0), uint.MaxValue);
            var result = new JsObject(realm?.ObjectPrototype);
            if (index < length)
            {
                result.SetProperty("value", projector(index));
                result.SetProperty("done", false);
                index++;
            }
            else
            {
                result.SetProperty("value", Symbol.Undefined);
                result.SetProperty("done", true);
                exhausted = true;
            }

            return result;
        }

        object? ReturnIterator(object? _, IReadOnlyList<object?> __, RealmState? ___)
        {
            return iterator;
        }
    }
}
