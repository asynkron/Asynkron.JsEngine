using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public interface ICallableMetadata
{
    bool IsArrowFunction { get; }
}

/// <summary>
///     Proof-of-concept evaluator that executes the new typed AST directly instead of walking cons cells.
///     The goal is to showcase the recommended shape: a dedicated evaluator with explicit pattern matching
///     rather than virtual methods on the node hierarchy. Only a focused subset of JavaScript semantics is
///     implemented for now so the skeleton stays approachable.
/// </summary>
public static partial class TypedAstEvaluator
{
    private const string GeneratorBrandPropertyName = "__generator_brand__";

    private static readonly string IteratorSymbolPropertyName =
        $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";

    private static readonly object GeneratorBrandMarker = new();
    private static readonly object EmptyCompletion = new();

    private static bool TryConvertToWithBindingObject(
        object? value,
        EvaluationContext context,
        out IJsObjectLike? bindingObject)
    {
        switch (value)
        {
            case IJsObjectLike objectLike:
                bindingObject = objectLike;
                return true;
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
            {
                var error = StandardLibrary.CreateTypeError("Cannot convert undefined or null to object", context,
                    context.RealmState);
                context.SetThrow(error);
                bindingObject = null;
                return false;
            }
            default:
            {
                var converted = ToObjectForDestructuring(value, context);
                if (context.IsThrow)
                {
                    bindingObject = null;
                    return false;
                }

                bindingObject = converted;
                return true;
            }
        }
    }


    // Per ECMA-262 §7.4.1/§7.4.2 (GetIterator / GetAsyncIterator) via @@iterator/@@asyncIterator.
    private static bool TryGetIteratorFromProtocols(object? iterable, out JsObject? iterator)
    {
        iterator = null;
        if (iterable is not IJsPropertyAccessor accessor)
        {
            return false;
        }

        if (TryInvokeSymbolMethod(accessor, iterable, "Symbol.asyncIterator", out var asyncIterator) &&
            asyncIterator is JsObject asyncObj)
        {
            iterator = asyncObj;
            return true;
        }

        if (!TryInvokeSymbolMethod(accessor, iterable, "Symbol.iterator", out var iteratorValue) ||
            iteratorValue is not JsObject iteratorObj)
        {
            return false;
        }

        iterator = iteratorObj;
        return true;
    }


    private static bool IsPromiseLike(object? candidate)
    {
        return AwaitScheduler.IsPromiseLike(candidate);
    }

    // WAITING ON FULL ASYNC/AWAIT + ASYNC GENERATOR IR SUPPORT:
    // This helper synchronously blocks on promise resolution using TaskCompletionSource.
    // It keeps async/await and async iteration usable for now but must be replaced by
    // a non-blocking, event-loop-integrated continuation model once the async IR
    // pipeline is in place.
    private static bool TryAwaitPromise(object? candidate, EvaluationContext context, out object? resolvedValue)
    {
        return AwaitScheduler.TryAwaitPromiseSync(candidate, context, out resolvedValue);
    }


