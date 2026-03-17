#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// The Iterator constructor. Iterator is an abstract class that cannot be directly instantiated.
/// It provides static methods like Iterator.from() and Iterator.concat().
/// </summary>
[JsConstructor("Iterator", PrototypeType = typeof(IteratorPrototype), Length = 0d, DisplayName = "Iterator")]
public sealed partial class IteratorConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Preserve the pre-created thisValue so subclass instances keep their own prototype
        // and methods (e.g. class MyIter extends Iterator { next() { ... } }).
        if (thisValue.TryGetObject(out _))
        {
            return thisValue;
        }

        // Fallback for unusual construction paths that did not provide an object thisValue.
        var iteratorObj = PrepareThisObject(JsValue.Undefined, false);
        if (iteratorObj.Prototype is null)
        {
            iteratorObj.SetPrototype(Prototype);
        }

        iteratorObj.RealmState ??= Realm;
        return new JsValue(iteratorObj);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        constructor.SetInvokeWithContext((args, thisValue, context, newTarget) =>
        {
            if (newTarget.IsUndefined || ReferenceEquals(newTarget.ObjectValue, constructor))
            {
                throw StandardLibrary.ThrowTypeError("Iterator is not directly constructable", context, Realm);
            }

            return ConstructInstance(thisValue, args);
        });

        // Store the iterator prototype in the realm for static method access
        Realm.IteratorPrototype ??= Prototype as JsObject;

        if (Realm.IteratorPrototype is { } iteratorPrototype)
        {
            var constructorGetter = new HostFunction((_, _) => JsValue.FromObjectUnsafe(constructor), Realm, false);
            var constructorSetter = new HostFunction((thisValue, args) =>
            {
                if (!thisValue.TryGetObject<IJsObjectLike>(out var receiverObject))
                {
                    throw StandardLibrary.ThrowTypeError("Iterator.prototype.constructor setter requires an object",
                        realm: Realm);
                }

                if (ReferenceEquals(receiverObject, iteratorPrototype))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot assign to Iterator.prototype.constructor",
                        realm: Realm);
                }

                var nextValue = args.Count > 0 ? args[0] : JsValue.Undefined;
                var existingDescriptor = receiverObject.GetOwnPropertyDescriptor("constructor");
                if (existingDescriptor is null)
                {
                    if (receiverObject is IExtensibilityControl { IsExtensible: false })
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "Cannot define property 'constructor' on non-extensible object",
                            realm: Realm);
                    }

                    receiverObject.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = nextValue,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                    return JsValue.Undefined;
                }

                if (existingDescriptor.IsAccessorDescriptor)
                {
                    if (existingDescriptor.Set is null)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "Cannot assign to property 'constructor' that has only a getter",
                            realm: Realm);
                    }

                    existingDescriptor.Set.Invoke([nextValue], thisValue);
                    return JsValue.Undefined;
                }

                if (!existingDescriptor.Writable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot assign to read only property 'constructor'",
                        realm: Realm);
                }

                receiverObject.DefineProperty("constructor", new PropertyDescriptor { JsValue = nextValue });
                return JsValue.Undefined;
            }, Realm, false);

            iteratorPrototype.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Get = constructorGetter,
                    Set = constructorSetter,
                    Enumerable = false,
                    Configurable = true
                });

            if (Realm.ArrayIteratorPrototype is { } arrayIteratorPrototype)
            {
                arrayIteratorPrototype.SetPrototype(iteratorPrototype);
            }

            // Ensure generator prototype chain includes Iterator.prototype
            if (Realm.GeneratorPrototype is { } generatorPrototype &&
                !ReferenceEquals(generatorPrototype.Prototype, iteratorPrototype))
            {
                generatorPrototype.SetPrototype(iteratorPrototype);
            }

            // Ensure Map iterator prototype chain includes Iterator.prototype
            if (Realm.MapIteratorPrototype is { } mapIteratorPrototype &&
                !ReferenceEquals(mapIteratorPrototype.Prototype, iteratorPrototype))
            {
                mapIteratorPrototype.SetPrototype(iteratorPrototype);
            }

            // Ensure Set iterator prototype chain includes Iterator.prototype
            if (Realm.SetIteratorPrototype is { } setIteratorPrototype &&
                !ReferenceEquals(setIteratorPrototype.Prototype, iteratorPrototype))
            {
                setIteratorPrototype.SetPrototype(iteratorPrototype);
            }
        }
    }

    /// <summary>
    /// Iterator.from(value) - Creates an iterator from an iterable or iterator-like object.
    /// If value is already an iterator with the correct prototype, returns it directly.
    /// Otherwise, wraps it in a new iterator that delegates to the original.
    /// </summary>
    [JsConstructorMethod("from", Length = 1d)]
    public static JsValue From(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        var value = args.Count > 0 ? args[0] : JsValue.Undefined;

        // GetIteratorFlattenable(O, iterate-strings) -- for Iterator.from, strings are allowed as iterables
        var iterated = GetIteratorFlattenableForFrom(value, realm, out var alreadyIterator);

        if (alreadyIterator)
        {
            // Check if the iterator already has Iterator.prototype in its prototype chain
            if (HasIteratorPrototype(iterated.Iterator, realm))
            {
                // Already a proper Iterator - return as-is
                return JsValue.FromObjectUnsafe(iterated.Iterator);
            }
        }

        // Wrap the iterator in a WrapForValidIteratorPrototype object
        return CreateWrappedIterator(iterated, realm);
    }

    /// <summary>
    /// Iterator.concat(...items) - Creates an iterator that concatenates all the given iterables.
    /// </summary>
    [JsConstructorMethod("concat", Length = 0d)]
    public static JsValue Concat(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // Per spec: For each element item of items, do
        //   a. If item is not an Object, throw a TypeError exception.
        //   b. Let method be ? GetMethod(item, %Symbol.iterator%).
        //   c. If method is undefined, throw a TypeError exception.
        var iterables = new List<(JsValue Value, IJsCallable IterMethod)>();
        foreach (var arg in args)
        {
            // Step 2a: Check if item is an object (not a primitive)
            if (!arg.TryGetPropertyAccessor(out var accessor))
            {
                throw StandardLibrary.ThrowTypeError("Iterator.concat requires iterable arguments", null, realm);
            }

            // Step 2b-c: Check for Symbol.iterator method
            if (!accessor.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) ||
                !iterMethod.TryGetObject<IJsCallable>(out var iterCallable))
            {
                throw StandardLibrary.ThrowTypeError("Iterator.concat requires iterable arguments", null, realm);
            }

            // Store the iterable and its Symbol.iterator method (captured once per spec)
            iterables.Add((arg, iterCallable));
        }

        return CreateConcatIterator(iterables, realm);
    }

    #region Helper Methods

    /// <summary>
    /// GetIteratorFlattenable for Iterator.from -- handles both iterables and iterator-like objects.
    /// Per spec:
    /// 1. If O is not an Object, throw TypeError (but for Iterator.from strings are handled via Symbol.iterator).
    /// 2. Let iteratorMethod = Get(O, Symbol.iterator).
    /// 3. If iteratorMethod is not undefined/null:
    ///    a. If not callable, throw TypeError.
    ///    b. Let iterator = Call(iteratorMethod, O).
    ///    c. If not Object, throw TypeError.
    /// 4. Else: let iterator = O (treat as iterator-like).
    /// 5. Return GetIteratorDirect(iterator).
    /// </summary>
    private static IteratorPrototype.IteratorRecord GetIteratorFlattenableForFrom(
        JsValue value, RealmState? realm, out bool alreadyIterator)
    {
        alreadyIterator = false;

        // Handle string primitives -- they are iterable via Symbol.iterator on the String wrapper
        if (value.TryGetString(out _))
        {
            // Strings need to go through their Symbol.iterator
            if (!value.TryGetPropertyAccessor(out var strAccessor))
            {
                throw StandardLibrary.ThrowTypeError("Iterator.from requires an object or string", null, realm);
            }

            if (strAccessor.TryGetProperty(SymbolKeys.Iterator, out var strIterMethod) &&
                strIterMethod.TryGetObject<IJsCallable>(out var strIterCallable))
            {
                var strResult = strIterCallable.Invoke([], value);
                if (!strResult.TryGetObject<IJsObjectLike>(out var strIterObj))
                {
                    throw StandardLibrary.ThrowTypeError("Symbol.iterator must return an object", null, realm);
                }

                alreadyIterator = true;
                return GetIteratorDirectStatic(strIterObj);
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", null, realm);
        }

        // Use TryGetPropertyAccessor to handle both JsObject and JsArray
        if (!value.TryGetPropertyAccessor(out var accessor))
        {
            throw StandardLibrary.ThrowTypeError("Iterator.from requires an object", null, realm);
        }

        // Check for Symbol.iterator
        if (accessor.TryGetProperty(SymbolKeys.Iterator, out var iterMethod))
        {
            if (iterMethod.IsNullOrUndefined)
            {
                // Symbol.iterator is null/undefined -- fall back to treating as iterator-like
                alreadyIterator = true;
                if (!value.TryGetObject<IJsObjectLike>(out var objLike))
                {
                    throw StandardLibrary.ThrowTypeError("Iterator.from requires an object", null, realm);
                }

                return GetIteratorDirectStatic(objLike);
            }

            if (iterMethod.TryGetObject<IJsCallable>(out var iterCallable))
            {
                // Symbol.iterator is callable -- call it
                var result = iterCallable.Invoke([], value);
                if (!result.TryGetObject<IJsObjectLike>(out var iterObj))
                {
                    throw StandardLibrary.ThrowTypeError("Symbol.iterator must return an object", null, realm);
                }

                alreadyIterator = true;
                return GetIteratorDirectStatic(iterObj);
            }

            // Symbol.iterator is truthy but not callable -- TypeError
            throw StandardLibrary.ThrowTypeError("Symbol.iterator is not a function", null, realm);
        }

        // No Symbol.iterator -- treat as iterator-like
        alreadyIterator = true;
        if (!value.TryGetObject<IJsObjectLike>(out var fallbackObj))
        {
            throw StandardLibrary.ThrowTypeError("Iterator.from requires an object", null, realm);
        }

        return GetIteratorDirectStatic(fallbackObj);
    }

    /// <summary>
    /// GetIteratorDirect -- static version for use in IteratorConstructor.
    /// Captures the next method once.
    /// </summary>
    private static IteratorPrototype.IteratorRecord GetIteratorDirectStatic(IJsObjectLike obj)
    {
        if (!obj.TryGetProperty("next", out var nextMethod))
        {
            nextMethod = JsValue.Undefined;
        }

        return new IteratorPrototype.IteratorRecord(obj, nextMethod);
    }

    /// <summary>
    /// Checks if an object has Iterator.prototype in its prototype chain.
    /// </summary>
    private static bool HasIteratorPrototype(IJsObjectLike obj, RealmState? realm)
    {
        var iteratorProto = realm?.IteratorPrototype;
        if (iteratorProto is null)
        {
            return false;
        }

        var current = obj.Prototype;
        while (current is not null)
        {
            if (current == iteratorProto)
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

    /// <summary>
    /// IteratorNext using an IteratorRecord -- used by concat.
    /// Uses TryGetObjectLike to accept both JsObject and IteratorResultObject.
    /// </summary>
    private static IJsObjectLike IteratorNext(IteratorPrototype.IteratorRecord iterated)
    {
        if (!iterated.NextMethod.TryGetObject<IJsCallable>(out var nextMethod))
        {
            throw StandardLibrary.ThrowTypeError("Iterator must have a callable 'next' method", null, null);
        }

        var result = nextMethod.Invoke([], JsValue.FromObjectUnsafe(iterated.Iterator));

        if (!result.TryGetObjectLike(out var resultObj))
        {
            throw StandardLibrary.ThrowTypeError("Iterator result must be an object", null, null);
        }

        return resultObj;
    }

    /// <summary>
    /// Creates a wrapped iterator that delegates to the underlying iterator
    /// but has Iterator.prototype in its prototype chain.
    /// Uses the captured IteratorRecord so next is only read once.
    /// </summary>
    private static JsValue CreateWrappedIterator(IteratorPrototype.IteratorRecord iterated, RealmState? realm)
    {
        var wrapper = new JsObject { RealmState = realm };
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, args) =>
            ExecuteIteratorCommand(shortCircuitIfDone: true, done, ref isExecuting, realm, () =>
            {
                if (!iterated.NextMethod.TryGetObject<IJsCallable>(out var nextMethod))
                {
                    throw StandardLibrary.ThrowTypeError("Iterator must have a next method", null, realm);
                }

                var result = nextMethod.Invoke(args.Count > 0 ? args : [],
                    JsValue.FromObjectUnsafe(iterated.Iterator));
                if (!result.TryGetPropertyAccessor(out var resultObj))
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result must be an object", null, realm);
                }

                if (resultObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
                {
                    done = true;
                }

                return result;
            }), isConstructor: false);

        var returnFunc = new HostFunction((_, _) =>
            ExecuteIteratorCommand(shortCircuitIfDone: false, done, ref isExecuting, realm, () =>
            {
                done = true;
                if (iterated.Iterator.TryGetProperty("return", out var returnProp) &&
                    returnProp.TryGetObject<IJsCallable>(out var returnMethod))
                {
                    return returnMethod.Invoke([], JsValue.FromObjectUnsafe(iterated.Iterator));
                }

                return CreateIterResult(JsValue.Undefined, true);
            }), isConstructor: false);

        wrapper.SetProperty("next", (JsValue)nextFunc);
        wrapper.SetProperty("return", (JsValue)returnFunc);

        // Set Symbol.iterator to return self
        wrapper.SetProperty(SymbolKeys.Iterator,
            (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

        // Set the prototype to Iterator.prototype
        var iteratorProto = realm?.IteratorPrototype;
        if (iteratorProto is not null)
        {
            wrapper.SetPrototype(iteratorProto);
        }

        return new JsValue(wrapper);
    }

    /// <summary>
    /// Creates an iterator that concatenates multiple iterables.
    /// The Symbol.iterator method for each iterable is captured once during validation (per spec).
    /// </summary>
    private static JsValue CreateConcatIterator(List<(JsValue Value, IJsCallable IterMethod)> iterables,
        RealmState? realm)
    {
        var iterator = new JsObject { RealmState = realm };
        var iterableIndex = 0;
        IteratorPrototype.IteratorRecord? currentIterated = null;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
            ExecuteIteratorCommand(shortCircuitIfDone: true, done, ref isExecuting, realm, () =>
            {
                while (true)
                {
                    // If we have a current iterator, try to get the next value
                    if (currentIterated is not null)
                    {
                        var result = IteratorNext(currentIterated);
                        if (result.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
                        {
                            // Current iterator is exhausted
                            currentIterated = null;
                        }
                        else
                        {
                            // Return the result directly (not re-wrapped)
                            return JsValue.FromObjectUnsafe(result);
                        }
                    }

                    // Move to the next iterable
                    if (iterableIndex >= iterables.Count)
                    {
                        done = true;
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    var (nextIterableValue, iterMethod) = iterables[iterableIndex++];
                    var iterResult = iterMethod.Invoke([], nextIterableValue);
                    if (!iterResult.TryGetObject<IJsObjectLike>(out var iterObj))
                    {
                        throw StandardLibrary.ThrowTypeError("Symbol.iterator must return an object", null, realm);
                    }

                    currentIterated = GetIteratorDirectStatic(iterObj);
                }
            }), isConstructor: false);

        var returnFunc = new HostFunction((_, _) =>
            ExecuteIteratorCommand(shortCircuitIfDone: false, done, ref isExecuting, realm, () =>
            {
                var alreadyDone = done;
                done = true;
                if (!alreadyDone && currentIterated is not null)
                {
                    // Forward return to the current active iterator only on first call
                    IteratorPrototype.IteratorCloseNormal(currentIterated.Iterator);
                }

                return CreateIterResult(JsValue.Undefined, true);
            }), isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);

        // Set Symbol.iterator to return self
        iterator.SetProperty(SymbolKeys.Iterator,
            (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

        // Set the prototype to Iterator.prototype
        var iteratorProto = realm?.IteratorPrototype;
        if (iteratorProto is not null)
        {
            iterator.SetPrototype(iteratorProto);
        }

        return new JsValue(iterator);
    }

    private static JsValue CreateIterResult(JsValue value, bool done)
    {
        var result = new JsObject();
        result.SetProperty("value", value);
        result.SetProperty("done", done);
        return new JsValue(result);
    }

    private static JsValue ExecuteIteratorCommand(
        bool shortCircuitIfDone,
        bool done,
        ref bool isExecuting,
        RealmState? realm,
        Func<JsValue> action)
    {
        if (isExecuting)
        {
            throw StandardLibrary.ThrowTypeError("Generator is already executing", null, realm);
        }

        if (shortCircuitIfDone && done)
        {
            return CreateIterResult(JsValue.Undefined, true);
        }

        isExecuting = true;
        try
        {
            return action();
        }
        finally
        {
            isExecuting = false;
        }
    }

    #endregion
}
