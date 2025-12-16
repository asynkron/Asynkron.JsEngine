using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static readonly string SymbolSpeciesKey =
        SymbolKeys.Species;

    internal static readonly string SymbolIteratorKey =
        SymbolKeys.Iterator;

    internal static readonly string SymbolAsyncIteratorKey =
        SymbolKeys.AsyncIterator;

    internal static readonly string SymbolIsConcatSpreadableKey =
        SymbolKeys.IsConcatSpreadable;

    internal static IJsObjectLike ArraySpeciesCreate(JsValue original, long length, RealmState? realm)
    {
        length = Math.Max(length, 0);

        IJsObjectLike CreateDefaultArray()
        {
            if (length > MaxConcreteArrayLength)
            {
                throw ThrowRangeError("Array length exceeds 2^32 - 1", realm: realm);
            }

            var arr = new JsArray(realm);
            arr.SetProperty("length", new JsValue((double)length));
            return arr;
        }

        if (realm is null)
        {
            return CreateDefaultArray();
        }

        if (!original.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return CreateDefaultArray();
        }

        if (!IsArrayObject(original, realm, "array species creation"))
        {
            return CreateDefaultArray();
        }

        var useDefaultConstructor = false;
        // constructorValue is a JsValue struct - use IsUndefined to check
        if (!accessor.TryGetProperty("constructor", out var constructorValue) ||
            constructorValue.IsUndefined)
        {
            useDefaultConstructor = true;
        }

        if (!useDefaultConstructor &&
            constructorValue.TryGetObject<HostFunction>(out var hostCtor) &&
            hostCtor.RealmState is { } ctorRealm &&
            !ReferenceEquals(ctorRealm, realm) &&
            ReferenceEquals(hostCtor, ctorRealm.ArrayConstructor))
        {
            useDefaultConstructor = true;
        }

        var constructor = constructorValue;

        if (!useDefaultConstructor && constructorValue.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor))
        {
            JsValue species = JsValue.Undefined;
            if (ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
            {
                species = speciesValue;
            }

            if (species.IsNullOrUndefined)
            {
                useDefaultConstructor = true;
            }
            else
            {
                constructor = species;
            }
        }

        if (useDefaultConstructor)
        {
            return CreateDefaultArray();
        }

        if (!constructor.TryGetObject<IJsCallable>(out var callable) || !JsOps.IsConstructor(callable))
        {
            throw ThrowTypeError("Array species constructor must be a constructor", realm: realm);
        }

        var proto = ResolveConstructPrototype(callable, callable, realm);
        IJsObjectLike receiver;

        if (callable is HostFunction hostFunction && realm?.ArrayConstructor is not null &&
            ReferenceEquals(hostFunction, realm.ArrayConstructor))
        {
            receiver = new JsArray(realm);
        }
        else
        {
            receiver = new JsObject();
        }

        if (proto is not null)
        {
            receiver.SetPrototype(proto);
        }

        var constructed = callable.Invoke([new JsValue((double)length)], JsValue.FromObjectUnsafe(receiver));
        if (constructed.TryGetObject<IJsObjectLike>(out var objectLike))
        {
            return objectLike;
        }

        return receiver;
    }

    internal static IJsObjectLike CreateArrayFromResult(JsValue constructorCandidate, RealmState? realm, long length,
        bool passLengthToConstructor, string methodName)
    {
        if (constructorCandidate.TryGetObject<IJsCallable>(out var callable) && JsOps.IsConstructor(callable))
        {
            var constructorRealm = GetConstructorRealm(callable, realm) ?? realm;
            if (constructorRealm is null)
            {
                throw new InvalidOperationException($"{methodName} requires an active realm.");
            }

            var args = passLengthToConstructor
                ? new JsValue[] { new JsValue((double)Math.Max(length, 0)) }
                : Array.Empty<JsValue>();
            // Use Construct to properly invoke the constructor with new.target semantics
            var constructed = Construct(callable, args, callable, constructorRealm);
            var result = constructed.TryGetObject<IJsObjectLike>(out var constructedObj) ? constructedObj : null;
            if (result is null)
            {
                throw ThrowTypeError($"{methodName}: constructor did not return an object", realm: realm);
            }

            if (!passLengthToConstructor)
            {
                SetArrayLikeLength(result, 0);
            }

            return result;
        }

        if (passLengthToConstructor && length > MaxConcreteArrayLength)
        {
            throw ThrowRangeError($"{methodName} result exceeds 2^32 - 1 elements", realm: realm);
        }

        var array = new JsArray(realm);
        array.SetProperty("length", new JsValue(passLengthToConstructor ? Math.Max(length, 0) : 0d));
        return array;
    }

    internal static IJsObjectLike CreateArrayLikeReceiverForConstructor(IJsCallable constructor, RealmState? realm,
        long length)
    {
        var activeRealm = realm ?? GetConstructorRealm(constructor, null);
        if (activeRealm is null)
        {
            throw new InvalidOperationException("Array species constructor requires an active realm.");
        }

        var proto = ResolveConstructPrototype(constructor, constructor, activeRealm);
        IJsObjectLike receiver;

        if (constructor is HostFunction hostFunction && activeRealm.ArrayConstructor is not null &&
            ReferenceEquals(hostFunction, activeRealm.ArrayConstructor))
        {
            receiver = new JsArray(activeRealm);
        }
        else
        {
            receiver = new JsObject();
        }

        if (proto is not null)
        {
            receiver.SetPrototype(proto);
        }

        receiver.SetProperty("length", new JsValue((double)Math.Max(length, 0)));
        return receiver;
    }

    private static RealmState? GetConstructorRealm(IJsCallable constructor, RealmState? fallback)
    {
        if (TryGetRealmInfo(constructor, out var ctorRealm, out _))
        {
            return ctorRealm ?? fallback;
        }

        return fallback;
    }

    internal static void DeletePropertyOrThrow(IJsObjectLike? objectLike, string propertyKey, bool propertyExisted,
        string methodName, RealmState? realm)
    {
        if (objectLike is null)
        {
            if (propertyExisted)
            {
                throw ThrowTypeError($"{methodName} receiver does not support deleting property '{propertyKey}'",
                    realm: realm);
            }

            return;
        }

        if (!objectLike.Delete(propertyKey) && propertyExisted)
        {
            throw ThrowTypeError($"{methodName} could not delete property '{propertyKey}'", realm: realm);
        }
    }

    internal static bool IsConcatSpreadable(JsValue candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate.IsNullOrUndefined)
        {
            return false;
        }

        if (!candidate.TryGetObject<IJsPropertyAccessor>(out var propertyAccessor))
        {
            return false;
        }

        accessor = propertyAccessor;

        if (accessor.TryGetProperty(SymbolIsConcatSpreadableKey, out var spreadable) &&
            !ReferenceEquals(spreadable, Symbol.Undefined))
        {
            return JsOps.ToBoolean(spreadable);
        }

        if (IsArrayObject(candidate, realm, operation))
        {
            return true;
        }

        // Wrapper objects for strings are not concat-spreadable by default, but
        // become spreadable when @@isConcatSpreadable is truthy. Ensure they
        // fall back to non-spreadable here.
        return false;
    }

    internal static bool ArrayIsArray(JsValue candidate, RealmState? realm)
    {
        if (candidate.IsNull)
        {
            return false;
        }

        var inspected = UnwrapProxy(candidate, realm, "Array.isArray");
        if (inspected.TryGetObject<JsArray>(out var jsArray))
        {
            if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs.TryGetBoolean(out var isArgsValue) && isArgsValue)
            {
                return false;
            }

            return true;
        }

        if (realm?.ArrayPrototype is not null &&
            inspected.TryGetObject(out var inspectedObj) &&
            ReferenceEquals(inspectedObj, realm.ArrayPrototype))
        {
            return true;
        }

        return false;
    }

    internal static bool TryGetArrayForFlatten(JsValue candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate.IsNullOrUndefined)
        {
            return false;
        }

        if (candidate.TryGetObject<IJsPropertyAccessor>(out var propertyAccessor))
        {
            accessor = propertyAccessor;
        }
        else if (!TryGetObject(candidate, realm, out var boxed))
        {
            return false;
        }
        else
        {
            accessor = boxed;
        }

        if (!IsArrayObject(candidate, realm, operation))
        {
            return false;
        }

        return true;
    }

    internal static JsValue UnwrapProxy(JsValue candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected.TryGetObject<JsProxy>(out var proxy))
        {
            if (proxy.Handler is null)
            {
                throw ThrowTypeError($"{operation} called on revoked proxy", realm: realm);
            }

            inspected = JsValue.FromObjectUnsafe(proxy.Target);
        }

        return inspected;
    }

    internal static bool IsArrayObject(JsValue candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (!inspected.IsNull)
        {
            if (inspected.TryGetObject<JsArray>(out var array))
            {
                if (array.TryGetProperty("__arguments__", out var isArgs) && isArgs.TryGetBoolean(out var isArgsValue) && isArgsValue)
                {
                    return false;
                }

                return true;
            }

            if (inspected.TryGetObject<JsProxy>(out var proxy))
            {
                inspected = JsValue.FromObjectUnsafe(proxy.Target);
                continue;
            }

            if (realm?.ArrayPrototype is not null &&
                inspected.TryGetObject(out var inspectedObj) &&
                ReferenceEquals(inspectedObj, realm.ArrayPrototype))
            {
                return true;
            }

            break;
        }

        return false;
    }

    internal static void CopyArrayElement(IJsPropertyAccessor source, long sourceIndex, IJsObjectLike target,
        long targetIndex)
    {
        var sourceKey = ToIndexString(sourceIndex);
        var targetKey = ToIndexString(targetIndex);

        if (!HasProperty(source, sourceKey))
        {
            target.Delete(targetKey);
            return;
        }

        var value = source.TryGetProperty(sourceKey, out var obtained) ? obtained : JsValue.Undefined;
        target.SetProperty(targetKey, value);
    }

    internal static long FlattenIntoArray(IJsPropertyAccessor target, IJsPropertyAccessor source, long sourceLength,
        long targetIndex, long depth, IJsCallable? mapper, JsValue thisArg, RealmState? realm, string operation)
    {
        for (long k = 0; k < sourceLength; k++)
        {
            if (!TryGetExistingElement(source, k, out var element))
            {
                continue;
            }

            // element is already a JsValue from TryGetExistingElement
            var mapped = element;
            if (mapper is not null)
            {
                mapped = mapper.Invoke([element, new JsValue((double)k), JsValue.FromObjectUnsafe(source)], thisArg);
            }

        IJsPropertyAccessor? mappedAccessor = null;
        var spreadable = depth > 0 && TryGetArrayForFlatten(mapped, realm, operation, out mappedAccessor);

        if (spreadable && mappedAccessor is not null)
        {
            var newDepth = depth == double.PositiveInfinity ? depth : depth - 1;
                var elementLengthValue = mappedAccessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
                var elementLength = (long)ToLengthOrZero(elementLengthValue);
                var nextMapper = mapper is not null ? null : mapper;
                var nextThisArg = mapper is not null ? JsValue.Undefined : thisArg;
                targetIndex = FlattenIntoArray(target, mappedAccessor, elementLength, targetIndex, newDepth,
                    nextMapper, nextThisArg, realm, operation);
            }
            else
            {
                if (targetIndex >= MaxConcreteArrayLength)
                {
                    throw ThrowTypeError("Array operation result exceeds 2^32 - 1 elements", realm: realm);
                }

                target.SetProperty(ToIndexString(targetIndex), mapped);
                targetIndex++;
            }
        }

        return targetIndex;
    }
}
