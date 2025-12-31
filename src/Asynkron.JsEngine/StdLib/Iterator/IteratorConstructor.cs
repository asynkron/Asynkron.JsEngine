#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
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
        // Iterator is an abstract class - it cannot be directly instantiated with 'new Iterator()'
        // However, subclasses can extend it
        if (thisValue.TryGetObject(out var obj) && obj is not null && obj.GetType() != typeof(JsObject))
        {
            // Being called from a subclass constructor
            return thisValue;
        }

        // Check if this is being called as a base class (new.target !== Iterator)
        // For now, just create a basic iterator object
        var iteratorObj = PrepareThisObject(JsValue.Undefined, false);
        if (Prototype is not null && iteratorObj.Prototype is null)
        {
            iteratorObj.SetPrototype(Prototype);
        }

        iteratorObj.RealmState ??= Realm;
        return new JsValue(iteratorObj);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        // Store the iterator prototype in the realm for static method access
        Realm.IteratorPrototype ??= Prototype as JsObject;

        if (Realm.IteratorPrototype is { } iteratorPrototype)
        {
            if (Realm.ArrayIteratorPrototype is { } arrayIteratorPrototype)
            {
                arrayIteratorPrototype.SetPrototype(iteratorPrototype);
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

        // Get an iterator from the value
        var iterator = GetIteratorFlattenable(value, realm, out var alreadyIterator);

        if (alreadyIterator)
        {
            // Check if the iterator already has Iterator.prototype in its prototype chain
            if (iterator.TryGetObject(out var iteratorObj) && iteratorObj is not null)
            {
                if (HasIteratorPrototype(iteratorObj, realm))
                {
                    // Already a proper Iterator - return as-is
                    return iterator;
                }
            }
        }

        // Wrap the iterator in a WrapForValidIteratorPrototype object
        return CreateWrappedIterator(iterator, realm);
    }

    /// <summary>
    /// Iterator.concat(...items) - Creates an iterator that concatenates all the given iterables.
    /// </summary>
    [JsConstructorMethod("concat", Length = 0d)]
    public static JsValue Concat(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        // Validate all arguments are iterable first
        var iterables = new List<JsValue>();
        foreach (var arg in args)
        {
            if (!IsIterable(arg))
            {
                throw StandardLibrary.ThrowTypeError("Iterator.concat requires iterable arguments", null, realm);
            }

            iterables.Add(arg);
        }

        return CreateConcatIterator(iterables, realm);
    }

    #region Helper Methods

    /// <summary>
    /// Gets an iterator from a value, handling both iterables and iterator-like objects.
    /// </summary>
    private static JsValue GetIteratorFlattenable(JsValue value, RealmState? realm, out bool alreadyIterator)
    {
        alreadyIterator = false;

        if (!value.TryGetObject(out var obj) || obj is null)
        {
            throw StandardLibrary.ThrowTypeError("Iterator.from requires an object", null, realm);
        }

        // First, check if it's an iterator (has a next method)
        if (obj.TryGetProperty("next", out var nextProp) &&
            nextProp.TryGetObject<IJsCallable>(out _))
        {
            alreadyIterator = true;
            return value;
        }

        // Check for Symbol.iterator
        if (obj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) &&
            iterMethod.TryGetObject<IJsCallable>(out var iterCallable) &&
            iterCallable is not null)
        {
            var result = iterCallable.Invoke([], value);
            if (!result.TryGetObject(out var iteratorObj) || iteratorObj is null)
            {
                throw StandardLibrary.ThrowTypeError("Symbol.iterator must return an object", null, realm);
            }

            alreadyIterator = true;
            return result;
        }

        throw StandardLibrary.ThrowTypeError("Value is not iterable", null, realm);
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
    /// Checks if a value is iterable (has Symbol.iterator).
    /// </summary>
    private static bool IsIterable(JsValue value)
    {
        if (!value.TryGetObject(out var obj) || obj is null)
        {
            // Strings are iterable
            if (value.TryGetString(out _))
            {
                return true;
            }

            return false;
        }

        // Check for Symbol.iterator
        return obj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) &&
               iterMethod.TryGetObject<IJsCallable>(out _);
    }

    /// <summary>
    /// Gets an iterator from an iterable value.
    /// </summary>
    private static IJsObjectLike GetIteratorFromIterable(JsValue value)
    {
        if (value.TryGetString(out var str) && str is not null)
        {
            return CreateStringIterator(str);
        }

        if (!value.TryGetObject(out var obj) || obj is null)
        {
            throw StandardLibrary.ThrowTypeError("Value is not iterable", null, null);
        }

        if (!obj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) ||
            !iterMethod.TryGetObject<IJsCallable>(out var iterCallable) ||
            iterCallable is null)
        {
            throw StandardLibrary.ThrowTypeError("Value is not iterable", null, null);
        }

        var result = iterCallable.Invoke([], value);
        if (!result.TryGetObject(out var iterator) || iterator is null)
        {
            throw StandardLibrary.ThrowTypeError("Symbol.iterator must return an object", null, null);
        }

        return iterator;
    }

    /// <summary>
    /// Creates a string iterator.
    /// </summary>
    private static JsObject CreateStringIterator(string str)
    {
        var iterator = new JsObject();
        var index = 0;

        var nextFunc = new HostFunction((_, _) =>
        {
            var result = new JsObject();
            if (index < str.Length)
            {
                var first = str[index];
                string charValue;
                if (char.IsHighSurrogate(first) && index + 1 < str.Length &&
                    char.IsLowSurrogate(str[index + 1]))
                {
                    charValue = str.Substring(index, 2);
                    index += 2;
                }
                else
                {
                    charValue = first.ToString();
                    index++;
                }

                result.SetProperty("value", (JsValue)charValue);
                result.SetProperty("done", false);
            }
            else
            {
                result.SetProperty("value", JsValue.Undefined);
                result.SetProperty("done", true);
            }

            return new JsValue(result);
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty(SymbolKeys.Iterator, (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

        return iterator;
    }

    /// <summary>
    /// Creates a wrapped iterator that delegates to the underlying iterator
    /// but has Iterator.prototype in its prototype chain.
    /// </summary>
    private static JsValue CreateWrappedIterator(JsValue underlyingIterator, RealmState? realm)
    {
        if (!underlyingIterator.TryGetObject(out var underlying) || underlying is null)
        {
            throw StandardLibrary.ThrowTypeError("Iterator must be an object", null, realm);
        }

        var wrapper = new JsObject { RealmState = realm };
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, args) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, realm);
            }

            if (done)
            {
                return CreateIterResult(JsValue.Undefined, true);
            }

            isExecuting = true;
            try
            {
                if (!underlying.TryGetProperty("next", out var nextProp) ||
                    !nextProp.TryGetObject<IJsCallable>(out var nextMethod) ||
                    nextMethod is null)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator must have a next method", null, realm);
                }

                var result = nextMethod.Invoke(args.Count > 0 ? args : [], underlyingIterator);
                if (!result.TryGetObject(out var resultObj) || resultObj is null)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result must be an object", null, realm);
                }

                if (resultObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
                {
                    done = true;
                }

                return result;
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        var returnFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                if (underlying.TryGetProperty("return", out var returnProp) &&
                    returnProp.TryGetObject<IJsCallable>(out var returnMethod) &&
                    returnMethod is not null)
                {
                    return returnMethod.Invoke([], underlyingIterator);
                }

                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        wrapper.SetProperty("next", (JsValue)nextFunc);
        wrapper.SetProperty("return", (JsValue)returnFunc);

        // Set Symbol.iterator to return self
        wrapper.SetProperty(SymbolKeys.Iterator, (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

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
    /// </summary>
    private static JsValue CreateConcatIterator(List<JsValue> iterables, RealmState? realm)
    {
        var iterator = new JsObject { RealmState = realm };
        var iterableIndex = 0;
        IJsObjectLike? currentIterator = null;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, realm);
            }

            if (done)
            {
                return CreateIterResult(JsValue.Undefined, true);
            }

            isExecuting = true;
            try
            {
                while (true)
                {
                    // If we have a current iterator, try to get the next value
                    if (currentIterator is not null)
                    {
                        if (currentIterator.TryGetProperty("next", out var nextProp) &&
                            nextProp.TryGetObject<IJsCallable>(out var nextMethod) &&
                            nextMethod is not null)
                        {
                            var result = nextMethod.Invoke([], JsValue.FromObjectUnsafe(currentIterator));
                            if (result.TryGetObject(out var resultObj) && resultObj is not null)
                            {
                                if (!resultObj.TryGetProperty("done", out var doneProp) || !JsOps.ToBoolean(doneProp))
                                {
                                    return result;
                                }
                            }
                        }

                        // Current iterator is exhausted
                        currentIterator = null;
                    }

                    // Move to the next iterable
                    if (iterableIndex >= iterables.Count)
                    {
                        done = true;
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    var nextIterable = iterables[iterableIndex++];
                    currentIterator = GetIteratorFromIterable(nextIterable);
                }
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        var returnFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                if (currentIterator is not null &&
                    currentIterator.TryGetProperty("return", out var returnProp) &&
                    returnProp.TryGetObject<IJsCallable>(out var returnMethod) &&
                    returnMethod is not null)
                {
                    try
                    {
                        returnMethod.Invoke([], JsValue.FromObjectUnsafe(currentIterator));
                    }
                    catch
                    {
                        // Ignore errors from return()
                    }
                }

                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);

        // Set Symbol.iterator to return self
        iterator.SetProperty(SymbolKeys.Iterator, (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

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

    #endregion
}
