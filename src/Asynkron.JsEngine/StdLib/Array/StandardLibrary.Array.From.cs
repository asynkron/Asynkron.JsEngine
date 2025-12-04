using System;
using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static HostFunction CreateArrayConstructor(RealmState realm)
    {
        return ArrayConstructor.CreateConstructor(realm);
    }

    internal static object? ArrayOf(HostFunction host, object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.of";
        var len = args.Count;
        IJsObjectLike result;

        if (thisValue is IJsCallable callable && JsOps.IsConstructor(callable))
        {
            var constructorRealm = GetConstructorRealm(callable, realm) ?? realm;
            var receiver = CreateArrayLikeReceiverForConstructor(callable, constructorRealm, len);
            var constructed = callable.Invoke([(double)len], receiver);
            result = constructed as IJsObjectLike ?? receiver;
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

    internal static object? ArrayFrom(HostFunction host, object? thisValue, IReadOnlyList<object?> args,
        RealmState? realm)
    {
        const string MethodName = "Array.from";

        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            throw ThrowTypeError("Array.from requires an array-like or iterable", realm: realm);
        }

        var items = args[0];
        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !ReferenceEquals(mapperCandidate, Symbol.Undefined);
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (mapperCandidate is not IJsCallable callableMapper)
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
            var mapped = mapping && mapper is not null
                ? InvokeArrayFromMapper(mapper, host, thisArg, value, k)
                : value;
            CreateDataPropertyOrThrow(result, key, mapped, realm, MethodName);
            k++;
        }

        SetArrayLikeLength(result, k);
        return result;
    }

    internal static object? ArrayFromAsync(HostFunction host, object? thisValue, IReadOnlyList<object?> args,
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

        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            promise.Reject(CreateTypeError("Array.fromAsync requires an array-like or iterable", realm: realm));
            return promise.JsObject;
        }

        var items = args[0];
        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !ReferenceEquals(mapperCandidate, Symbol.Undefined);
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (mapperCandidate is not IJsCallable callableMapper)
            {
                promise.Reject(CreateTypeError("Array.fromAsync: when provided, the mapping callback must be callable",
                    realm: realm));
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
            promise.Reject(signal.ThrownValue ?? signal);
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
            promise.Reject(signal.ThrownValue ?? signal);
        }

        return promise.JsObject;
    }

    internal static object? ArrayFromIterable(HostFunction host, object? thisValue, object? items,
        IJsCallable iteratorMethod, IJsCallable? mapper, bool mapping, object? thisArg, RealmState? realm)
    {
        const string MethodName = "Array.from";
        var result = CreateArrayFromResult(thisValue, realm, 0, false, MethodName);
        var iteratorValue = iteratorMethod.Invoke([], items);
        if (iteratorValue is not IJsPropertyAccessor iterator)
        {
            throw ThrowTypeError("Array.from iterator method did not return an object", realm: realm);
        }

        if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable nextFn)
        {
            throw ThrowTypeError("Array.from iterator does not expose a callable next()", realm: realm);
        }
        long k = 0;

        while (true)
        {
            object? step;
            try
            {
                step = nextFn.Invoke(Array.Empty<object?>(), iterator);
            }
            catch (ThrowSignal)
            {
                IteratorClose(iterator, realm, MethodName);
                throw;
            }

            if (step is not IJsPropertyAccessor stepAccessor)
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

            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : Symbol.Undefined;
            var mappedValue = value;
            if (mapping && mapper is not null)
            {
                try
                {
                    mappedValue = InvokeArrayFromMapper(mapper, host, thisArg, value, k);
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
        if (candidate is JsObject jsObject &&
            jsObject.TryGetProperty("then", out var thenValue) &&
            thenValue is IJsCallable thenCallable)
        {
            try
            {
                thenCallable.Invoke(
                    [
                        new HostFunction((_, args) =>
                        {
                            onFulfilled(args.Count > 0 ? args[0] : null);
                            return null;
                        }, isConstructor: false),
                        new HostFunction((_, args) =>
                        {
                            onRejected(args.Count > 0 ? args[0] : null);
                            return null;
                        }, isConstructor: false)
                    ],
                    jsObject);
                return true;
            }
            catch (ThrowSignal signal)
            {
                onRejected(signal.ThrownValue ?? signal);
                return true;
            }
        }

        return false;
    }

    internal static object? InvokeArrayFromMapper(IJsCallable mapper, HostFunction host, object? thisArg, object? value,
        long index)
    {
        if (mapper is IJsEnvironmentAwareCallable envAware && host.CallingJsEnvironment is not null)
        {
            envAware.CallingJsEnvironment = host.CallingJsEnvironment;
        }

        return mapper.Invoke([value, (double)index], thisArg);
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
            object? iteratorValue;
            try
            {
                iteratorValue = iteratorMethod.Invoke(Array.Empty<object?>(), items);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            if (iteratorValue is not IJsPropertyAccessor iterator)
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator method did not return an object", null,
                    realm));
                return;
            }

            if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable nextFn)
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
            if (_settled || _iterator is null || _nextFn is null)
            {
                return;
            }

            object? step;
            try
            {
                step = _nextFn.Invoke(Array.Empty<object?>(), _iterator);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            HandleIteratorStep(step);
        }

        private void HandleIteratorStep(object? stepCandidate)
        {
            if (_settled)
            {
                return;
            }

            if (_awaitIteratorResult && TryAwaitPromiseLike(stepCandidate, realm, HandleIteratorStep,
                    RejectWithClose))
            {
                return;
            }

            if (stepCandidate is not IJsPropertyAccessor stepAccessor)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync iterator result is not an object", null, realm));
                return;
            }

            var done = stepAccessor.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue);
            if (done)
            {
                ResolveSuccess();
                return;
            }

            if (_index >= MaxConcreteArrayLength)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                return;
            }

            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : Symbol.Undefined;
            if (TryAwaitPromiseLike(value, realm, HandleIteratorValue, RejectWithClose))
            {
                return;
            }

            HandleIteratorValue(value);
        }

        private void HandleIteratorValue(object? value)
        {
            if (_settled)
            {
                return;
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
                    RejectWithClose(signal.ThrownValue ?? signal);
                    return;
                }

                if (TryAwaitPromiseLike(mapperResult, realm, StoreIteratorValue, RejectWithClose))
                {
                    return;
                }

                StoreIteratorValue(mapperResult);
                return;
            }

            StoreIteratorValue(value);
        }

        private void StoreIteratorValue(object? value)
        {
            if (_settled)
            {
                return;
            }

            try
            {
                CreateDataPropertyOrThrow(result, ToIndexString(_index), value, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectWithClose(signal.ThrownValue ?? signal);
                return;
            }

            _index++;
            ProcessIteratorStep();
        }

        private void ProcessArrayLike()
        {
            if (_settled || _arrayLike is null)
            {
                return;
            }

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
            if (TryAwaitPromiseLike(value, realm, resolved => HandleArrayLikeValue(key, resolved), RejectFailure))
            {
                return;
            }

            HandleArrayLikeValue(key, value);
        }

        private void HandleArrayLikeValue(string key, object? resolved)
        {
            if (_settled)
            {
                return;
            }

            void FinalizeValue(object? finalValue)
            {
                if (_settled)
                {
                    return;
                }

                try
                {
                    CreateDataPropertyOrThrow(result, key, finalValue, realm, methodName);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailure(signal.ThrownValue ?? signal);
                    return;
                }

                _index++;
                ProcessArrayLike();
            }

            if (mapping && mapper is not null)
            {
                object? mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, resolved, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailure(signal.ThrownValue ?? signal);
                    return;
                }

                if (TryAwaitPromiseLike(mapperResult, realm, FinalizeValue, RejectFailure))
                {
                    return;
                }

                FinalizeValue(mapperResult);
                return;
            }

            FinalizeValue(resolved);
        }

        private void RejectSignal(ThrowSignal signal)
        {
            RejectFailure(signal.ThrownValue ?? signal);
        }

        private void RejectFailure(object? reason)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            promise.Reject(reason);
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
                    reason = signal.ThrownValue ?? signal;
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
            promise.Resolve(result);
        }
    }
}
