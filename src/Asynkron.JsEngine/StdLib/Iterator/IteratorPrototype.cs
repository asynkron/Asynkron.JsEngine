#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Iterator prototype provides helper methods for working with iterators.
/// These are the Iterator Helper methods from the ECMAScript proposal.
/// </summary>
[JsPrototype("Iterator", ToStringTag = "Iterator")]
[JsSymbolAlias("iterator", "__selfIterator")]
public sealed partial class IteratorPrototype : JsPrototype
{
    /// <summary>
    /// Helper method that returns this iterator (for Symbol.iterator)
    /// </summary>
    [JsHostMethod("__selfIterator", Length = 0d)]
    public static JsValue SelfIterator(JsValue thisValue) => thisValue;

    /// <summary>
    /// Iterator.prototype.map(mapper) - Returns a new iterator that yields mapped values
    /// </summary>
    [JsHostMethod("map", Length = 1d)]
    public JsValue Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var mapper = GetCallable(args, 0, "mapper");

        return CreateMappedIterator(iterated, mapper);
    }

    /// <summary>
    /// Iterator.prototype.filter(predicate) - Returns an iterator that yields filtered values
    /// </summary>
    [JsHostMethod("filter", Length = 1d)]
    public JsValue Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var predicate = GetCallable(args, 0, "predicate");

        return CreateFilteredIterator(iterated, predicate);
    }

    /// <summary>
    /// Iterator.prototype.take(limit) - Returns an iterator that yields at most limit values
    /// </summary>
    [JsHostMethod("take", Length = 1d)]
    public JsValue Take(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var limitArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var limit = ToIntegerOrInfinity(limitArg);

        if (limit < 0)
        {
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.take requires a non-negative number", null, Realm);
        }

        return CreateTakeIterator(iterated, limit);
    }

    /// <summary>
    /// Iterator.prototype.drop(limit) - Returns an iterator that skips the first limit values
    /// </summary>
    [JsHostMethod("drop", Length = 1d)]
    public JsValue Drop(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var limitArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var limit = ToIntegerOrInfinity(limitArg);

        if (limit < 0)
        {
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.drop requires a non-negative number", null, Realm);
        }

        return CreateDropIterator(iterated, limit);
    }

    /// <summary>
    /// Iterator.prototype.flatMap(mapper) - Returns an iterator that maps and flattens values
    /// </summary>
    [JsHostMethod("flatMap", Length = 1d)]
    public JsValue FlatMap(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var mapper = GetCallable(args, 0, "mapper");

        return CreateFlatMapIterator(iterated, mapper);
    }

    /// <summary>
    /// Iterator.prototype.reduce(reducer[, initialValue]) - Reduces iterator to a single value
    /// </summary>
    [JsHostMethod("reduce", Length = 1d)]
    public JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var reducer = GetCallable(args, 0, "reducer");

        var hasInitial = args.Count > 1;
        var accumulator = hasInitial ? args[1] : JsValue.Undefined;
        var counter = 0;

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                if (!hasInitial && counter == 0)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Reduce of empty iterator with no initial value", null, Realm);
                }

                return accumulator;
            }

            var value = IteratorValue(next);

            if (!hasInitial && counter == 0)
            {
                accumulator = value;
            }
            else
            {
                var callArgs = new[] { accumulator, value, (JsValue)(double)counter };
                accumulator = reducer.Invoke(callArgs, JsValue.Undefined);
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.toArray() - Converts iterator to an array
    /// </summary>
    [JsHostMethod("toArray", Length = 0d)]
    public JsValue ToArray(JsValue thisValue)
    {
        var iterated = GetIteratorDirect(thisValue);
        var result = new JsArray(Realm);

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                return JsValue.FromObjectUnsafe(result);
            }

            var value = IteratorValue(next);
            result.Push(value);
        }
    }

    /// <summary>
    /// Iterator.prototype.forEach(fn) - Executes a callback for each value
    /// </summary>
    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var fn = GetCallable(args, 0, "fn");
        var counter = 0;

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                return JsValue.Undefined;
            }

            var value = IteratorValue(next);
            var callArgs = new[] { value, (JsValue)(double)counter };
            fn.Invoke(callArgs, JsValue.Undefined);
            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.some(predicate) - Returns true if any value satisfies the predicate
    /// </summary>
    [JsHostMethod("some", Length = 1d)]
    public JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var predicate = GetCallable(args, 0, "predicate");
        var counter = 0;

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                return false;
            }

            var value = IteratorValue(next);
            var callArgs = new[] { value, (JsValue)(double)counter };
            var result = predicate.Invoke(callArgs, JsValue.Undefined);

            if (JsOps.ToBoolean(result))
            {
                IteratorClose(iterated);
                return true;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.every(predicate) - Returns true if all values satisfy the predicate
    /// </summary>
    [JsHostMethod("every", Length = 1d)]
    public JsValue Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var predicate = GetCallable(args, 0, "predicate");
        var counter = 0;

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                return true;
            }

            var value = IteratorValue(next);
            var callArgs = new[] { value, (JsValue)(double)counter };
            var result = predicate.Invoke(callArgs, JsValue.Undefined);

            if (!JsOps.ToBoolean(result))
            {
                IteratorClose(iterated);
                return false;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.find(predicate) - Returns the first value that satisfies the predicate
    /// </summary>
    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var iterated = GetIteratorDirect(thisValue);
        var predicate = GetCallable(args, 0, "predicate");
        var counter = 0;

        while (true)
        {
            var next = IteratorStep(iterated);
            if (next is null)
            {
                return JsValue.Undefined;
            }

            var value = IteratorValue(next);
            var callArgs = new[] { value, (JsValue)(double)counter };
            var result = predicate.Invoke(callArgs, JsValue.Undefined);

            if (JsOps.ToBoolean(result))
            {
                IteratorClose(iterated);
                return value;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype[Symbol.dispose]() - Disposes the iterator
    /// </summary>
    [JsSymbolMethod("dispose", Length = 0d)]
    public JsValue Dispose(JsValue thisValue)
    {
        if (thisValue.TryGetObject(out var obj) && obj is not null)
        {
            IteratorClose(obj);
        }

        return JsValue.Undefined;
    }

    #region Helper Methods

    /// <summary>
    /// Gets an iterator record from the given value (GetIteratorDirect)
    /// </summary>
    private static IJsObjectLike GetIteratorDirect(JsValue value)
    {
        if (!value.TryGetObject(out var obj) || obj is null)
        {
            throw StandardLibrary.ThrowTypeError("Iterator value is not an object", null, null);
        }

        // Verify it has a callable next method
        if (!obj.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetObject<IJsCallable>(out _))
        {
            throw StandardLibrary.ThrowTypeError("Iterator must have a callable 'next' method", null, null);
        }

        return obj;
    }

    /// <summary>
    /// Gets a callable from the arguments
    /// </summary>
    private static IJsCallable GetCallable(IReadOnlyList<JsValue> args, int index, string name)
    {
        var arg = args.Count > index ? args[index] : JsValue.Undefined;

        if (!arg.TryGetObject<IJsCallable>(out var callable) || callable is null)
        {
            throw StandardLibrary.ThrowTypeError($"{name} is not a function", null, null);
        }

        return callable;
    }

    /// <summary>
    /// Converts a value to an integer or infinity
    /// </summary>
    private static double ToIntegerOrInfinity(JsValue value)
    {
        var number = JsOps.ToNumber(value);

        if (double.IsNaN(number) || number == 0)
        {
            return 0;
        }

        if (double.IsInfinity(number))
        {
            return number;
        }

        return Math.Truncate(number);
    }

    /// <summary>
    /// Calls iterator.next() and returns the result, or null if done
    /// </summary>
    private static IJsObjectLike? IteratorStep(IJsObjectLike iterator)
    {
        if (!iterator.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetObject<IJsCallable>(out var nextMethod) ||
            nextMethod is null)
        {
            throw StandardLibrary.ThrowTypeError("Iterator must have a callable 'next' method", null, null);
        }

        var result = nextMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));

        if (!result.TryGetObject(out var resultObj) || resultObj is null)
        {
            throw StandardLibrary.ThrowTypeError("Iterator result must be an object", null, null);
        }

        if (resultObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
        {
            return null;
        }

        return resultObj;
    }

    /// <summary>
    /// Gets the value property from an iterator result
    /// </summary>
    private static JsValue IteratorValue(IJsObjectLike result)
    {
        if (result.TryGetProperty("value", out var value))
        {
            return value;
        }

        return JsValue.Undefined;
    }

    /// <summary>
    /// Closes an iterator by calling its return method if present
    /// </summary>
    private static void IteratorClose(IJsObjectLike iterator)
    {
        if (iterator.TryGetProperty("return", out var returnProp) &&
            returnProp.TryGetObject<IJsCallable>(out var returnMethod) &&
            returnMethod is not null)
        {
            try
            {
                returnMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
            }
            catch
            {
                // Ignore errors from return() per spec
            }
        }
    }

    #endregion

    #region Iterator Factory Methods

    private JsValue CreateMappedIterator(IJsObjectLike source, IJsCallable mapper)
    {
        var iterator = new JsObject { RealmState = Realm };
        var counter = 0;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            if (done)
            {
                return CreateIterResult(JsValue.Undefined, true);
            }

            isExecuting = true;
            try
            {
                var next = IteratorStep(source);
                if (next is null)
                {
                    done = true;
                    return CreateIterResult(JsValue.Undefined, true);
                }

                var value = IteratorValue(next);
                var callArgs = new[] { value, (JsValue)(double)counter };
                counter++;

                try
                {
                    var mapped = mapper.Invoke(callArgs, JsValue.Undefined);
                    return CreateIterResult(mapped, false);
                }
                catch
                {
                    done = true;
                    IteratorClose(source);
                    throw;
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
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                IteratorClose(source);
                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateFilteredIterator(IJsObjectLike source, IJsCallable predicate)
    {
        var iterator = new JsObject { RealmState = Realm };
        var counter = 0;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
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
                    var next = IteratorStep(source);
                    if (next is null)
                    {
                        done = true;
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    var value = IteratorValue(next);
                    var callArgs = new[] { value, (JsValue)(double)counter };
                    counter++;

                    try
                    {
                        var selected = predicate.Invoke(callArgs, JsValue.Undefined);
                        if (JsOps.ToBoolean(selected))
                        {
                            return CreateIterResult(value, false);
                        }
                    }
                    catch
                    {
                        done = true;
                        IteratorClose(source);
                        throw;
                    }
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
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                IteratorClose(source);
                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateTakeIterator(IJsObjectLike source, double limit)
    {
        var iterator = new JsObject { RealmState = Realm };
        var remaining = limit;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            if (done)
            {
                return CreateIterResult(JsValue.Undefined, true);
            }

            isExecuting = true;
            try
            {
                if (remaining <= 0)
                {
                    done = true;
                    IteratorClose(source);
                    return CreateIterResult(JsValue.Undefined, true);
                }

                var next = IteratorStep(source);
                if (next is null)
                {
                    done = true;
                    return CreateIterResult(JsValue.Undefined, true);
                }

                remaining--;
                var value = IteratorValue(next);

                if (remaining <= 0)
                {
                    done = true;
                    IteratorClose(source);
                }

                return CreateIterResult(value, false);
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
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                IteratorClose(source);
                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateDropIterator(IJsObjectLike source, double limit)
    {
        var iterator = new JsObject { RealmState = Realm };
        var remaining = limit;
        var done = false;
        var isExecuting = false;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            if (done)
            {
                return CreateIterResult(JsValue.Undefined, true);
            }

            isExecuting = true;
            try
            {
                // Skip the first 'remaining' items
                while (remaining > 0)
                {
                    var skipped = IteratorStep(source);
                    if (skipped is null)
                    {
                        done = true;
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    remaining--;
                }

                var next = IteratorStep(source);
                if (next is null)
                {
                    done = true;
                    return CreateIterResult(JsValue.Undefined, true);
                }

                var value = IteratorValue(next);
                return CreateIterResult(value, false);
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
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                IteratorClose(source);
                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateFlatMapIterator(IJsObjectLike source, IJsCallable mapper)
    {
        var iterator = new JsObject { RealmState = Realm };
        var counter = 0;
        var done = false;
        var isExecuting = false;
        IJsObjectLike? innerIterator = null;

        var nextFunc = new HostFunction((_, _) =>
        {
            if (isExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
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
                    // If we have an inner iterator, try to get the next value from it
                    if (innerIterator is not null)
                    {
                        var innerNext = IteratorStep(innerIterator);
                        if (innerNext is not null)
                        {
                            var innerValue = IteratorValue(innerNext);
                            return CreateIterResult(innerValue, false);
                        }

                        // Inner iterator is exhausted
                        innerIterator = null;
                    }

                    // Get the next value from the source iterator
                    var next = IteratorStep(source);
                    if (next is null)
                    {
                        done = true;
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    var value = IteratorValue(next);
                    var callArgs = new[] { value, (JsValue)(double)counter };
                    counter++;

                    try
                    {
                        var mapped = mapper.Invoke(callArgs, JsValue.Undefined);

                        // Try to get an iterator from the mapped value
                        if (mapped.TryGetObject(out var mappedObj) && mappedObj is not null)
                        {
                            // Check for Symbol.iterator
                            if (mappedObj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod) &&
                                iterMethod.TryGetObject<IJsCallable>(out var iterCallable) &&
                                iterCallable is not null)
                            {
                                var iterResult = iterCallable.Invoke([], mapped);
                                if (iterResult.TryGetObject(out var iter) && iter is not null)
                                {
                                    innerIterator = iter;
                                    continue; // Get first value from inner iterator
                                }
                            }
                        }

                        // If mapped value is not iterable, yield it directly
                        return CreateIterResult(mapped, false);
                    }
                    catch
                    {
                        done = true;
                        IteratorClose(source);
                        throw;
                    }
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
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, Realm);
            }

            isExecuting = true;
            try
            {
                done = true;
                if (innerIterator is not null)
                {
                    IteratorClose(innerIterator);
                }

                IteratorClose(source);
                return CreateIterResult(JsValue.Undefined, true);
            }
            finally
            {
                isExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)returnFunc);
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private void SetupIteratorPrototype(JsObject iterator)
    {
        // Set Symbol.iterator to return self
        var iteratorKey = SymbolKeys.Iterator;
        iterator.SetProperty(iteratorKey, (JsValue)new HostFunction((thisVal, _) => thisVal, isConstructor: false));

        // Set the prototype to Iterator.prototype if available
        if (Prototype is JsObject proto)
        {
            iterator.SetPrototype(proto);
        }
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
