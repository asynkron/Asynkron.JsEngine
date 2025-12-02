using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static readonly string SymbolSpeciesKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.species").GetHashCode()}";

    internal static readonly string SymbolIteratorKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";

    internal static readonly string SymbolAsyncIteratorKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.asyncIterator").GetHashCode()}";

    internal static readonly string SymbolIsConcatSpreadableKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.isConcatSpreadable").GetHashCode()}";

    internal static IJsObjectLike ArraySpeciesCreate(object? original, long length, RealmState? realm)
    {
        length = Math.Max(length, 0);

        IJsObjectLike CreateDefaultArray()
        {
            if (length > MaxConcreteArrayLength)
            {
                throw ThrowRangeError("Array length exceeds 2^32 - 1", realm: realm);
            }

            var arr = new JsArray(realm);
            arr.SetProperty("length", (double)length);
            return arr;
        }

        if (realm is null)
        {
            return CreateDefaultArray();
        }

        if (original is not IJsPropertyAccessor accessor)
        {
            return CreateDefaultArray();
        }

        if (!IsArrayObject(original, realm, "array species creation"))
        {
            return CreateDefaultArray();
        }

        var useDefaultConstructor = false;
        if (!accessor.TryGetProperty("constructor", out var constructorValue) ||
            ReferenceEquals(constructorValue, Symbol.Undefined))
        {
            useDefaultConstructor = true;
        }

        if (!useDefaultConstructor &&
            constructorValue is HostFunction hostCtor &&
            hostCtor.RealmState is { } ctorRealm &&
            !ReferenceEquals(ctorRealm, realm) &&
            ReferenceEquals(hostCtor, ctorRealm.ArrayConstructor))
        {
            useDefaultConstructor = true;
        }

        object? constructor = constructorValue;

        if (!useDefaultConstructor && constructor is IJsPropertyAccessor ctorAccessor)
        {
            object? species = null;
            if (ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
            {
                species = speciesValue;
            }

            if (species is null || ReferenceEquals(species, Symbol.Undefined))
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

        if (constructor is not IJsCallable callable || !JsOps.IsConstructor(callable))
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

        var constructed = callable.Invoke([(double)length], receiver);
        if (constructed is IJsObjectLike objectLike)
        {
            return objectLike;
        }

        return receiver;
    }

    internal static IJsObjectLike CreateArrayFromResult(object? constructorCandidate, RealmState? realm, long length,
        bool passLengthToConstructor, string methodName)
    {
        if (constructorCandidate is IJsCallable callable && JsOps.IsConstructor(callable))
        {
            var constructorRealm = GetConstructorRealm(callable, realm) ?? realm;
            var receiver =
                CreateArrayLikeReceiverForConstructor(callable, constructorRealm, passLengthToConstructor ? length : 0);
            var args = passLengthToConstructor
                ? new object?[] { (double)Math.Max(length, 0) }
                : Array.Empty<object?>();
            var constructed = callable.Invoke(args, receiver);
            var result = constructed as IJsObjectLike ?? receiver;
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
        array.SetProperty("length", passLengthToConstructor ? (double)Math.Max(length, 0) : 0d);
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

        receiver.SetProperty("length", (double)Math.Max(length, 0));
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

    internal static bool IsConcatSpreadable(object? candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return false;
        }

        if (candidate is IJsPropertyAccessor propertyAccessor)
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

        if (accessor.TryGetProperty(SymbolIsConcatSpreadableKey, out var spreadable) &&
            !ReferenceEquals(spreadable, Symbol.Undefined))
        {
            return JsOps.ToBoolean(spreadable);
        }

        return IsArrayObject(candidate, realm, operation);
    }

    internal static bool ArrayIsArray(object? candidate, RealmState? realm)
    {
        if (candidate is null)
        {
            return false;
        }

        var inspected = UnwrapProxy(candidate, realm, "Array.isArray");
        if (inspected is JsArray jsArray)
        {
            if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
            {
                return false;
            }

            return true;
        }

        if (realm?.ArrayPrototype is not null &&
            ReferenceEquals(inspected, realm.ArrayPrototype))
        {
            return true;
        }

        return false;
    }

    internal static bool TryGetArrayForFlatten(object? candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return false;
        }

        if (candidate is IJsPropertyAccessor propertyAccessor)
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

    internal static object? UnwrapProxy(object? candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected is JsProxy proxy)
        {
            if (proxy.Handler is null)
            {
                throw ThrowTypeError($"{operation} called on revoked proxy", realm: realm);
            }

            inspected = proxy.Target;
        }

        return inspected;
    }

    internal static bool IsArrayObject(object? candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected is not null)
        {
            if (inspected is JsArray array)
            {
                if (array.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
                {
                    return false;
                }

                return true;
            }

            if (inspected is JsProxy proxy)
            {
                inspected = proxy.Target;
                continue;
            }

            if (realm?.ArrayPrototype is not null &&
                ReferenceEquals(inspected, realm.ArrayPrototype))
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

        var value = source.TryGetProperty(sourceKey, out var obtained) ? obtained : Symbol.Undefined;
        target.SetProperty(targetKey, value);
    }

    internal static long FlattenIntoArray(IJsPropertyAccessor target, IJsPropertyAccessor source, long sourceLength,
        long targetIndex, long depth, IJsCallable? mapper, object? thisArg, RealmState? realm, string operation)
    {
        for (long k = 0; k < sourceLength; k++)
        {
            if (!TryGetExistingElement(source, k, out var element))
            {
                continue;
            }

            object? mapped = element;
            if (mapper is not null)
            {
                mapped = mapper.Invoke([element, (double)k, source], thisArg);
            }

        IJsPropertyAccessor? mappedAccessor = null;
        var spreadable = depth > 0 && TryGetArrayForFlatten(mapped, realm, operation, out mappedAccessor);

        if (spreadable && mappedAccessor is not null)
        {
            var newDepth = depth == double.PositiveInfinity ? depth : depth - 1;
                var elementLengthValue = mappedAccessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
                var elementLength = (long)ToLengthOrZero(elementLengthValue);
                IJsCallable? nextMapper = mapper is not null ? null : mapper;
                object? nextThisArg = mapper is not null ? null : thisArg;
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
