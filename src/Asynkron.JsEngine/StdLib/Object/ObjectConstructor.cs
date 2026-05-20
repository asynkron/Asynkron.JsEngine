#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.BigIntHelper;
using static Asynkron.JsEngine.StdLib.ObjectHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;
using static Asynkron.JsEngine.StdLib.SymbolHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Object", PrototypeType = typeof(ObjectPrototype), Length = 1d, DisplayName = "Object")]
public sealed partial class ObjectConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Object constructor not initialized");

    /// <summary>
    /// Common argument extraction for keys/values/entries methods.
    /// Throws TypeError for null/undefined, returns null for non-object primitives.
    /// </summary>
    private static IJsPropertyAccessor? GetObjectForEnumeration(
        IReadOnlyList<JsValue> args,
        RealmState? realm,
        out RealmState realmState)
    {
        realmState = RequireRealm(realm);
        var arg = args.GetArgument(0);

        if (arg.IsNullOrUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: realmState);
        }

        if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        return TryGetObject(arg, realmState, out var coerced) ? coerced : null;
    }

    private static IEnumerable<string> EnumerateEnumerableOwnStringKeys(IJsPropertyAccessor obj)
    {
        foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: true))
        {
            var desc = obj.GetOwnPropertyDescriptor(key);
            if (desc is { Enumerable: true })
            {
                yield return key;
            }
        }
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, targetCtor);
            return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, constructing));
        }

        return JsValue.FromObjectUnsafe(ConstructCore(args, targetCtor, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ObjectPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            if (newTarget.TryGetObject<IJsCallable>(out var newTargetCallable))
            {
                return JsValue.FromObjectUnsafe(ConstructCore(args, newTargetCallable, null));
            }

            return JsValue.FromObjectUnsafe(ConstructCore(args, target, null));
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
        AttachPrototypeShortcut(constructor);
    }

    private object ConstructCore(IReadOnlyList<JsValue> args, IJsCallable newTarget, JsObject? existing)
    {
        var isSubclassConstruction = existing is not null
            ? !ReferenceEquals(existing.Prototype, Prototype)
            : !ReferenceEquals(newTarget, ConstructFallback);

        if (isSubclassConstruction)
        {
            return CreateBlank(newTarget, existing);
        }

        if (args.Count == 0 || args[0].IsUndefined || args[0].IsNull)
        {
            return CreateBlank(newTarget, existing);
        }

        var value = args[0];

        // Check if it's a TypedAstSymbol (stored in ObjectValue when Kind is Symbol)
        if (value is { IsSymbol: true, ObjectValue: JsSymbol typedSym })
        {
            return CreateSymbolWrapper(typedSym, realm: Realm);
        }

        if (value.TryGetBigInt(out var bigInt))
        {
            return CreateBigIntWrapper(bigInt, realm: Realm);
        }

        if (value.TryGetBoolean(out var boolValue))
        {
            return BooleanHelper.CreateBooleanWrapper(boolValue, realm: Realm);
        }

        if (value.TryGetString(out var strValue))
        {
            return StringHelper.CreateStringWrapper(strValue, realm: Realm);
        }

        if (value.TryGetDouble(out var numValue))
        {
            return NumberHelper.CreateNumberWrapper(numValue, realm: Realm);
        }

        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        return CreateBlank(newTarget, existing);
    }

    private JsObject CreateBlank(IJsCallable newTarget, JsObject? existing)
    {
        var targetCtor = _constructor ?? newTarget;
        var obj = existing ?? PrepareThisObject(JsValue.Undefined, false);
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        if (obj.Prototype is null)
        {
            obj.SetPrototype(proto);
        }

        obj.RealmState ??= Realm;
        return obj;
    }

    private void AttachPrototypeShortcut(HostFunction constructor)
    {
        if (Prototype.TryGetProperty("hasOwnProperty", out var hasOwn))
        {
            constructor.SetProperty("hasOwnProperty", hasOwn);
        }
    }

    private static IEnumerable<JsValue> EnumerateIteratorValues(
        JsValue source,
        RealmState realm,
        string methodName)
    {
        var iteratorValue = GetIteratorObject(source, realm, methodName);
        if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iteratorAccessor))
        {
            throw ThrowTypeError($"{methodName} iterator must be an object", realm: realm);
        }

        var iteratorReceiver = JsValue.FromObjectUnsafe(iteratorAccessor);
        if (!iteratorAccessor.TryGetProperty("next", iteratorReceiver, out var nextMethod) ||
            !nextMethod.TryGetObject<IJsCallable>(out var nextCallable))
        {
            throw ThrowTypeError($"{methodName} iterator must have a callable next method", realm: realm);
        }

        while (true)
        {
            var nextResult = nextCallable.Invoke([], iteratorReceiver);
            if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultAccessor))
            {
                throw ThrowTypeError($"{methodName} iterator result must be an object", realm: realm);
            }

            var resultReceiver = JsValue.FromObjectUnsafe(resultAccessor);
            var done = resultAccessor.TryGetProperty("done", resultReceiver, out var doneValue) &&
                       JsOps.ToBoolean(doneValue);
            if (done)
            {
                yield break;
            }

            if (resultAccessor.TryGetProperty("value", resultReceiver, out var value))
            {
                yield return value;
            }
            else
            {
                yield return JsValue.Undefined;
            }
        }
    }

    private static JsValue GetIteratorObject(JsValue source, RealmState realm, string methodName)
    {
        if (!TryGetObject(source, realm, out var accessor))
        {
            throw ThrowTypeError($"{methodName} requires an iterable object", realm: realm);
        }

        var receiver = JsValue.FromObjectUnsafe(accessor);
        // Iterator-like objects can be used directly if they expose a callable next.
        if (accessor.TryGetProperty("next", receiver, out var nextMethod) &&
            nextMethod.TryGetObject<IJsCallable>(out _))
        {
            return receiver;
        }

        // Otherwise, try Symbol.iterator to obtain the iterator.
        if (!accessor.TryGetProperty(SymbolKeys.Iterator, receiver, out var iteratorMethod) ||
            !iteratorMethod.TryGetObject<IJsCallable>(out var iteratorCallable))
        {
            throw ThrowTypeError($"{methodName} requires an iterable object", realm: realm);
        }

        var iterator = iteratorCallable.Invoke([], receiver);
        // Use TryGetObjectLike instead of TryGetObject to support iterator types like
        // JsArrayIterator and JsMapIterator that implement IJsObjectLike but are not JsObject
        if (!iterator.TryGetObjectLike(out _))
        {
            throw ThrowTypeError($"{methodName} Symbol.iterator must return an object", realm: realm);
        }

        return iterator;

    }
}