    private static IEnumerable<object?> EnumeratePropertyKeys(object? value)
    {
        switch (value)
        {
            case JsArray array:
            {
                for (var i = 0; i < array.Items.Count; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case string s:
            {
                for (var i = 0; i < s.Length; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case IJsObjectLike accessor:
            {
                foreach (var key in accessor.GetOwnPropertyNames())
                {
                    var desc = accessor.GetOwnPropertyDescriptor(key);
                    if (desc is { Enumerable: false })
                    {
                        continue;
                    }

                    yield return key;
                }

                yield break;
            }
        }

        throw new InvalidOperationException("Cannot iterate properties of non-object value.");
    }

    private static IEnumerable<object?> EnumerateValues(object? value)
    {
        switch (value)
        {
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable<object?> enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
        }

        throw new InvalidOperationException("Value is not iterable.");
    }


    private static object? NormalizeLoopCompletion(object? completion)
    {
        return ReferenceEquals(completion, EmptyCompletion) ? Symbol.Undefined : completion;
    }

    private static DelegatedYieldState CreateDelegatedState(object? iterable)
    {
        if (TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
        {
            return DelegatedYieldState.FromIterator(iterator);
        }

        var values = EnumerateValues(iterable);
        return DelegatedYieldState.FromEnumerable(values);
    }


    private static ImmutableArray<object?> FreezeArguments(ImmutableArray<object?>.Builder builder)
    {
        return builder.Count == builder.Capacity
            ? builder.MoveToImmutable()
            : builder.ToImmutable();
    }

    private static object? CreateRejectedPromise(object? reason, JsEnvironment environment)
    {
        if (!environment.TryGet(Symbol.PromiseIdentifier, out var promiseCtor) ||
            promiseCtor is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("reject", out var rejectValue) ||
            rejectValue is not IJsCallable rejectCallable)
        {
            return reason;
        }

        try
        {
            return rejectCallable.Invoke([reason], promiseCtor);
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }

    private static object? CreateResolvedPromise(object? value, JsEnvironment environment)
    {
        if (!environment.TryGet(Symbol.PromiseIdentifier, out var promiseCtor) ||
            promiseCtor is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("resolve", out var resolveValue) ||
            resolveValue is not IJsCallable resolveCallable)
        {
            return value;
        }

        try
        {
            return resolveCallable.Invoke([value], promiseCtor);
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }


    // SpreadElement runtime semantics (ECMA-262 §12.2.5.2) use GetIterator on the operand.
    private static IEnumerable<object?> EnumerateSpread(object? value, EvaluationContext context)
    {
        if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
        {
            throw StandardLibrary.ThrowTypeError("Value is not iterable.", context, context.RealmState);
        }

        var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);

        try
        {
            while (true)
            {
                var (item, done) = iteratorRecord.Next(context);
                if (context.ShouldStopEvaluation)
                {
                    if (iterator is not null)
                    {
                        IteratorClose(iterator, context);
                    }

                    yield break;
                }

                if (done)
                {
                    yield break;
                }

                yield return item;
            }
        }
        finally
        {
            if (iterator is not null && context.IsThrow)
            {
                IteratorClose(iterator, context);
            }

            enumerator?.Dispose();
        }
    }


    private static void InitializeClassInstance(object? constructor, JsObject instance, JsEnvironment environment,
        EvaluationContext context)
    {
        if (constructor is TypedFunction typedFunction)
        {
            typedFunction.InitializeInstance(instance, environment, context);
        }
    }


    private static bool IsNullish(object? value)
    {
        return value.IsNullish();
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static object? Add(object? left, object? right, EvaluationContext context)
    {
        var leftPrimitive = JsOps.ToPrimitive(left, "default", context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightPrimitive = JsOps.ToPrimitive(right, "default", context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftPrimitive is string || rightPrimitive is string)
        {
            bool IsRealSymbol(object? v)
            {
                return v switch
                {
                    TypedAstSymbol => true,
                    Symbol sym when !ReferenceEquals(sym, Symbol.Undefined) => true,
                    _ => false
                };
            }

            if (IsRealSymbol(leftPrimitive) || IsRealSymbol(rightPrimitive))
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context);
            }

            return JsOps.ToJsString(leftPrimitive, context) + JsOps.ToJsString(rightPrimitive, context);
        }

        var leftNumeric = JsOps.ToNumeric(leftPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = JsOps.ToNumeric(rightPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric is JsBigInt leftBigInt && rightNumeric is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return JsOps.ToNumber(leftNumeric, context) + JsOps.ToNumber(rightNumeric, context);
    }

    private static object Subtract(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l - r,
            (l, r) => l - r,
            context);
    }

    private static object Multiply(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l * r,
            (l, r) => l * r,
            context);
    }

    private static object Divide(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l / r,
            (l, r) => l / r,
            context);
    }

    private static object Modulo(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l % r,
            (l, r) => l % r,
            context);
    }

    private static object Power(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            JsBigInt.Pow,
            (l, r) => Math.Pow(l, r),
            context);
    }

    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp,
        EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return numericOp(JsOps.ToNumber(left, context), JsOps.ToNumber(right, context));
    }

    private static bool LooseEquals(object? left, object? right, EvaluationContext context)
    {
        return JsOps.LooseEquals(left, right, context);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static object BitwiseAnd(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l & r,
            (l, r) => l & r,
            context);
    }

    private static object BitwiseOr(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l | r,
            (l, r) => l | r,
            context);
    }

    private static object BitwiseXor(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r,
            context);
    }

