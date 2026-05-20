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

        EnsureConstructedTypedArrayLength("%TypedArray%.from", typed, length);

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

    private void EnsureConstructedTypedArrayLength(string operation, TypedArrayBase typed, int requiredLength)
    {
        if (typed.IsDetachedOrOutOfBounds() || typed.Length < requiredLength)
        {
            throw ThrowTypeError($"{operation}: constructor returned a typed array with insufficient length", realm: Realm);
        }
    }
}
