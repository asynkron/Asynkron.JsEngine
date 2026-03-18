#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("TypedArray", PrototypeType = typeof(TypedArrayPrototype), Length = 0d, DisplayName = "TypedArray")]
public sealed partial class TypedArrayConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.TypedArrayPrototype ??= Prototype as JsObject;
        Realm.TypedArrayConstructor ??= constructor;

        constructor.DisallowConstruct = true;
        constructor.ConstructErrorMessage = "TypedArray is not a constructor";
        constructor.SetInvokeWithContext((_, _, _, newTarget) =>
        {
            if (!newTarget.IsUndefined)
            {

                // ReSharper disable once DuplicatedStatements
                throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
            }

            throw ThrowTypeError("TypedArray is not a constructor", realm: Realm);
        });

    }

    /// <summary>
    /// %TypedArray%.from ( source [ , mapfn [ , thisArg ] ] )
    /// </summary>
    [JsHostFunction("from", Target = JsHostFunctionTarget.Constructor, TargetName = "TypedArrayConstructor", Length = 1d)]
    private JsValue TypedArrayFrom(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // 1. Let C be the this value.
        // 2. If IsConstructor(C) is false, throw a TypeError exception.
        // Note: %TypedArray% has IsConstructor=true even though DisallowConstruct=true,
        // because per spec %TypedArray% has a [[Construct]] internal method.
        if (!IsTypedArrayConstructorLike(thisValue, out var ctor))
        {
            throw ThrowTypeError("%TypedArray%.from requires a constructor as 'this'", realm: Realm);
        }

        // 3. If mapfn is undefined, let mapping be false.
        // 4. Else, if IsCallable(mapfn) is false, throw a TypeError.
        IJsCallable? mapFn = null;
        var mapThis = JsValue.Undefined;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            if (!args[1].TryGetObject<IJsCallable>(out var callableMap))
            {
                throw ThrowTypeError("mapfn is not callable", realm: Realm);
            }
            mapFn = callableMap;
            mapThis = args.GetArgument(2);
        }

        if (args.Count == 0)
        {
            return CreateAndPopulateTypedArray(ctor, [], mapFn, mapThis);
        }

        var source = args[0];

        // 6. Let usingIterator be ? GetMethod(items, @@iterator).
        // The GetMethod call uses [[Get]] which may invoke getters that throw.
        // We must let those exceptions propagate.
        var iteratorKey = SymbolKeys.Iterator;
        if (source.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            // TryGetProperty will invoke getters; if the getter throws, it propagates.
            if (accessor.TryGetProperty(iteratorKey, out var methodVal) &&
                !methodVal.IsUndefined && !methodVal.IsNull)
            {
                if (!methodVal.TryGetObject<IJsCallable>(out var callableIterator))
                {
                    throw ThrowTypeError("Iterator method is not callable", realm: Realm);
                }

                var iteratorObj = callableIterator.Invoke([], source);
                if (!iteratorObj.TryGetObject<IJsPropertyAccessor>(out var iteratorAccessor))
                {
                    throw ThrowTypeError("Iterator method did not return an object", realm: Realm);
                }

                if (!iteratorAccessor.TryGetProperty("next", out var nextVal) ||
                    !nextVal.TryGetObject<IJsCallable>(out var nextCallable))
                {
                    throw ThrowTypeError("Iterator result does not expose next", realm: Realm);
                }

                var collected = new List<JsValue>();
                while (true)
                {
                    var nextResult = nextCallable.Invoke([], iteratorObj);
                    if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var nextResultAccessor))
                    {
                        throw ThrowTypeError("Iterator result is not an object", realm: Realm);
                    }

                    var done = nextResultAccessor.TryGetProperty("done", out var doneVal) &&
                               JsOps.ToBoolean(doneVal);
                    if (done)
                    {
                        return CreateAndPopulateTypedArray(ctor, collected, mapFn, mapThis);
                    }

                    var value = nextResultAccessor.TryGetProperty("value", out var valueVal)
                        ? valueVal
                        : JsValue.Undefined;
                    collected.Add(value);
                }
            }

            // 7. Let arrayLike be source (no iterator, use array-like path).
            // Let len be ? ToLength(? Get(arrayLike, "length")).
            if (accessor.TryGetProperty("length", out var lengthVal))
            {
                var lenNumber = JsOps.ToNumber(lengthVal);
                var length = double.IsNaN(lenNumber) || lenNumber < 0
                    ? 0
                    : (int)Math.Min(lenNumber, int.MaxValue);

                var collected = new List<JsValue>(length);
                for (var i = 0; i < length; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    var hasElement = accessor.TryGetProperty(key, out var element);
                    collected.Add(hasElement ? element : JsValue.Undefined);
                }

                return CreateAndPopulateTypedArray(ctor, collected, mapFn, mapThis);
            }
        }

        return CreateAndPopulateTypedArray(ctor, [], mapFn, mapThis);
    }

    /// <summary>
    /// Checks if a value is a constructor suitable for TypedArray.from/of.
    /// Per spec, %TypedArray% has [[Construct]] even though it always throws,
    /// so IsConstructor(%TypedArray%) should return true.
    /// </summary>
    private static bool IsTypedArrayConstructorLike(JsValue value, out IJsCallable ctor)
    {
        if (!value.TryGetObject<IJsCallable>(out ctor!))
        {
            return false;
        }

        // For HostFunction: accept if IsConstructor is true (even with DisallowConstruct)
        if (ctor is HostFunction hf)
        {
            return hf.IsConstructor;
        }

        // For other callables, use the standard IsConstructor check
        return JsOps.IsConstructor(value);
    }

    private JsValue CreateAndPopulateTypedArray(IJsCallable ctor, IList<JsValue> values, IJsCallable? mapFn, JsValue mapThis)
    {
        var length = values.Count;
        var taObj = ctor.Invoke(new SingleValueArgs(JsValue.FromDouble(length)), JsValue.FromObjectUnsafe(ctor));
        if (!taObj.TryGetObject<TypedArrayBase>(out var typed))
        {
            throw ThrowTypeError("%TypedArray%.from: constructor did not return a typed array", realm: Realm);
        }

        for (var i = 0; i < length; i++)
        {
            var value = values[i];
            if (mapFn != null)
            {
                value = mapFn.Invoke([value, JsValue.FromDouble(i)], mapThis);
            }

            // Per spec: Perform ? Set(targetObj, Pk, mappedValue, true).
            // If the buffer was detached or resized by the mapper, ignore the index
            // (Set on a detached/out-of-bounds typed array is a no-op for numeric indices).
            if (typed.IsDetachedOrOutOfBounds() || i >= typed.Length)
            {
                continue;
            }

            typed.SetValue(i, value);
        }

        return taObj;
    }

    /// <summary>
    /// %TypedArray%.of ( ...items )
    /// </summary>
    [JsHostFunction("of", Target = JsHostFunctionTarget.Constructor, TargetName = "TypedArrayConstructor", Length = 0d)]
    private JsValue TypedArrayOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // 1. Let len be the actual number of arguments passed to this function.
        var length = args.Count;

        // 2. Let items be the List of arguments passed to this function.
        // 3. Let C be the this value.
        // 4. If IsConstructor(C) is false, throw a TypeError exception.
        if (!IsTypedArrayConstructorLike(thisValue, out var ctor))
        {
            throw ThrowTypeError("%TypedArray%.of requires a constructor as 'this'", realm: Realm);
        }

        // 5. Let newObj be ? TypedArrayCreate(C, « 𝔽(len) »).
        var taObj = ctor.Invoke(new SingleValueArgs(JsValue.FromDouble(length)), JsValue.FromObjectUnsafe(ctor));
        if (!taObj.TryGetObject<TypedArrayBase>(out var typed))
        {
            throw ThrowTypeError("%TypedArray%.of: constructor did not return a typed array", realm: Realm);
        }

        // 6. Let k be 0.
        // 7. Repeat, while k < len,
        for (var i = 0; i < length; i++)
        {
            typed.SetValue(i, args[i]);
        }

        // 8. Return newObj.
        return taObj;
    }
}