    private static object BitwiseNot(object? operand, EvaluationContext context)
    {
        var numeric = JsOps.ToNumeric(operand, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (numeric is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var int32 = JsNumericConversions.ToInt32(JsOps.ToNumber(numeric, context));
        return (double)~int32;
    }

    private static object LeftShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("BigInts have no unsigned right shift, use >> instead", context);
        }

        var leftUInt = ToUInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftUInt >> rightInt);
    }

    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op,
        EvaluationContext context)
    {
        var leftNumeric = JsOps.ToNumeric(left, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        var rightNumeric = JsOps.ToNumeric(right, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (leftNumeric is JsBigInt leftBigInt && rightNumeric is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = JsNumericConversions.ToInt32(JsOps.ToNumber(leftNumeric, context));
        var rightInt = JsNumericConversions.ToInt32(JsOps.ToNumber(rightNumeric, context));
        return (double)int32Op(leftInt, rightInt);
    }

    private static int ToInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToInt32(JsOps.ToNumber(value, context));
    }

    private static uint ToUInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToUInt32(JsOps.ToNumber(value, context));
    }

    private static object IncrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value + BigInteger.One),
            _ => JsOps.ToNumber(value, context) + 1
        };
    }

    private static object DecrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value - BigInteger.One),
            _ => JsOps.ToNumber(value, context) - 1
        };
    }

    private static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        return JsOps.ToPropertyName(value, context);
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        return JsOps.TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context = null)
    {
        return JsOps.TryGetPropertyValue(target, propertyKey, out value, context);
    }

    private static void AssignPropertyValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context = null)
    {
        JsOps.AssignPropertyValue(target, propertyKey, value, context);
    }

    private static bool DeletePropertyValue(object? target, object? propertyKey, EvaluationContext? context = null)
    {
        return JsOps.DeletePropertyValue(target, propertyKey, context);
    }


    private static bool InOperator(object? property, object? target, EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(property, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            return moduleNamespace.HasExport(propertyName);
        }

        return TryGetPropertyValue(target, propertyName, out _, context);
    }

    private static bool InstanceofOperator(object? left, object? right, EvaluationContext context)
    {
        if (right is not IJsPropertyAccessor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not an object",
                context));
            return false;
        }

        var hasInstanceSymbol = TypedAstSymbol.For("Symbol.hasInstance");
        if (TryGetPropertyValue(right, hasInstanceSymbol, out var hasInstance, context))
        {
            if (!IsNullish(hasInstance))
            {
                if (hasInstance is not IJsCallable callable)
                {
                    context.SetThrow(StandardLibrary.CreateTypeError("@@hasInstance is not callable", context));
                    return false;
                }

                try
                {
                    var result = callable.Invoke([left], right);
                    return JsOps.ToBoolean(result);
                }
                catch (ThrowSignal signal)
                {
                    context.SetThrow(signal.ThrownValue);
                    return false;
                }
            }
        }
        else if (context.ShouldStopEvaluation)
        {
            return false;
        }

        if (right is IJsCallable)
        {
            return OrdinaryHasInstance(left, right, context);
        }

        context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not callable",
            context));
        return false;
    }

    private static bool OrdinaryHasInstance(object? candidate, object? constructor, EvaluationContext context)
    {
        if (constructor is not IJsCallable)
        {
            return false;
        }

        if (candidate is not JsObject && candidate is not IJsObjectLike)
        {
            return false;
        }

        if (!TryGetPropertyValue(constructor, "prototype", out var prototype, context) ||
            prototype is not JsObject prototypeObject)
        {
            context.SetThrow(
                StandardLibrary.CreateTypeError("Function has non-object prototype in instanceof check", context));
            return false;
        }

        var current = candidate switch
        {
            JsObject obj => obj.Prototype,
            IJsObjectLike objectLike => objectLike.Prototype,
            _ => null
        };

        while (current is not null)
        {
            if (ReferenceEquals(current, prototypeObject))
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

    private static string GetTypeofString(object? value)
    {
        return JsOps.GetTypeofString(value);
    }


    // Array/object destructuring uses iterator protocol (ECMA-262 §14.1.5).
    private static bool TryGetIteratorForDestructuring(object? value, EvaluationContext context,
        out JsObject? iterator, [MustDisposeResource] out IEnumerator<object?>? enumerator)
    {
        iterator = null;
        enumerator = null;

        var iteratorTarget = value as IJsPropertyAccessor;
        var thisArg = value;
        if (iteratorTarget is null && value is not null && !ReferenceEquals(value, Symbol.Undefined))
        {
            iteratorTarget = ToObjectForDestructuring(value, context);
            thisArg = iteratorTarget;
        }

        if (iteratorTarget is not null)
        {
            if (TryGetIteratorFromProtocols(iteratorTarget, out var iteratorCandidate) &&
                iteratorCandidate is not null)
            {
                iterator = iteratorCandidate;
                return true;
            }

            // Fallback: treat objects with a callable `next` as iterators even if
            // @@iterator is missing so generator objects still participate in
            // destructuring when their symbol lookup fails.
            if (!iteratorTarget.TryGetProperty("next", out var nextVal) || nextVal is not IJsCallable)
            {
                return false;
            }

            iterator = thisArg as JsObject;
            if (iterator is not null || thisArg is not IJsObjectLike objectLike)
            {
                return true;
            }

            var wrapper = new JsObject();
            foreach (var key in objectLike.Keys)
            {
                if (objectLike.TryGetProperty(key, out var val))
                {
                    wrapper.SetProperty(key, val);
                }
            }

            iterator = wrapper;

            return true;
        }

        switch (value)
        {
            case string s:
                enumerator = EnumerateStringCharacters(s);
                return true;
            case IEnumerable<object?> enumerable:
                enumerator = enumerable.GetEnumerator();
                return true;
        }

        return false;
    }


    [MustDisposeResource]
    private static IEnumerator<object?> EnumerateStringCharacters(string value)
    {
        IEnumerable<object?> Enumerate()
        {
            foreach (var ch in value)
            {
                yield return ch.ToString();
            }
        }

        return Enumerate().GetEnumerator();
    }

    private static JsObject ToObjectForDestructuring(object? value, EvaluationContext context)
    {
        var realm = context.RealmState;
        switch (value)
        {
            case JsObject jsObj:
                return jsObj;
            case JsArray jsArray:
            {
                var obj = new JsObject();
                if (realm?.ArrayPrototype is not null)
                {
                    obj.SetPrototype(realm.ArrayPrototype);
                }

                var length = jsArray.Length;
                var count = length > int.MaxValue ? int.MaxValue : (int)length;
                for (var i = 0; i < count; i++)
                {
                    obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), jsArray.GetElement(i));
                }

                obj.SetProperty("length", length);
                return obj;
            }
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
                throw StandardLibrary.ThrowTypeError("Cannot destructure undefined or null", context, realm);
            case string s:
                return StandardLibrary.CreateStringWrapper(s, context, realm);
            case JsBigInt bi:
                return StandardLibrary.CreateBigIntWrapper(bi, context, realm);
            case TypedAstSymbol symbolValue:
            {
                var obj = new JsObject();
                if (realm?.ObjectPrototype is not null)
                {
                    obj.SetPrototype(realm.ObjectPrototype);
                }

                obj.SetProperty("__value__", symbolValue);
                return obj;
            }
            case double:
            case float:
            case decimal:
            case int:
            case uint:
            case long:
            case ulong:
            case short:
            case ushort:
            case byte:
            case sbyte:
            {
                var num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return StandardLibrary.CreateNumberWrapper(num, context, realm);
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
            default:
            {
                var obj = new JsObject();
                if (realm?.ObjectPrototype is not null)
                {
                    obj.SetPrototype(realm.ObjectPrototype);
                }

                return obj;
            }
        }
    }

    private static JsObject CreateGeneratorIteratorObject(
        Func<IReadOnlyList<object?>, object?> next,
        Func<IReadOnlyList<object?>, object?> @return,
        Func<IReadOnlyList<object?>, object?> @throw)
    {
        var iterator = new JsObject();
        iterator.SetProperty("next", new HostFunction(next));
        iterator.SetProperty("return", new HostFunction(@return));
        iterator.SetProperty("throw", new HostFunction(@throw));
        return iterator;
    }

}
