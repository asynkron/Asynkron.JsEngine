using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal const long MaxArrayLength = 9007199254740991L; // 2^53 - 1


    internal static object CreateArrayIterator(object? thisValue, string methodName, RealmState? realm,
        Func<IJsPropertyAccessor, object?, Func<uint, object?>> projectorFactory)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var projector = projectorFactory(accessor, thisValue);
        return CreateArrayIteratorObject(accessor, projector, realm);
    }

    private static object CreateArrayIteratorObject(IJsPropertyAccessor accessor, Func<uint, object?> projector,
        RealmState? realm)
    {
        var iterator = new JsObject(realm?.ObjectPrototype);
        var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
        var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

        uint index = 0;
        var exhausted = false;

        iterator.SetHostedProperty("next", Next, realm);
        iterator.SetHostedProperty(iteratorKey, ReturnIterator, realm);
        return iterator;

        object? Next(object? _, IReadOnlyList<object?> __, RealmState? ___)
        {
            if (exhausted)
            {
                var doneResult = new JsObject(realm?.ObjectPrototype);
                doneResult.SetProperty("value", Symbol.Undefined);
                doneResult.SetProperty("done", true);
                return doneResult;
            }

            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (uint)Math.Min(Math.Max(ToLengthOrZero(lengthValue), 0), uint.MaxValue);
            var result = new JsObject(realm?.ObjectPrototype);
            if (index < length)
            {
                result.SetProperty("value", projector(index));
                result.SetProperty("done", false);
                index++;
            }
            else
            {
                result.SetProperty("value", Symbol.Undefined);
                result.SetProperty("done", true);
                exhausted = true;
            }

            return result;
        }

        object? ReturnIterator(object? _, IReadOnlyList<object?> __, RealmState? ___)
        {
            return iterator;
        }
    }

    internal static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    internal static bool AreStrictlyEqual(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    internal static IJsObjectLike ToArrayLike(object? value, RealmState? realm)
    {
        if (value is IJsObjectLike accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError("Array method called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError("Array method receiver is not object-like", realm: realm);
    }

    internal static int GetArrayLikeLength(IJsObjectLike obj)
    {
        if (!obj.TryGetProperty("length", out var lengthVal))
        {
            return 0;
        }

        var asNumber = JsOps.ToNumber(lengthVal);
        if (double.IsNaN(asNumber) || !(asNumber > 0))
        {
            return 0;
        }

        if (asNumber > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)asNumber;

    }

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
            var receiver = CreateArrayLikeReceiverForConstructor(callable, realm, len);
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
        var result = CreateArrayFromResult(thisValue, realm, initialLength, true);

        long k = 0;
        while (true)
        {
            var lengthValue = arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var dynamicLength = (long)ToLengthOrZero(lengthValue);
            if (k >= dynamicLength)
            {
                break;
            }

            if (k >= MaxArrayLength)
            {
                throw ThrowTypeError("Array.from result exceeds 2^53 - 1 elements", realm: realm);
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
            result = CreateArrayFromResult(thisValue, realm, 0, false);
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
        var result = CreateArrayFromResult(thisValue, realm, 0, false);
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
                step = nextFn.Invoke([], iterator);
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

            if (k >= MaxArrayLength)
            {
                IteratorClose(iterator, realm, MethodName);
                throw ThrowTypeError("Array.from result exceeds 2^53 - 1 elements", realm: realm);
            }

            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : Symbol.Undefined;
            object? mappedValue = value;
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
            var engine = realm?.Engine;
            var fulfilled = new HostFunction((_, args) =>
            {
                var value = args.GetArgument(0);
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onFulfilled(value);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onFulfilled(value);
                }

                return Symbol.Undefined;
            }, realm);
            var rejected = new HostFunction((_, args) =>
            {
                var reason = args.GetArgument(0);
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onRejected(reason);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onRejected(reason);
                }

                return Symbol.Undefined;
            }, realm);

            try
            {
                thenCallable.Invoke([fulfilled, rejected], jsObject);
            }
            catch (ThrowSignal signal)
            {
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onRejected(signal.ThrownValue ?? signal);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onRejected(signal.ThrownValue ?? signal);
                }
            }

            return true;
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


    internal static void AttachBuiltinMetadata(HostFunction fn, string name, double length)
    {
        fn.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });
        fn.DefineProperty("length",
            new PropertyDescriptor { Value = length, Writable = false, Enumerable = false, Configurable = true });
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
                iteratorValue = iteratorMethod.Invoke([], items);
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
                step = _nextFn.Invoke([], _iterator);
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

            if (_index >= MaxArrayLength)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync result exceeds 2^53 - 1 elements", null, realm));
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
            if (_index >= dynamicLength)
            {
                ResolveSuccess();
                return;
            }

            if (_index >= MaxArrayLength)
            {
                RejectFailure(CreateTypeError("Array.fromAsync result exceeds 2^53 - 1 elements", null, realm));
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

        private void ResolveSuccess()
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            SetArrayLikeLength(result, _index);
            promise.Resolve(result);
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
            if (_settled)
            {
                return;
            }

            _settled = true;

            if (_iterator is null)
            {
                promise.Reject(reason);
                return;
            }

            if (!_iterator.TryGetProperty("return", out var returnValue) ||
                returnValue is null ||
                ReferenceEquals(returnValue, Symbol.Undefined))
            {
                promise.Reject(reason);
                return;
            }

            if (returnValue is not IJsCallable returnFn)
            {
                promise.Reject(CreateTypeError($"{methodName} iterator.return is not callable", null, realm));
                return;
            }

            object? completion;
            try
            {
                completion = returnFn.Invoke([], _iterator);
            }
            catch (ThrowSignal signal)
            {
                promise.Reject(signal.ThrownValue ?? signal);
                return;
            }

            if (TryAwaitPromiseLike(completion, realm,
                    _ => promise.Reject(reason),
                    rejection => promise.Reject(rejection)))
            {
                return;
            }

            promise.Reject(reason);
        }

        private void RejectSignal(ThrowSignal signal)
        {
            RejectFailure(signal.ThrownValue ?? signal);
        }
    }

    internal static readonly string SymbolSpeciesKey = $"@@symbol:{TypedAstSymbol.For("Symbol.species").GetHashCode()}";
    internal static readonly string SymbolIteratorKey = $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";
    internal static readonly string SymbolAsyncIteratorKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.asyncIterator").GetHashCode()}";
    internal static readonly string SymbolIsConcatSpreadableKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.isConcatSpreadable").GetHashCode()}";

    internal static IJsObjectLike ArraySpeciesCreate(object? original, long length, RealmState? realm)
    {
        length = Math.Max(length, 0);

        IJsObjectLike CreateDefaultArray()
        {
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
        bool passLengthToConstructor)
    {
        if (constructorCandidate is IJsCallable callable && JsOps.IsConstructor(callable))
        {
            var receiver = CreateArrayLikeReceiverForConstructor(callable, realm, passLengthToConstructor ? length : 0);
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

        var array = new JsArray(realm);
        array.SetProperty("length", passLengthToConstructor ? (double)Math.Max(length, 0) : 0d);
        return array;
    }

    internal static IJsObjectLike CreateArrayLikeReceiverForConstructor(IJsCallable constructor, RealmState? realm,
        long length)
    {
        var proto = ResolveConstructPrototype(constructor, constructor, realm);
        IJsObjectLike receiver;

        if (constructor is HostFunction hostFunction && realm?.ArrayConstructor is not null &&
            ReferenceEquals(hostFunction, realm.ArrayConstructor))
        {
            var array = new JsArray(realm);
            receiver = array;
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
        if (candidate is not IJsPropertyAccessor propertyAccessor)
        {
            return false;
        }

        if (propertyAccessor.TryGetProperty(SymbolIsConcatSpreadableKey, out var spreadableValue) &&
            !ReferenceEquals(spreadableValue, Symbol.Undefined))
        {
            if (JsOps.IsTruthy(spreadableValue))
            {
                accessor = propertyAccessor;
                return true;
            }

            return false;
        }

        if (IsArrayObject(candidate, realm, operation))
        {
            accessor = propertyAccessor;
            return true;
        }

        return false;
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

        if (inspected is JsObject obj && realm?.ArrayPrototype is not null &&
            ReferenceEquals(obj, realm.ArrayPrototype))
        {
            return true;
        }

        return false;
    }

    internal static bool TryGetArrayForFlatten(object? candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate is not IJsPropertyAccessor propertyAccessor)
        {
            return false;
        }

        var inspected = UnwrapProxy(candidate, realm, operation);

        if (inspected is JsArray jsArray)
        {
            if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
            {
                return false;
            }

            accessor = propertyAccessor;
            return true;
        }

        if (inspected is JsObject obj && realm?.ArrayPrototype is not null &&
            ReferenceEquals(obj, realm.ArrayPrototype))
        {
            accessor = propertyAccessor;
            return true;
        }

        return false;
    }

    internal static object? UnwrapProxy(object? candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected is JsProxy proxy)
        {
            if (proxy.Handler is null)
            {
                throw ThrowTypeError($"Cannot perform '{operation}' with a revoked Proxy", realm: realm);
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
            if (inspected is JsArray)
            {
                return true;
            }

            if (inspected is JsObject obj && realm?.ArrayPrototype is not null &&
                ReferenceEquals(obj, realm.ArrayPrototype))
            {
                return true;
            }

            if (inspected is JsProxy proxy)
            {
                if (proxy.Handler is null)
                {
                    throw ThrowTypeError($"Cannot perform '{operation}' with a revoked Proxy", realm: realm);
                }

                inspected = proxy.Target;
                continue;
            }

            break;
        }

        return false;
    }

    internal static void CopyArrayElement(IJsPropertyAccessor source, long sourceIndex, IJsPropertyAccessor target,
        long targetIndex)
    {
        var sourceKey = ToIndexString(sourceIndex);
        var targetKey = ToIndexString(targetIndex);

        if (TryGetExistingElement(source, sourceKey, out var value))
        {
            target.SetProperty(targetKey, value);
            return;
        }

        if (target is IJsObjectLike targetLike)
        {
            targetLike.Delete(targetKey);
        }
        else
        {
            target.SetProperty(targetKey, Symbol.Undefined);
        }
    }

    internal static long FlattenIntoArray(IJsPropertyAccessor target, IJsPropertyAccessor source, long sourceLength,
        long targetIndex, long depth, IJsCallable? mapper, object? thisArg, RealmState? realm, string operation)
    {
        for (long k = 0; k < sourceLength; k++)
        {
            var key = ToIndexString(k);
            if (!TryGetExistingElement(source, key, out var element))
            {
                continue;
            }

            object? mappedValue = element;
            if (mapper is not null)
            {
                mappedValue = mapper.Invoke([element, (double)k, source], thisArg ?? Symbol.Undefined);
            }

            if (depth > 0 && TryGetArrayForFlatten(mappedValue, realm, operation, out var flattenAccessor))
            {
                var lengthValue = flattenAccessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
                var elementLength = (long)ToLengthOrZero(lengthValue);
                targetIndex =
                    FlattenIntoArray(target, flattenAccessor, elementLength, targetIndex, depth - 1, null, null, realm,
                        operation);
                continue;
            }

            if (targetIndex >= MaxArrayLength)
            {
                throw ThrowTypeError("Flattened array exceeds maximum length", realm: realm);
            }

            target.SetProperty(ToIndexString(targetIndex), mappedValue);
            targetIndex++;
        }

        return targetIndex;
    }

    internal static void SetArrayLikeLength(IJsPropertyAccessor target, long length)
    {
        target.SetProperty("length", (double)Math.Max(length, 0));
    }

    internal static long LengthOfArrayLike(object? target, RealmState? realm, string operation)
    {
        if (target is null || ReferenceEquals(target, Symbol.Undefined))
        {
            throw ThrowTypeError($"{operation} called on null or undefined", realm: realm);
        }

        if (!JsOps.TryGetPropertyValue(target, "length", out var lengthValue))
        {
            lengthValue = 0d;
        }

        var numericContext = realm?.CreateContext();
        return (long)ToLengthOrZero(lengthValue, numericContext);
    }

    internal static (IJsPropertyAccessor Accessor, long Length, IJsCallable Callback, object? ThisArg)
        PrepareArrayIteration(object? receiver, IReadOnlyList<object?> args, RealmState? realm, string methodName)
    {
        var accessor = EnsureArrayLikeReceiver(receiver, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var thisArg = args.GetArgument(1);
        return (accessor, length, callback, thisArg);
    }

    internal static string ToIndexString(long index)
    {
        return index.ToString(CultureInfo.InvariantCulture);
    }

    internal static double ToLengthOrZero(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context is not null && context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(number))
        {
            return MaxArrayLength;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLength ? MaxArrayLength : truncated;
    }

    internal static double ToIntegerOrInfinity(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context is not null && context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number))
        {
            return 0;
        }

        if (double.IsInfinity(number) || number == 0)
        {
            return number;
        }

        return Math.Sign(number) * Math.Floor(Math.Abs(number));
    }

    internal static long ClampRelativeIndex(double index, long length)
    {
        if (double.IsNegativeInfinity(index))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(index))
        {
            return length;
        }

        var integer = (long)Math.Truncate(index);
        if (integer < 0)
        {
            var relative = length + integer;
            return relative < 0 ? 0 : relative;
        }

        return integer > length ? length : integer;
    }

    internal static object? ReduceLike(object? thisValue, IReadOnlyList<object?> args, RealmState? realm,
        string methodName, bool fromRight)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        if (accessor is TypedArrayBase typed)
        {
            if (typed.IsDetachedOrOutOfBounds())
            {
                throw typed.CreateOutOfBoundsTypeError();
            }

            var length = typed.Length;
            var step = fromRight ? -1 : 1;
            var index = fromRight ? length - 1 : 0;

            var hasAccumulator = args.Count > 1;
            var accumulator = hasAccumulator ? args[1] : null;

            if (!hasAccumulator)
            {
                if (length == 0)
                {
                    throw ThrowTypeError("Reduce of empty array with no initial value", realm: realm);
                }

                accumulator = typed.GetValueForIndex(index);
                index += step;
            }

            while (index >= 0 && index < length)
            {
                if (typed.IsDetachedOrOutOfBounds())
                {
                    throw typed.CreateOutOfBoundsTypeError();
                }

                var current = typed.GetValueForIndex(index);
                accumulator = callback.Invoke([accumulator, current, (double)index, typed], Symbol.Undefined);
                index += step;
            }

            return accumulator;
        }

        var lengthValue = accessor.TryGetProperty("length", out var len) ? len : 0d;
        var lengthGeneric = (int)ToLengthOrZero(lengthValue);
        var stepGeneric = fromRight ? -1 : 1;
        var indexGeneric = fromRight ? lengthGeneric - 1 : 0;

        var hasAccumulatorGeneric = args.Count > 1;
        var accumulatorGeneric = hasAccumulatorGeneric ? args[1] : null;

        if (!hasAccumulatorGeneric)
        {
            var found = false;
            while (indexGeneric >= 0 && indexGeneric < lengthGeneric)
            {
                if (TryGetExistingElement(accessor, indexGeneric, out var current))
                {
                    accumulatorGeneric = current;
                    found = true;
                    indexGeneric += stepGeneric;
                    break;
                }

                indexGeneric += stepGeneric;
            }

            if (!found)
            {
                throw ThrowTypeError("Reduce of empty array with no initial value", realm: realm);
            }
        }

        while (indexGeneric >= 0 && indexGeneric < lengthGeneric)
        {
            if (TryGetExistingElement(accessor, indexGeneric, out var current))
            {
                accumulatorGeneric = callback.Invoke([accumulatorGeneric, current, (double)indexGeneric, accessor],
                    Symbol.Undefined);
            }

            indexGeneric += stepGeneric;
        }

        return accumulatorGeneric;
    }

    internal static object? SomeLike(object? thisValue, IReadOnlyList<object?> args, RealmState? realm,
        string methodName)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);

        if (accessor is TypedArrayBase typed)
        {
            if (typed.IsDetachedOrOutOfBounds())
            {
                throw typed.CreateOutOfBoundsTypeError();
            }

            var length = typed.Length;
            for (var i = 0; i < length; i++)
            {
                if (typed.IsDetachedOrOutOfBounds())
                {
                    throw typed.CreateOutOfBoundsTypeError();
                }

                var value = typed.GetValueForIndex(i);
                var testResult = callback.Invoke([value, (double)i, typed], thisArg);
                if (IsTruthy(testResult))
                {
                    return true;
                }
            }

            return false;
        }

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var objectLength = (long)ToLengthOrZero(lengthValue);
        for (long i = 0; i < objectLength; i++)
        {
            if (!TryGetExistingElement(accessor, i, out var value))
            {
                continue;
            }

            var testResult = callback.Invoke([value, (double)i, accessor], thisArg);
            if (IsTruthy(testResult))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool SameValueZero(object? x, object? y)
    {
        if (x is double.NaN && y is double.NaN)
        {
            return true;
        }

        return JsOps.StrictEquals(x, y);
    }

    internal static IJsPropertyAccessor EnsureArrayLikeReceiver(object? receiver, string methodName, RealmState? realm)
    {
        if (receiver is null || ReferenceEquals(receiver, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        switch (receiver)
        {
            case IJsPropertyAccessor accessor when accessor is not TypedAstSymbol:
            {
                if (accessor is not JsObject jsObj || !jsObj.TryGetProperty("__value__", out var inner) ||
                    inner is not string sInner)
                {
                    return accessor;
                }

                if (!jsObj.TryGetProperty("length", out _))
                {
                    jsObj.DefineProperty("length",
                        new PropertyDescriptor
                        {
                            Value = (double)sInner.Length,
                            Writable = false,
                            Enumerable = false,
                            Configurable = false
                        });
                }

                for (var i = 0; i < sInner.Length; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    if (!jsObj.TryGetProperty(key, out _))
                    {
                        jsObj.SetProperty(key, sInner[i].ToString());
                    }
                }

                return jsObj;
            }
            // Box primitives to objects per ToObject.
            case string s:
            {
                var obj = new JsObject();
                if (realm?.StringPrototype is not null)
                {
                    obj.SetPrototype(realm.StringPrototype);
                }

                obj.SetProperty("__value__", s);
                obj.DefineProperty("length",
                    new PropertyDescriptor
                    {
                        Value = (double)s.Length, Writable = false, Enumerable = false, Configurable = false
                    });

                for (var i = 0; i < s.Length; i++)
                {
                    obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), s[i].ToString());
                }

                return obj;
            }
            case double or int or uint or long or ulong or short or ushort or byte or sbyte or decimal or float:
            {
                var obj = new JsObject();
                if (realm?.NumberPrototype is not null)
                {
                    obj.SetPrototype(realm.NumberPrototype);
                }

                obj.SetProperty("__value__", receiver);
                return obj;
            }
            case bool b:
            {
                var obj = new JsObject();
                if (realm?.BooleanPrototype is not null)
                {
                    obj.SetPrototype(realm.BooleanPrototype);
                }

                obj.SetProperty("__value__", b);
                return obj;
            }
            // Symbols and BigInts should throw TypeError for array methods
            case TypedAstSymbol:
            case JsBigInt:
                throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
            default:
                throw ThrowTypeError($"{methodName} called on non-object", realm: realm);
        }
    }

    internal static bool TryGetCallableMethod(object? target, string propertyKey, string operation, RealmState? realm,
        out IJsCallable? callable)
    {
        callable = null;
        if (!JsOps.TryGetPropertyValue(target, propertyKey, out var candidate))
        {
            return false;
        }

        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return false;
        }

        if (candidate is not IJsCallable method)
        {
            throw ThrowTypeError($"{operation} property '{propertyKey}' is not callable", realm: realm);
        }

        callable = method;
        return true;
    }

    internal static IJsPropertyAccessor ToPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} requires an array-like or iterable", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} could not convert the source to an object", realm: realm);
    }

    internal static IJsPropertyAccessor ToObjectPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} called on non-object", realm: realm);
    }

    internal static void IteratorClose(IJsPropertyAccessor iterator, RealmState? realm, string operation)
    {
        if (!iterator.TryGetProperty("return", out var returnValue) ||
            returnValue is null ||
            ReferenceEquals(returnValue, Symbol.Undefined))
        {
            return;
        }

        if (returnValue is not IJsCallable returnFn)
        {
            throw ThrowTypeError($"{operation} iterator.return is not callable", realm: realm);
        }

        _ = returnFn.Invoke([], iterator);
    }

    internal static void CreateDataPropertyOrThrow(IJsObjectLike target, string propertyKey, object? value,
        RealmState? realm, string operation)
    {
        try
        {
            var descriptor = new PropertyDescriptor
            {
                Value = value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
            target.DefineProperty(propertyKey, descriptor);
        }
        catch (ThrowSignal)
        {
            throw;
        }
        catch (Exception)
        {
            throw ThrowTypeError($"{operation} could not define property '{propertyKey}'", realm: realm);
        }

        var defined = target.GetOwnPropertyDescriptor(propertyKey);
        if (defined is null || !defined.Writable || !defined.Configurable || !defined.Enumerable)
        {
            throw ThrowTypeError($"{operation} could not define property '{propertyKey}'", realm: realm);
        }
    }

    internal static bool TryGetExistingElement(IJsPropertyAccessor accessor, long index, out object? value)
    {
        return TryGetExistingElement(accessor, ToIndexString(index), out value);
    }

    internal static bool TryGetExistingElement(IJsPropertyAccessor accessor, string propertyKey, out object? value)
    {
        if (!HasProperty(accessor, propertyKey))
        {
            value = null;
            return false;
        }

        if (!accessor.TryGetProperty(propertyKey, out value))
        {
            value = Symbol.Undefined;
        }

        return true;
    }

    internal static object? GetElementOrUndefined(IJsPropertyAccessor accessor, string propertyKey)
    {
        return accessor.TryGetProperty(propertyKey, out var value) ? value : Symbol.Undefined;
    }

    internal static object InvokeDefaultObjectToString(object? target, RealmState? realm)
    {
        if (realm?.ObjectPrototype is IJsPropertyAccessor objectPrototype &&
            objectPrototype.TryGetProperty("toString", out var toStringValue) &&
            toStringValue is IJsCallable callable)
        {
            return callable.Invoke([], target);
        }

        return "[object Object]";
    }
}
