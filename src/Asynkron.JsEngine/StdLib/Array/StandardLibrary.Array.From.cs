using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateArrayConstructor(RealmState realm)
    {
        return ArrayConstructor.CreateConstructor(realm);
    }

    internal static object? ArrayOf(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        const string MethodName = "Array.of";
        var len = args.Count;
        IJsObjectLike result;

        if (thisValue.TryGetObject<IJsCallable>(out var callable) && JsOps.IsConstructor(callable))
        {
            var constructorRealm = GetConstructorRealm(callable, realm) ?? realm;
            if (constructorRealm is null)
            {
                throw new InvalidOperationException($"{MethodName} requires an active realm.");
            }

            // Use Construct to properly invoke the constructor with new.target semantics
            var constructed = Construct(callable, [(double)len], callable, constructorRealm);
            result = constructed.TryGetObject<IJsObjectLike>(out var constructedObj) ? constructedObj : null!;
            if (result is null)
            {
                throw ThrowTypeError($"{MethodName}: constructor did not return an object", realm: realm);
            }
        }
        else
        {
            result = CreateDefaultArrayInstance(len, realm);
        }

        for (var k = 0; k < len; k++)
        {
            var key = ToIndexString(k);
            CreateDataPropertyOrThrow(result, key, args[k], realm, MethodName);
        }

        SetArrayLikeLength(result, len);
        return result;

        static IJsObjectLike CreateDefaultArrayInstance(int length, RealmState? realm)
        {
            var arr = new JsArray(realm);
            arr.SetProperty("length", (double)length);
            return arr;
        }
    }

    internal static object? ArrayFrom(HostFunction host, JsValue thisValue, IReadOnlyList<JsValue> args,
        RealmState? realm)
    {
        const string MethodName = "Array.from";

        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            throw ThrowTypeError("Array.from requires an array-like or iterable", realm: realm);
        }

        var items = args[0];
        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !mapperCandidate.IsUndefined;
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (!mapperCandidate.TryGetObject<IJsCallable>(out var callableMapper))
            {
                throw ThrowTypeError("Array.from: when provided, the mapping callback must be callable", realm: realm);
            }

            mapper = callableMapper;
        }

        if (TryGetCallableMethod(items, SymbolIteratorKey, MethodName, realm, out var iteratorMethod))
        {
            return ArrayFromIterable(host, thisValue, items, iteratorMethod!, mapper, mapping, thisArg, realm);
        }

        var arrayLike = ToPropertyAccessor(items, MethodName, realm);
        var initialLengthValue = arrayLike.TryGetProperty("length", out var initialLenVal) ? initialLenVal : 0d;
        var initialLength = (long)ToLengthOrZero(initialLengthValue);
        if (initialLength > MaxConcreteArrayLength)
        {
            throw ThrowRangeError("Array.from result exceeds 2^32 - 1 elements", realm: realm);
        }

        var result = CreateArrayFromResult(thisValue, realm, initialLength, true, MethodName);

        long k = 0;
        while (true)
        {
            var lengthValue = arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var dynamicLength = (long)ToLengthOrZero(lengthValue);
            if (dynamicLength > MaxConcreteArrayLength)
            {
                throw ThrowRangeError("Array.from result exceeds 2^32 - 1 elements", realm: realm);
            }
            if (k >= dynamicLength)
            {
                break;
            }

            if (k >= MaxConcreteArrayLength)
            {
                throw ThrowTypeError("Array.from result exceeds 2^32 - 1 elements", realm: realm);
            }

            var key = ToIndexString(k);
            var value = GetElementOrUndefined(arrayLike, key);
            JsValue mapped;
            if (mapping && mapper is not null)
            {
                var mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, value, k);
                // Handle case where result is already a boxed JsValue
                mapped = mapperResult is JsValue mrJs ? mrJs : JsValue.FromObjectUnsafe(mapperResult);
            }
            else
            {
                // Handle case where value is already a boxed JsValue
                mapped = value is JsValue vJs ? vJs : JsValue.FromObjectUnsafe(value);
            }
            CreateDataPropertyOrThrow(result, key, mapped, realm, MethodName);
            k++;
        }

        SetArrayLikeLength(result, k);
        return result;
    }

    internal static object? ArrayFromAsync(HostFunction host, JsValue thisValue, IReadOnlyList<JsValue> args,
        RealmState? realm)
    {
        const string MethodName = "Array.fromAsync";
        if (realm?.Engine is null)
        {
            throw new InvalidOperationException("Array.fromAsync requires an active engine.");
        }

        var engine = realm.Engine;
        var promise = new JsPromise(engine);
        AddPromiseInstanceMethods(promise.JsObject, promise, engine);

        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            promise.Reject(JsValue.FromObjectUnsafe(CreateTypeError("Array.fromAsync requires an array-like or iterable", realm: realm)));
            return promise.JsObject;
        }

        var items = args[0];
        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !mapperCandidate.IsUndefined;
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (!mapperCandidate.TryGetObject<IJsCallable>(out var callableMapper))
            {
                promise.Reject(JsValue.FromObjectUnsafe(CreateTypeError("Array.fromAsync: when provided, the mapping callback must be callable",
                    realm: realm)));
                return promise.JsObject;
            }

            mapper = callableMapper;
        }

        IJsObjectLike result;
        try
        {
            result = CreateArrayFromResult(thisValue, realm, 0, false, MethodName);
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue);
            return promise.JsObject;
        }

        try
        {
            var operation = new ArrayFromAsyncOperation(host, realm, promise, result, mapping, mapper, thisArg,
                MethodName);
            if (TryGetCallableMethod(items, SymbolAsyncIteratorKey, MethodName, realm, out var asyncIterator) &&
                asyncIterator is not null)
            {
                operation.StartIterator(items, asyncIterator, true);
            }
            else if (TryGetCallableMethod(items, SymbolIteratorKey, MethodName, realm, out var syncIterator) &&
                     syncIterator is not null)
            {
                operation.StartIterator(items, syncIterator, false);
            }
            else
            {
                var arrayLike = ToPropertyAccessor(items, MethodName, realm);
                operation.StartArrayLike(arrayLike);
            }
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue);
        }

        return promise.JsObject;
    }

    internal static object? ArrayFromIterable(HostFunction host, JsValue thisValue, object? items,
        IJsCallable iteratorMethod, IJsCallable? mapper, bool mapping, object? thisArg, RealmState? realm)
    {
        const string MethodName = "Array.from";
        var result = CreateArrayFromResult(thisValue, realm, 0, false, MethodName);
        // Handle case where items is already a boxed JsValue
        var itemsArg = items is JsValue itemsJs ? itemsJs : JsValue.FromObjectUnsafe(items);
        var iteratorValue = iteratorMethod.Invoke([], itemsArg);
        if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iterator))
        {
            throw ThrowTypeError("Array.from iterator method did not return an object", realm: realm);
        }

        if (!iterator.TryGetProperty("next", out var nextVal))
        {
            throw ThrowTypeError("Array.from iterator does not expose a callable next()", realm: realm);
        }
        // nextVal is already a JsValue from TryGetProperty
        if (!nextVal.TryGetObject<IJsCallable>(out var nextFn))
        {
            throw ThrowTypeError("Array.from iterator does not expose a callable next()", realm: realm);
        }
        long k = 0;

        while (true)
        {
            JsValue step;
            try
            {
                step = nextFn.Invoke([], JsValue.FromObjectUnsafe(iterator));
            }
            catch (ThrowSignal)
            {
                IteratorClose(iterator, realm, MethodName);
                throw;
            }

            if (!step.TryGetObject<IJsPropertyAccessor>(out var stepAccessor))
            {
                IteratorClose(iterator, realm, MethodName);
                throw ThrowTypeError("Array.from iterator result is not an object", realm: realm);
            }

            var done = stepAccessor.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue);
            if (done)
            {
                SetArrayLikeLength(result, k);
                return result;
            }

            if (k >= MaxConcreteArrayLength)
            {
                IteratorClose(iterator, realm, MethodName);
                throw ThrowTypeError("Array.from result exceeds 2^32 - 1 elements", realm: realm);
            }

            // entryValue is already a JsValue from TryGetProperty
            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : JsValue.Undefined;
            var mappedValue = value;
            if (mapping && mapper is not null)
            {
                try
                {
                    var mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, value, k);
                    // Handle case where result is already a boxed JsValue
                    mappedValue = mapperResult is JsValue mrJs ? mrJs : JsValue.FromObjectUnsafe(mapperResult);
                }
                catch (ThrowSignal)
                {
                    IteratorClose(iterator, realm, MethodName);
                    throw;
                }
            }

            try
            {
                CreateDataPropertyOrThrow(result, ToIndexString(k), mappedValue, realm, MethodName);
            }
            catch (ThrowSignal)
            {
                IteratorClose(iterator, realm, MethodName);
                throw;
            }

            k++;
        }
    }

    internal static bool TryAwaitPromiseLike(object? candidate, RealmState? realm, Action<object?> onFulfilled,
        Action<object?> onRejected)
    {
        var jsCandidate = candidate is JsValue value ? value : JsValue.FromObjectUnsafe(candidate);

        // Handle internal JsPromise instances (both raw and wrapped)
        if (jsCandidate.ObjectValue is JsPromise directPromise)
        {
            AttachHandlers(directPromise);
            return true;
        }

        if (JsPromise.TryGetInternalPromise(jsCandidate, out var wrappedPromise) && wrappedPromise is not null)
        {
            AttachHandlers(wrappedPromise);
            return true;
        }

        // Handle generic thenables (objects with a callable "then")
        if (jsCandidate.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("then", out var thenVal) &&
            thenVal.TryGetObject<IJsCallable>(out var thenCallable))
        {
            try
            {
                thenCallable.Invoke(
                    [
                        (JsValue)new HostFunction(args =>
                        {
                            onFulfilled(args.Count > 0 ? args[0].ToObject() : null);
                            return JsValue.Undefined;
                        }, isConstructor: false),
                        (JsValue)new HostFunction(args =>
                        {
                            onRejected(args.Count > 0 ? args[0].ToObject() : null);
                            return JsValue.Undefined;
                        }, isConstructor: false)
                    ],
                    JsValue.FromObjectUnsafe(accessor));
                return true;
            }
            catch (ThrowSignal signal)
            {
                onRejected(signal.ThrownValue.ToObject());
                return true;
            }
        }

        return false;

        void AttachHandlers(JsPromise promise)
        {
            var resolveFn = new HostFunction(args =>
            {
                onFulfilled(args.Count > 0 ? args[0].ToObject() : null);
                return JsValue.Undefined;
            }, isConstructor: false);
            var rejectFn = new HostFunction(args =>
            {
                onRejected(args.Count > 0 ? args[0].ToObject() : null);
                return JsValue.Undefined;
            }, isConstructor: false);

            promise.Then(resolveFn, rejectFn);
        }
    }

    internal static object? InvokeArrayFromMapper(IJsCallable mapper, HostFunction host, object? thisArg, object? value,
        long index)
    {
        if (mapper is IJsEnvironmentAwareCallable envAware && host.CallingJsEnvironment is not null)
        {
            envAware.CallingJsEnvironment = host.CallingJsEnvironment;
        }

        // Handle case where value and thisArg are already boxed JsValues
        var valueArg = value is JsValue vJs ? vJs : JsValue.FromObjectUnsafe(value);
        var thisArgJs = thisArg is JsValue taJs ? taJs : JsValue.FromObjectUnsafe(thisArg);
        return mapper.Invoke([valueArg, (double)index], thisArgJs).ToObject();
    }

    private sealed class ArrayFromAsyncOperation(
        HostFunction host,
        RealmState realm,
        JsPromise promise,
        IJsObjectLike result,
        bool mapping,
        IJsCallable? mapper,
        object? thisArg,
        string methodName)
    {
        private long _index;
        private bool _settled;
        private IJsPropertyAccessor? _iterator;
        private IJsCallable? _nextFn;
        private bool _awaitIteratorResult;
        private IJsPropertyAccessor? _arrayLike;

        public void StartIterator(object? items, IJsCallable iteratorMethod, bool awaitIteratorResult)
        {
            JsValue iteratorValue;
            try
            {
                // Handle case where items is already a boxed JsValue
                var itemsArg = items is JsValue itemsJs ? itemsJs : JsValue.FromObjectUnsafe(items);
                iteratorValue = iteratorMethod.Invoke([], itemsArg);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iterator))
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator method did not return an object", null,
                    realm));
                return;
            }

            if (!iterator.TryGetProperty("next", out var nextVal))
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator does not expose a callable next()", null,
                    realm));
                return;
            }
            // nextVal is already a JsValue from TryGetProperty
            if (!nextVal.TryGetObject<IJsCallable>(out var nextFn))
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator does not expose a callable next()", null,
                    realm));
                return;
            }

            _iterator = iterator;
            _nextFn = nextFn;
            _awaitIteratorResult = awaitIteratorResult;
            ProcessIteratorStep();
        }

        public void StartArrayLike(IJsPropertyAccessor arrayLike)
        {
            _arrayLike = arrayLike;
            ProcessArrayLike();
        }

        private void ProcessIteratorStep()
        {
            while (!_settled && _iterator is not null && _nextFn is not null)
            {
                JsValue step;
                try
                {
                    step = _nextFn.Invoke([], JsValue.FromObjectUnsafe(_iterator));
                }
                catch (ThrowSignal signal)
                {
                    RejectSignal(signal);
                    return;
                }

                var shouldContinue = HandleIteratorStep(step.ToObject());
                if (!shouldContinue)
                {
                    return;
                }
            }
        }

        private bool HandleIteratorStep(object? stepCandidate)
        {
            if (_settled)
            {
                return false;
            }

            if (_awaitIteratorResult && TryAwaitPromiseLike(stepCandidate, realm,
                    resolved =>
                    {
                        if (HandleIteratorStep(resolved))
                        {
                            ProcessIteratorStep();
                        }
                    },
                    RejectWithClose))
            {
                return false;
            }

            if (stepCandidate is not IJsPropertyAccessor stepAccessor)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync iterator result is not an object", null, realm));
                return false;
            }

            var done = stepAccessor.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue);
            if (done)
            {
                ResolveSuccess();
                return false;
            }

            if (_index >= MaxConcreteArrayLength)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                return false;
            }

            // entryValue is already a JsValue from TryGetProperty
            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : JsValue.Undefined;
            if (TryAwaitPromiseLike(value, realm,
                    resolved =>
                    {
                        if (HandleIteratorValue(resolved))
                        {
                            ProcessIteratorStep();
                        }
                    },
                    RejectWithClose))
            {
                return false;
            }

            return HandleIteratorValue(value);
        }

        private bool HandleIteratorValue(object? value)
        {
            if (_settled)
            {
                return false;
            }

            if (mapping && mapper is not null)
            {
                object? mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, value, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectWithClose(signal.ThrownValue.ToObject());
                    return false;
                }

                if (TryAwaitPromiseLike(mapperResult, realm,
                        resolved =>
                        {
                            if (StoreIteratorValue(resolved))
                            {
                                ProcessIteratorStep();
                            }
                        },
                        RejectWithClose))
                {
                    return false;
                }

                return StoreIteratorValue(mapperResult);
            }

            return StoreIteratorValue(value);
        }

        private bool StoreIteratorValue(object? value)
        {
            if (_settled)
            {
                return false;
            }

            try
            {
                CreateDataPropertyOrThrow(result, ToIndexString(_index), value, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectWithClose(signal.ThrownValue.ToObject());
                return false;
            }

            _index++;
            return true;
        }

        private void ProcessArrayLike()
        {
            while (!_settled && _arrayLike is not null)
            {
                var lengthValue = _arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
                var dynamicLength = (long)ToLengthOrZero(lengthValue);
                if (dynamicLength > MaxConcreteArrayLength)
                {
                    RejectFailure(CreateRangeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                    return;
                }

                if (_index >= dynamicLength)
                {
                    ResolveSuccess();
                    return;
                }

                if (_index >= MaxConcreteArrayLength)
                {
                    RejectFailure(CreateTypeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                    return;
                }

                var key = ToIndexString(_index);
                var value = GetElementOrUndefined(_arrayLike, key);
                if (TryAwaitPromiseLike(value, realm,
                        resolved =>
                        {
                            if (HandleArrayLikeValue(key, resolved))
                            {
                                ProcessArrayLike();
                            }
                        },
                        RejectFailure))
                {
                    return;
                }

                if (!HandleArrayLikeValue(key, value))
                {
                    return;
                }
            }
        }

        private bool HandleArrayLikeValue(string key, object? resolved)
        {
            if (_settled)
            {
                return false;
            }

            var finalValue = resolved;

            if (mapping && mapper is not null)
            {
                object? mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, resolved, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailure(signal.ThrownValue.ToObject());
                    return false;
                }

                if (TryAwaitPromiseLike(mapperResult, realm,
                        mapped =>
                        {
                            if (CommitArrayLikeValue(key, mapped))
                            {
                                ProcessArrayLike();
                            }
                        },
                        RejectFailure))
                {
                    return false;
                }

                finalValue = mapperResult;
            }

            return CommitArrayLikeValue(key, finalValue);
        }

        private bool CommitArrayLikeValue(string key, object? finalValue)
        {
            if (_settled)
            {
                return false;
            }

            try
            {
                CreateDataPropertyOrThrow(result, key, finalValue, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectFailure(signal.ThrownValue.ToObject());
                return false;
            }

            _index++;
            return true;
        }

        private void RejectSignal(ThrowSignal signal)
        {
            RejectFailure(signal.ThrownValue.ToObject());
        }

        private void RejectFailure(object? reason)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            promise.Reject(JsValue.FromObjectUnsafe(reason));
        }

        private void RejectWithClose(object? reason)
        {
            if (_iterator is not null)
            {
                try
                {
                    IteratorClose(_iterator, realm, methodName);
                }
                catch (ThrowSignal signal)
                {
                    reason = signal.ThrownValue.ToObject();
                }
            }

            RejectFailure(reason);
        }

        private void ResolveSuccess()
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            promise.Resolve(JsValue.FromObjectUnsafe(result));
        }
    }
}
