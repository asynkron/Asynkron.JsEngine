#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Iterator prototype provides helper methods for working with iterators.
/// These are the Iterator Helper methods from the ECMAScript proposal.
/// </summary>
[JsPrototype("Iterator", ToStringTag = "Iterator")]
public sealed partial class IteratorPrototype : JsPrototype
{
    /// <summary>
    /// Helper method that returns this iterator (for Symbol.iterator)
    /// </summary>
    [JsSymbolMethod("iterator", DisplayName = "[Symbol.iterator]", Length = 0d)]
    public static JsValue SelfIterator(JsValue thisValue) => thisValue;

    /// <summary>
    /// Iterator.prototype.map(mapper) - Returns a new iterator that yields mapped values.
    /// Spec order: 1-2. RequireObject, 3. IsCallable check, 4. GetIteratorDirect.
    /// </summary>
    [JsHostMethod("map", Length = 1d)]
    public JsValue Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var mapper = RequireCallable(args, 0, "mapper", obj);
        var iterated = GetIteratorDirect(obj);

        return CreateMappedIterator(iterated, mapper);
    }

    /// <summary>
    /// Iterator.prototype.filter(predicate) - Returns an iterator that yields filtered values.
    /// Spec order: 1-2. RequireObject, 3. IsCallable check, 4. GetIteratorDirect.
    /// </summary>
    [JsHostMethod("filter", Length = 1d)]
    public JsValue Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var predicate = RequireCallable(args, 0, "predicate", obj);
        var iterated = GetIteratorDirect(obj);

        return CreateFilteredIterator(iterated, predicate);
    }

    /// <summary>
    /// Iterator.prototype.take(limit) - Returns an iterator that yields at most limit values.
    /// Spec order: 1-2. RequireObject, 3. ToNumber(limit), 4. NaN check, 5. ToIntegerOrInfinity,
    ///             6. Range check, 7. GetIteratorDirect.
    /// </summary>
    [JsHostMethod("take", Length = 1d)]
    public JsValue Take(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);

        // Steps 3-6: ToNumber and validate BEFORE GetIteratorDirect
        var limitArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        double numLimit;
        try
        {
            var evalContext = Realm?.CreateContext();
            numLimit = JsOps.ToNumber(limitArg, evalContext);
            if (evalContext?.IsThrow == true)
            {
                IteratorCloseOnAbrupt(obj);
                throw new ThrowSignal(evalContext.FlowValue);
            }
        }
        catch (ThrowSignal)
        {
            IteratorCloseOnAbrupt(obj);
            throw;
        }
        catch
        {
            IteratorCloseOnAbrupt(obj);
            throw;
        }

        if (double.IsNaN(numLimit))
        {
            IteratorCloseOnAbrupt(obj);
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.take requires a non-negative number", null, Realm);
        }

        var limit = ToIntegerOrInfinity(numLimit);
        if (limit < 0)
        {
            IteratorCloseOnAbrupt(obj);
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.take requires a non-negative number", null, Realm);
        }

        // Step 7: GetIteratorDirect
        var iterated = GetIteratorDirect(obj);

        return CreateTakeIterator(iterated, limit);
    }

    /// <summary>
    /// Iterator.prototype.drop(limit) - Returns an iterator that skips the first limit values.
    /// Spec order: same as take.
    /// </summary>
    [JsHostMethod("drop", Length = 1d)]
    public JsValue Drop(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);

        var limitArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        double numLimit;
        try
        {
            var evalContext = Realm?.CreateContext();
            numLimit = JsOps.ToNumber(limitArg, evalContext);
            if (evalContext?.IsThrow == true)
            {
                IteratorCloseOnAbrupt(obj);
                throw new ThrowSignal(evalContext.FlowValue);
            }
        }
        catch (ThrowSignal)
        {
            IteratorCloseOnAbrupt(obj);
            throw;
        }
        catch
        {
            IteratorCloseOnAbrupt(obj);
            throw;
        }

        if (double.IsNaN(numLimit))
        {
            IteratorCloseOnAbrupt(obj);
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.drop requires a non-negative number", null, Realm);
        }

        var limit = ToIntegerOrInfinity(numLimit);
        if (limit < 0)
        {
            IteratorCloseOnAbrupt(obj);
            throw StandardLibrary.ThrowRangeError("Iterator.prototype.drop requires a non-negative number", null, Realm);
        }

        var iterated = GetIteratorDirect(obj);

        return CreateDropIterator(iterated, limit);
    }

    /// <summary>
    /// Iterator.prototype.flatMap(mapper) - Returns an iterator that maps and flattens values.
    /// </summary>
    [JsHostMethod("flatMap", Length = 1d)]
    public JsValue FlatMap(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var mapper = RequireCallable(args, 0, "mapper", obj);
        var iterated = GetIteratorDirect(obj);

        return CreateFlatMapIterator(iterated, mapper);
    }

    /// <summary>
    /// Iterator.prototype.reduce(reducer[, initialValue]) - Reduces iterator to a single value.
    /// Spec order: 1-2. RequireObject, 3. IsCallable check, 4. GetIteratorDirect.
    /// </summary>
    [JsHostMethod("reduce", Length = 1d)]
    public JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var reducer = RequireCallable(args, 0, "reducer", obj);
        var iterated = GetIteratorDirect(obj);

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
                try
                {
                    accumulator = reducer.Invoke(callArgs, JsValue.Undefined);
                }
                catch
                {
                    IteratorCloseOnAbrupt(iterated.Iterator);
                    throw;
                }
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.toArray() - Converts iterator to an array.
    /// Spec order: 1-2. RequireObject, 3. GetIteratorDirect.
    /// </summary>
    [JsHostMethod("toArray", Length = 0d)]
    public JsValue ToArray(JsValue thisValue)
    {
        var obj = RequireObject(thisValue);
        var iterated = GetIteratorDirect(obj);
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
    /// Iterator.prototype.forEach(fn) - Executes a callback for each value.
    /// </summary>
    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var fn = RequireCallable(args, 0, "fn", obj);
        var iterated = GetIteratorDirect(obj);
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
            try
            {
                fn.Invoke(callArgs, JsValue.Undefined);
            }
            catch
            {
                IteratorCloseOnAbrupt(iterated.Iterator);
                throw;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.some(predicate) - Returns true if any value satisfies the predicate.
    /// </summary>
    [JsHostMethod("some", Length = 1d)]
    public JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var predicate = RequireCallable(args, 0, "predicate", obj);
        var iterated = GetIteratorDirect(obj);
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
            JsValue result;
            try
            {
                result = predicate.Invoke(callArgs, JsValue.Undefined);
            }
            catch
            {
                IteratorCloseOnAbrupt(iterated.Iterator);
                throw;
            }

            if (JsOps.ToBoolean(result))
            {
                IteratorCloseNormal(iterated.Iterator);
                return true;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.every(predicate) - Returns true if all values satisfy the predicate.
    /// </summary>
    [JsHostMethod("every", Length = 1d)]
    public JsValue Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var predicate = RequireCallable(args, 0, "predicate", obj);
        var iterated = GetIteratorDirect(obj);
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
            JsValue result;
            try
            {
                result = predicate.Invoke(callArgs, JsValue.Undefined);
            }
            catch
            {
                IteratorCloseOnAbrupt(iterated.Iterator);
                throw;
            }

            if (!JsOps.ToBoolean(result))
            {
                IteratorCloseNormal(iterated.Iterator);
                return false;
            }

            counter++;
        }
    }

    /// <summary>
    /// Iterator.prototype.find(predicate) - Returns the first value that satisfies the predicate.
    /// </summary>
    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var obj = RequireObject(thisValue);
        var predicate = RequireCallable(args, 0, "predicate", obj);
        var iterated = GetIteratorDirect(obj);
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
            JsValue result;
            try
            {
                result = predicate.Invoke(callArgs, JsValue.Undefined);
            }
            catch
            {
                IteratorCloseOnAbrupt(iterated.Iterator);
                throw;
            }

            if (JsOps.ToBoolean(result))
            {
                IteratorCloseNormal(iterated.Iterator);
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
        if (thisValue.TryGetObjectLike(out var obj))
        {
            IteratorCloseNormal(obj);
        }

        return JsValue.Undefined;
    }

    #region Iterator Record

    /// <summary>
    /// ECMAScript Iterator Record: { [[Iterator]], [[NextMethod]], [[Done]] }.
    /// The NextMethod is captured once at GetIteratorDirect time and reused for all subsequent calls.
    /// </summary>
    internal sealed class IteratorRecord(IJsObjectLike iterator, JsValue nextMethod)
    {
        public readonly IJsObjectLike Iterator = iterator;
        public readonly JsValue NextMethod = nextMethod;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates that thisValue is an object.
    /// Spec: "1. Let O be the this value. 2. If O is not an Object, throw TypeError."
    /// </summary>
    private static IJsObjectLike RequireObject(JsValue value)
    {
        if (!value.TryGetObjectLike(out var obj))
        {
            throw StandardLibrary.ThrowTypeError("Iterator value is not an object", null, null);
        }

        return obj;
    }

    /// <summary>
    /// Validates that args[index] is callable. If not, closes the iterator and throws TypeError.
    /// Implements the argument-validation-failure-closes-underlying behavior.
    /// </summary>
    private static IJsCallable RequireCallable(IReadOnlyList<JsValue> args, int index, string name,
        IJsObjectLike iteratorObj)
    {
        var arg = args.Count > index ? args[index] : JsValue.Undefined;

        if (!arg.TryGetObject<IJsCallable>(out var callable))
        {
            // Per spec: when argument validation fails, close the underlying iterator
            IteratorCloseOnAbrupt(iteratorObj);
            throw StandardLibrary.ThrowTypeError($"{name} is not a function", null, null);
        }

        return callable;
    }

    /// <summary>
    /// GetIteratorDirect(obj) per ECMAScript spec:
    /// 1. Let nextMethod be ? Get(obj, "next").
    /// 2. Return Iterator Record { [[Iterator]]: obj, [[NextMethod]]: nextMethod, [[Done]]: false }.
    /// Does NOT check if nextMethod is callable -- that check is deferred to IteratorNext.
    /// </summary>
    private static IteratorRecord GetIteratorDirect(IJsObjectLike obj)
    {
        // Get "next" -- this can trigger getters and throw
        if (!obj.TryGetProperty("next", out var nextMethod))
        {
            nextMethod = JsValue.Undefined;
        }

        return new IteratorRecord(obj, nextMethod);
    }

    /// <summary>
    /// Converts a pre-computed ToNumber result to integer or infinity.
    /// </summary>
    private static double ToIntegerOrInfinity(double number)
    {
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
    /// IteratorNext: Calls the captured NextMethod from the IteratorRecord.
    /// Throws TypeError if next is not callable or result is not an object.
    /// Uses TryGetObjectLike to accept both JsObject and IteratorResultObject.
    /// </summary>
    private static IJsObjectLike IteratorNext(IteratorRecord iterated)
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
    /// IteratorStep: Calls IteratorNext, returns null if done.
    /// </summary>
    private static IJsObjectLike? IteratorStep(IteratorRecord iterated)
    {
        var result = IteratorNext(iterated);

        if (result.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
        {
            return null;
        }

        return result;
    }

    /// <summary>
    /// Gets the value property from an iterator result.
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
    /// IteratorClose for normal completion.
    /// Per spec: Get return method. If undefined, return. Otherwise call it.
    /// Errors from getting or calling return() propagate to the caller.
    /// </summary>
    internal static void IteratorCloseNormal(IJsObjectLike iterator)
    {
        if (!iterator.TryGetProperty("return", out var returnProp))
        {
            return;
        }

        if (returnProp.IsNullOrUndefined)
        {
            return;
        }

        if (!returnProp.TryGetObject<IJsCallable>(out var returnMethod))
        {
            throw StandardLibrary.ThrowTypeError("return is not a function", null, null);
        }

        returnMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
    }

    /// <summary>
    /// IteratorClose for abrupt completion.
    /// Attempts to call return() but suppresses errors (the original error takes priority).
    /// </summary>
    internal static void IteratorCloseOnAbrupt(IJsObjectLike iterator)
    {
        try
        {
            if (!iterator.TryGetProperty("return", out var returnProp))
            {
                return;
            }

            if (returnProp.IsNullOrUndefined)
            {
                return;
            }

            if (!returnProp.TryGetObject<IJsCallable>(out var returnMethod))
            {
                return;
            }

            returnMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
        }
        catch
        {
            // Suppress -- the original error takes priority
        }
    }

    #endregion

    #region Iterator State Helper

    /// <summary>
    /// Encapsulates common iterator state and behavior for iterator helper objects.
    /// </summary>
    private sealed class IteratorState(IteratorRecord iterated, IteratorPrototype prototype)
    {
        public bool Done;
        public bool IsExecuting;
        public IteratorRecord? InnerIterator;

        public void EnsureNotExecuting()
        {
            if (IsExecuting)
            {
                throw StandardLibrary.ThrowTypeError("Generator is already executing", null, prototype.Realm);
            }
        }

        public bool TryGetDoneResult(out JsValue result)
        {
            if (Done)
            {
                result = CreateIterResult(JsValue.Undefined, true);
                return true;
            }

            result = default;
            return false;
        }

        public void MarkDone()
        {
            Done = true;
        }

        public HostFunction CreateReturnFunc()
        {
            return new HostFunction((_, _) =>
            {
                EnsureNotExecuting();
                IsExecuting = true;
                try
                {
                    Done = true;
                    if (InnerIterator is not null)
                    {
                        IteratorCloseOnAbrupt(InnerIterator.Iterator);
                        InnerIterator = null;
                    }

                    // Forward to underlying iterator's return
                    IteratorCloseNormal(iterated.Iterator);
                    return CreateIterResult(JsValue.Undefined, true);
                }
                finally
                {
                    IsExecuting = false;
                }
            }, isConstructor: false);
        }
    }

    #endregion

    #region Iterator Factory Methods

    private JsValue CreateMappedIterator(IteratorRecord iterated, IJsCallable mapper)
    {
        var iterator = new JsObject { RealmState = Realm };
        var state = new IteratorState(iterated, this);
        var counter = 0;

        var nextFunc = new HostFunction((_, _) =>
        {
            state.EnsureNotExecuting();
            if (state.TryGetDoneResult(out var doneResult))
            {
                return doneResult;
            }

            state.IsExecuting = true;
            try
            {
                var next = IteratorStep(iterated);
                if (next is null)
                {
                    state.MarkDone();
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
                    state.MarkDone();
                    IteratorCloseOnAbrupt(iterated.Iterator);
                    throw;
                }
            }
            finally
            {
                state.IsExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)state.CreateReturnFunc());
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateFilteredIterator(IteratorRecord iterated, IJsCallable predicate)
    {
        var iterator = new JsObject { RealmState = Realm };
        var state = new IteratorState(iterated, this);
        var counter = 0;

        var nextFunc = new HostFunction((_, _) =>
        {
            state.EnsureNotExecuting();
            if (state.TryGetDoneResult(out var doneResult))
            {
                return doneResult;
            }

            state.IsExecuting = true;
            try
            {
                while (true)
                {
                    var next = IteratorStep(iterated);
                    if (next is null)
                    {
                        state.MarkDone();
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
                        state.MarkDone();
                        IteratorCloseOnAbrupt(iterated.Iterator);
                        throw;
                    }
                }
            }
            finally
            {
                state.IsExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)state.CreateReturnFunc());
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateTakeIterator(IteratorRecord iterated, double limit)
    {
        var iterator = new JsObject { RealmState = Realm };
        var state = new IteratorState(iterated, this);
        var remaining = limit;

        var nextFunc = new HostFunction((_, _) =>
        {
            state.EnsureNotExecuting();
            if (state.TryGetDoneResult(out var doneResult))
            {
                return doneResult;
            }

            state.IsExecuting = true;
            try
            {
                if (remaining <= 0)
                {
                    // take(0) or exhausted: close the underlying iterator via IteratorClose
                    state.MarkDone();
                    IteratorCloseNormal(iterated.Iterator);
                    return CreateIterResult(JsValue.Undefined, true);
                }

                remaining--;

                var next = IteratorStep(iterated);
                if (next is null)
                {
                    state.MarkDone();
                    return CreateIterResult(JsValue.Undefined, true);
                }

                var value = IteratorValue(next);

                if (remaining <= 0)
                {
                    // This was the last item, close the underlying iterator
                    state.MarkDone();
                    IteratorCloseNormal(iterated.Iterator);
                }

                return CreateIterResult(value, false);
            }
            finally
            {
                state.IsExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)state.CreateReturnFunc());
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateDropIterator(IteratorRecord iterated, double limit)
    {
        var iterator = new JsObject { RealmState = Realm };
        var state = new IteratorState(iterated, this);
        var remaining = limit;

        var nextFunc = new HostFunction((_, _) =>
        {
            state.EnsureNotExecuting();
            if (state.TryGetDoneResult(out var doneResult))
            {
                return doneResult;
            }

            state.IsExecuting = true;
            try
            {
                // Skip the first 'remaining' items
                while (remaining > 0)
                {
                    var skipped = IteratorStep(iterated);
                    if (skipped is null)
                    {
                        state.MarkDone();
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    remaining--;
                }

                var next = IteratorStep(iterated);
                if (next is null)
                {
                    state.MarkDone();
                    return CreateIterResult(JsValue.Undefined, true);
                }

                var value = IteratorValue(next);
                return CreateIterResult(value, false);
            }
            finally
            {
                state.IsExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)state.CreateReturnFunc());
        SetupIteratorPrototype(iterator);

        return new JsValue(iterator);
    }

    private JsValue CreateFlatMapIterator(IteratorRecord iterated, IJsCallable mapper)
    {
        var iterator = new JsObject { RealmState = Realm };
        var state = new IteratorState(iterated, this);
        var counter = 0;

        var nextFunc = new HostFunction((_, _) =>
        {
            state.EnsureNotExecuting();
            if (state.TryGetDoneResult(out var doneResult))
            {
                return doneResult;
            }

            state.IsExecuting = true;
            try
            {
                while (true)
                {
                    // If we have an inner iterator, try to get the next value from it
                    if (state.InnerIterator is not null)
                    {
                        IJsObjectLike innerResult;
                        try
                        {
                            innerResult = IteratorNext(state.InnerIterator);
                        }
                        catch
                        {
                            state.MarkDone();
                            IteratorCloseOnAbrupt(iterated.Iterator);
                            throw;
                        }

                        if (innerResult.TryGetProperty("done", out var innerDone) &&
                            JsOps.ToBoolean(innerDone))
                        {
                            // Inner iterator is exhausted
                            state.InnerIterator = null;
                        }
                        else
                        {
                            var innerValue = IteratorValue(innerResult);
                            return CreateIterResult(innerValue, false);
                        }
                    }

                    // Get the next value from the source iterator
                    var next = IteratorStep(iterated);
                    if (next is null)
                    {
                        state.MarkDone();
                        return CreateIterResult(JsValue.Undefined, true);
                    }

                    var value = IteratorValue(next);
                    var callArgs = new[] { value, (JsValue)(double)counter };
                    counter++;

                    JsValue mapped;
                    try
                    {
                        mapped = mapper.Invoke(callArgs, JsValue.Undefined);
                    }
                    catch
                    {
                        state.MarkDone();
                        IteratorCloseOnAbrupt(iterated.Iterator);
                        throw;
                    }

                    // GetIteratorFlattenable(mapped, reject-primitives)
                    // Step 1: If mapped is not an Object, throw TypeError
                    // Use TryGetObjectLike to accept IteratorResultObject and other IJsObjectLike types
                    if (!mapped.TryGetObjectLike(out var mappedObj))
                    {
                        state.MarkDone();
                        IteratorCloseOnAbrupt(iterated.Iterator);
                        throw StandardLibrary.ThrowTypeError(
                            "Iterator.prototype.flatMap mapper must return an object", null, Realm);
                    }

                    // Step 2: Check for Symbol.iterator
                    IteratorRecord innerIterated;
                    try
                    {
                        if (mappedObj.TryGetProperty(SymbolKeys.Iterator, out var iterMethod))
                        {
                            if (iterMethod.IsNullOrUndefined)
                            {
                                // Symbol.iterator is null/undefined -- fall back to treating as iterator
                                innerIterated = GetIteratorDirect(mappedObj);
                            }
                            else if (iterMethod.TryGetObject<IJsCallable>(out var iterCallable))
                            {
                                // Symbol.iterator is callable -- call it
                                var iterResult = iterCallable.Invoke([], mapped);
                                if (!iterResult.TryGetObjectLike(out var innerObj))
                                {
                                    state.MarkDone();
                                    IteratorCloseOnAbrupt(iterated.Iterator);
                                    throw StandardLibrary.ThrowTypeError(
                                        "Symbol.iterator must return an object", null, Realm);
                                }

                                innerIterated = GetIteratorDirect(innerObj);
                            }
                            else
                            {
                                // Symbol.iterator is present but not callable and not null/undefined
                                state.MarkDone();
                                IteratorCloseOnAbrupt(iterated.Iterator);
                                throw StandardLibrary.ThrowTypeError(
                                    "Symbol.iterator is not a function", null, Realm);
                            }
                        }
                        else
                        {
                            // No Symbol.iterator -- treat the object as an iterator-like
                            innerIterated = GetIteratorDirect(mappedObj);
                        }
                    }
                    catch
                    {
                        state.MarkDone();
                        IteratorCloseOnAbrupt(iterated.Iterator);
                        throw;
                    }

                    state.InnerIterator = innerIterated;
                }
            }
            finally
            {
                state.IsExecuting = false;
            }
        }, isConstructor: false);

        iterator.SetProperty("next", (JsValue)nextFunc);
        iterator.SetProperty("return", (JsValue)state.CreateReturnFunc());
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
