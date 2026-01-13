#region

using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.JsArrayConstants;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static object? ArrayOf(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        const string MethodName = "Array.of";
        var len = args.Count;
        IJsObjectLike result;

        if (thisValue.TryGetObject<IJsCallable>(out var callable) &&
            JsOps.IsConstructor(JsValue.FromObjectUnsafe(callable)))
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
        var evalContext = realm?.CreateContext();
        var initialLengthValue = arrayLike.TryGetProperty("length", out var initialLenVal) ? initialLenVal : JsValue.FromDouble(0d);
        var initialLength = (long)ToLengthOrZero(initialLengthValue, evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        if (initialLength > MaxConcreteArrayLength)
        {
            throw ThrowRangeError("Array.from result exceeds 2^32 - 1 elements", realm: realm);
        }

        var result = CreateArrayFromResult(thisValue, realm, initialLength, true, MethodName);

        long k = 0;
        while (true)
        {
            var lengthValue = arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
            var dynamicLength = (long)ToLengthOrZero(lengthValue, evalContext);
            if (evalContext?.IsThrow == true)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

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
            var value = GetElementOrUndefinedJsValue(arrayLike, key);
            var mapped = mapping && mapper is not null
                ? InvokeArrayFromMapper(mapper, host, thisArg, value, k)
                : value;

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
            promise.Reject(CreateTypeError("Array.fromAsync requires an array-like or iterable", realm: realm));
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

    internal static object? ArrayFromIterable(HostFunction host, JsValue thisValue, JsValue items,
        IJsCallable iteratorMethod, IJsCallable? mapper, bool mapping, JsValue thisArg, RealmState? realm)
    {
        const string MethodName = "Array.from";
        var result = CreateArrayFromResult(thisValue, realm, 0, false, MethodName);
        var iteratorValue = iteratorMethod.Invoke([], items);
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

    internal static bool TryAwaitPromiseLikeJsValue(JsValue candidate, Action<JsValue> onFulfilled,
        Action<JsValue> onRejected)
    {
        // Handle internal JsPromise instances (both raw and wrapped)
        if (candidate.ObjectValue is JsPromise directPromise)
        {
            AttachHandlers(directPromise);
            return true;
        }

        if (JsPromise.TryGetInternalPromise(candidate, out var wrappedPromise) && wrappedPromise is not null)
        {
            AttachHandlers(wrappedPromise);
            return true;
        }

        // Handle generic thenables (objects with a callable "then")
        if (candidate.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("then", out var thenVal) &&
            thenVal.TryGetObject<IJsCallable>(out var thenCallable))
        {
            try
            {
                thenCallable.Invoke(
                    [
                        (JsValue)new HostFunction(args =>
                        {
                            onFulfilled(args.Count > 0 ? args[0] : JsValue.Undefined);
                            return JsValue.Undefined;
                        }, isConstructor: false),
                        (JsValue)new HostFunction(args =>
                        {
                            onRejected(args.Count > 0 ? args[0] : JsValue.Undefined);
                            return JsValue.Undefined;
                        }, isConstructor: false)
                    ],
                    JsValue.FromObjectUnsafe(accessor));
                return true;
            }
            catch (ThrowSignal signal)
            {
                onRejected(signal.ThrownValue);
                return true;
            }
        }

        return false;

        void AttachHandlers(JsPromise promise)
        {
            var resolveFn = new HostFunction(args =>
            {
                onFulfilled(args.Count > 0 ? args[0] : JsValue.Undefined);
                return JsValue.Undefined;
            }, isConstructor: false);
            var rejectFn = new HostFunction(args =>
            {
                onRejected(args.Count > 0 ? args[0] : JsValue.Undefined);
                return JsValue.Undefined;
            }, isConstructor: false);

            promise.Then(resolveFn, rejectFn);
        }
    }

    internal static JsValue InvokeArrayFromMapper(IJsCallable mapper, HostFunction host, JsValue thisArg, JsValue value,
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
        JsValue thisArg,
        string methodName)
    {
        private IJsPropertyAccessor? _arrayLike;
        private bool _awaitIteratorResult;
        private long _index;
        private IJsPropertyAccessor? _iterator;
        private IJsCallable? _nextFn;
        private bool _settled;

        public void StartIterator(JsValue items, IJsCallable iteratorMethod, bool awaitIteratorResult)
        {
            JsValue iteratorValue;
            try
            {
                iteratorValue = iteratorMethod.Invoke([], items);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            if (!iteratorValue.TryGetObject<IJsPropertyAccessor>(out var iterator))
            {
                RejectFailureJsValue(CreateTypeError("Array.fromAsync iterator method did not return an object", null,
                    realm));
                return;
            }

            if (!iterator.TryGetProperty("next", out var nextVal))
            {
                RejectFailureJsValue(CreateTypeError("Array.fromAsync iterator does not expose a callable next()", null,
                    realm));
                return;
            }

            // nextVal is already a JsValue from TryGetProperty
            if (!nextVal.TryGetObject<IJsCallable>(out var nextFn))
            {
                RejectFailureJsValue(CreateTypeError("Array.fromAsync iterator does not expose a callable next()", null,
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

                var shouldContinue = HandleIteratorStepJsValue(step);
                if (!shouldContinue)
                {
                    return;
                }
            }
        }

        private bool HandleIteratorStepJsValue(JsValue stepCandidate)
        {
            if (_settled)
            {
                return false;
            }

            if (_awaitIteratorResult && TryAwaitPromiseLikeJsValue(stepCandidate,
                    resolved =>
                    {
                        if (HandleIteratorStepJsValue(resolved))
                        {
                            ProcessIteratorStep();
                        }
                    },
                    RejectWithCloseJsValue))
            {
                return false;
            }

            if (!stepCandidate.TryGetObject<IJsPropertyAccessor>(out var stepAccessor))
            {
                RejectWithCloseJsValue(
                    CreateTypeError("Array.fromAsync iterator result is not an object", null,
                        realm));
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
                RejectWithCloseJsValue(
                    CreateTypeError("Array.fromAsync result exceeds 2^32 - 1 elements", null,
                        realm));
                return false;
            }

            // entryValue is already a JsValue from TryGetProperty
            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : JsValue.Undefined;
            if (TryAwaitPromiseLikeJsValue(value,
                    resolved =>
                    {
                        if (HandleIteratorValueJsValue(resolved))
                        {
                            ProcessIteratorStep();
                        }
                    },
                    RejectWithCloseJsValue))
            {
                return false;
            }

            return HandleIteratorValueJsValue(value);
        }

        private bool HandleIteratorValueJsValue(JsValue value)
        {
            if (_settled)
            {
                return false;
            }

            if (mapping && mapper is not null)
            {
                JsValue mapperResult;
                try
                {
                    // InvokeArrayFromMapper handles JsValue by checking if the value is already a boxed JsValue
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, value, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectWithCloseJsValue(signal.ThrownValue);
                    return false;
                }

                if (TryAwaitPromiseLikeJsValue(mapperResult,
                        resolved =>
                        {
                            if (StoreIteratorValueJsValue(resolved))
                            {
                                ProcessIteratorStep();
                            }
                        },
                        RejectWithCloseJsValue))
                {
                    return false;
                }

                return StoreIteratorValueJsValue(mapperResult);
            }

            return StoreIteratorValueJsValue(value);
        }

        private bool StoreIteratorValueJsValue(JsValue value)
        {
            if (_settled)
            {
                return false;
            }

            try
            {
                CreateDataPropertyOrThrowJsValue(result, ToIndexString(_index), value, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectWithCloseJsValue(signal.ThrownValue);
                return false;
            }

            _index++;
            return true;
        }

        private void ProcessArrayLike()
        {
            while (!_settled && _arrayLike is not null)
            {
                var evalContext = realm.CreateContext();
                var lengthValue = _arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
                var dynamicLength = (long)ToLengthOrZero(lengthValue, evalContext);
                if (evalContext.IsThrow)
                {
                    RejectFailureJsValue(evalContext.FlowValue);
                    return;
                }
                if (dynamicLength > MaxConcreteArrayLength)
                {
                    RejectFailureJsValue(CreateRangeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                    return;
                }

                if (_index >= dynamicLength)
                {
                    ResolveSuccess();
                    return;
                }

                if (_index >= MaxConcreteArrayLength)
                {
                    RejectFailureJsValue(CreateTypeError("Array.fromAsync result exceeds 2^32 - 1 elements", null, realm));
                    return;
                }

                var key = ToIndexString(_index);
                var value = GetElementOrUndefinedJsValue(_arrayLike, key);
                if (TryAwaitPromiseLikeJsValue(value,
                        resolved =>
                        {
                            if (HandleArrayLikeValue(key, resolved))
                            {
                                ProcessArrayLike();
                            }
                        },
                        RejectFailureJsValue))
                {
                    return;
                }

                if (!HandleArrayLikeValue(key, value))
                {
                    return;
                }
            }
        }

        private bool HandleArrayLikeValue(string key, JsValue resolved)
        {
            if (_settled)
            {
                return false;
            }

            var finalValue = resolved;

            if (mapping && mapper is not null)
            {
                JsValue mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, resolved, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailureJsValue(signal.ThrownValue);
                    return false;
                }

                if (TryAwaitPromiseLikeJsValue(mapperResult,
                        mapped =>
                        {
                            if (CommitArrayLikeValueJsValue(key, mapped))
                            {
                                ProcessArrayLike();
                            }
                        },
                        RejectFailureJsValue))
                {
                    return false;
                }

                return CommitArrayLikeValueJsValue(key, mapperResult);
            }

            return CommitArrayLikeValueJsValue(key, finalValue);
        }

        private bool CommitArrayLikeValueJsValue(string key, JsValue finalValue)
        {
            if (_settled)
            {
                return false;
            }

            try
            {
                CreateDataPropertyOrThrowJsValue(result, key, finalValue, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectFailureJsValue(signal.ThrownValue);
                return false;
            }

            _index++;
            return true;
        }

        private void RejectSignal(ThrowSignal signal)
        {
            RejectFailureJsValue(signal.ThrownValue);
        }

        private void RejectFailureJsValue(JsValue reason)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            promise.Reject(reason);
        }

        private void RejectWithCloseJsValue(JsValue reason)
        {
            if (_iterator is not null)
            {
                try
                {
                    IteratorClose(_iterator, realm, methodName);
                }
                catch (ThrowSignal signal)
                {
                    reason = signal.ThrownValue;
                }
            }

            RejectFailureJsValue(reason);
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
