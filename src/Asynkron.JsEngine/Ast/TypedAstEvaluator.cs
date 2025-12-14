using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public interface ICallableMetadata
{
    bool IsArrowFunction { get; }

    bool DisallowConstruct { get; }

    RealmState RealmState { get; }
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

    private static readonly string IteratorSymbolPropertyName = SymbolKeys.Iterator;

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
    private static bool TryGetIteratorFromProtocols(object? iterable, EvaluationContext context, out IJsObjectLike? iterator)
    {
        iterator = null;
        if (iterable is not IJsPropertyAccessor accessor)
        {
            return false;
        }

        var logger = context.RealmState?.Logger;
        var iteratorKey = SymbolKeys.Iterator;
        logger?.LogInformation("TryGetIteratorFromProtocols start targetType={Type} iteratorKey={Key}",
            iterable?.GetType().Name ?? "null",
            iteratorKey);
        if (accessor is IJsObjectLike objectLike)
        {
            var keysPreview = string.Join(",", objectLike.Keys.Take(8));
            logger?.LogInformation("TryGetIteratorFromProtocols keys={Keys}", keysPreview);
        }

        if (TryInvokeSymbolMethod(accessor, iterable, Symbols.AsyncIterator, context, out var asyncIterator))
        {
            logger?.LogInformation("TryGetIteratorFromProtocols asyncIterator invoked stop={Stop} type={IterType}",
                context.ShouldStopEvaluation,
                asyncIterator?.GetType().Name ?? "null");
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (asyncIterator is IJsObjectLike asyncObj)
            {
                iterator = asyncObj;
                return true;
            }

            var typeError = StandardLibrary.CreateTypeError("Iterator is not an object", context, context.RealmState);
            context.SetThrow(typeError);
            return false;
        }

        if (TryInvokeSymbolMethod(accessor, iterable, Symbols.Iterator, context, out var iteratorValue))
        {
            logger?.LogInformation("TryGetIteratorFromProtocols iterator invoked stop={Stop} type={IterType}",
                context.ShouldStopEvaluation,
                iteratorValue?.GetType().Name ?? "null");
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (iteratorValue is IJsObjectLike iteratorObj)
            {
                iterator = iteratorObj;
                return true;
            }

            var typeError = StandardLibrary.CreateTypeError("Iterator is not an object", context, context.RealmState);
            context.SetThrow(typeError);
            return false;
        }

        return false;
    }


    private static bool IsPromiseLike(JsValue candidate)
    {
        return AwaitScheduler.IsPromiseLike(candidate);
    }

    // RequireObjectCoercible for iteration heads so null/undefined throw a JS TypeError
    // before attempting iterator resolution (ES2024 14.7.5.1, 14.7.5.2).
    private static void EnsureObjectCoercibleForIteration(object? value, EvaluationContext context)
    {
        if (value is null || ReferenceEquals(value, Symbol.Undefined) || value is IIsHtmlDda)
        {
            throw StandardLibrary.ThrowTypeError("Cannot iterate over undefined or null", context, context.RealmState);
        }
    }

    // ToObject for iteration lookup: primitives must be wrapped so @@iterator can be
    // found on their prototypes (ES2024 GetIterator/ToObject step).
    private static object NormalizeIterableTarget(object? value, EvaluationContext context)
    {
        EnsureObjectCoercibleForIteration(value, context);

        return value switch
        {
            IJsPropertyAccessor => value,
            _ => ToObjectForDestructuring(value, context)
        };
    }

    // WAITING ON FULL ASYNC/AWAIT + ASYNC GENERATOR IR SUPPORT:
    // This helper synchronously blocks on promise resolution using TaskCompletionSource.
    // It keeps async/await and async iteration usable for now but must be replaced by
    // a non-blocking, event-loop-integrated continuation model once the async IR
    // pipeline is in place.
    private static bool TryAwaitPromise(JsValue candidate, EvaluationContext context, out JsValue resolvedValue)
    {
        return AwaitScheduler.TryAwaitPromiseSync(
            candidate,
            context,
            out resolvedValue,
            context.DrainAwaitMicrotasks);
    }


    private static IEnumerable<object?> EnumeratePropertyKeys(object? value)
    {
        switch (value)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                // Per ES spec, for-in over null or undefined should not iterate (no properties to enumerate)
                yield break;

            case JsArray array:
            {
                // First, enumerate numeric indices (array elements)
                for (var i = 0; i < array.Items.Count; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                // Track seen keys to properly handle shadowing
                var seenArrayKeys = new HashSet<string>(StringComparer.Ordinal);

                // Add all numeric indices as seen (already enumerated above)
                for (var i = 0; i < array.Items.Count; i++)
                {
                    seenArrayKeys.Add(i.ToString(CultureInfo.InvariantCulture));
                }

                // Now enumerate non-index properties on the array and its prototype chain
                IJsPropertyAccessor? currentArray = array;
                while (currentArray is not null)
                {
                    var keys = currentArray.GetOwnPropertyNames().ToList();

                    foreach (var key in keys)
                    {
                        // Skip if we've already seen this key
                        if (!seenArrayKeys.Add(key))
                        {
                            continue;
                        }

                        // Skip 'length' - it's not enumerable
                        if (string.Equals(key, "length", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var desc = currentArray.GetOwnPropertyDescriptor(key);
                        if (desc is null || desc is { Enumerable: false })
                        {
                            continue;
                        }

                        yield return key;
                    }

                    // Move to prototype
                    currentArray = currentArray switch
                    {
                        IJsObjectLike objectLike => objectLike.Prototype,
                        IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                        _ => null
                    };
                }

                yield break;
            }

            case TypedArrayBase typedArray:
            {
                // TypedArray for-in only exposes own enumerable properties (indices and custom slots).
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in typedArray.GetOwnPropertyNames().ToList())
                {
                    if (!seenKeys.Add(key))
                    {
                        continue;
                    }

                    var desc = typedArray.GetOwnPropertyDescriptor(key);
                    if (desc is null || desc is { Enumerable: false })
                    {
                        continue;
                    }

                    yield return key;
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
                // Track seen keys to properly handle shadowing - prototype properties
                // with the same name as own properties should not be enumerated
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);

                // Walk prototype chain, starting with the object itself
                IJsPropertyAccessor? current = accessor;
                while (current is not null)
                {
                    // Collect keys from this object in the chain - we snapshot to avoid
                    // concurrent modification issues during iteration
                    var keys = current.GetOwnPropertyNames().ToList();

                    foreach (var key in keys)
                    {
                        // Skip if we've already seen this key (shadowed by own/earlier property)
                        if (!seenKeys.Add(key))
                        {
                            continue;
                        }

                        // Per ECMAScript spec, skip properties that were deleted during enumeration.
                        // Check that the property still exists on this object in the chain.
                        var desc = current.GetOwnPropertyDescriptor(key);
                        if (desc is null)
                        {
                            // Property was deleted since we collected the keys
                            continue;
                        }
                        if (desc is { Enumerable: false })
                        {
                            continue;
                        }

                        yield return key;
                    }

                    // Move to prototype
                    current = current switch
                    {
                        IJsObjectLike objectLike => objectLike.Prototype,
                        IPrototypeAccessorProvider provider => provider.PrototypeAccessor,
                        _ => null
                    };
                }

                yield break;
            }
        }

        throw new InvalidOperationException("Cannot iterate properties of non-object value.");
    }

    private static IEnumerable<object?> EnumerateValues(object? value, EvaluationContext context)
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

        throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
    }


    private static object? NormalizeLoopCompletion(object? completion)
    {
        return ReferenceEquals(completion, EmptyCompletion) ? Symbol.Undefined : completion;
    }

    private static DelegatedYieldState CreateDelegatedState(object? iterable, EvaluationContext context)
    {
        var iteratorTarget = NormalizeIterableTarget(iterable, context);
        if (context.ShouldStopEvaluation)
        {
            return DelegatedYieldState.FromEnumerable(Array.Empty<object?>());
        }

        if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
        {
            return DelegatedYieldState.FromIterator(iterator);
        }

        if (context.ShouldStopEvaluation)
        {
            return DelegatedYieldState.FromEnumerable(Array.Empty<object?>());
        }

        throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
    }


    private static ImmutableArray<JsValue> FreezeArguments(ImmutableArray<JsValue>.Builder builder)
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
            return rejectCallable.Invoke([JsValue.FromObject(reason)], JsValue.FromObject(promiseCtor)).ToObject();
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }

    private static object? CreateResolvedPromise(object? value, JsEnvironment environment)
    {
        object? resolveCandidate = null;
        if (!environment.TryGet(Symbol.PromiseIdentifier, out var promiseCtor) ||
            promiseCtor is not IJsPropertyAccessor accessor ||
            !accessor.TryGetProperty("resolve", out resolveCandidate) ||
            resolveCandidate is not IJsCallable resolveCallable)
        {
            environment.RealmState?.Logger?.LogInformation(
                "CreateResolvedPromise falling back (promiseCtorType={CtorType}, hasResolve={HasResolve}, resolveCallable={ResolveCallable})",
                promiseCtor?.GetType().Name ?? "null",
                promiseCtor is IJsPropertyAccessor a && a.TryGetProperty("resolve", out _),
                resolveCandidate is IJsCallable);
            return value;
        }

        try
        {
            return resolveCallable.Invoke([JsValue.FromObject(value)], JsValue.FromObject(promiseCtor)).ToObject();
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }


    // SpreadElement runtime semantics (ECMA-262 §12.2.5.2) use GetIterator on the operand.
    private static IEnumerable<JsValue> EnumerateSpread(JsValue value, EvaluationContext context)
    {
        if (!TryGetIteratorForDestructuring(value.ToObject(), context, out var iterator, out var enumerator))
        {
            if (context.ShouldStopEvaluation)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable.", context, context.RealmState);
        }

        if (context.ShouldStopEvaluation)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        var logger = context.RealmState?.Logger;
        var valueObj = value.ToObject();
        logger?.LogInformation("EnumerateSpread start valueType={Type} hasIterator={HasIterator} hasEnumerator={HasEnum}",
            valueObj?.GetType().Name ?? "null",
            iterator is not null,
            enumerator is not null);
        var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);

        try
        {
            var index = 0;
            while (true)
            {
                var (item, done) = iteratorRecord.Next(context);
                if (context.ShouldStopEvaluation)
                {
                    if (iterator is not null)
                    {
                        IteratorClose(iterator, context);
                    }

                    throw new ThrowSignal(context.FlowValue);
                }

                if (done)
                {
                    logger?.LogInformation("EnumerateSpread done at index={Index}", index);
                    yield break;
                }

                if (index < 5 || index % 1000 == 0)
                {
                    var itemObj = item.ToObject();
                    logger?.LogInformation("EnumerateSpread yield index={Index} type={Type}", index,
                        itemObj?.GetType().Name ?? "null");
                }

                yield return item;
                index++;
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


    private static bool IsNullish(object? value)
    {
        return value.IsNullish();
    }

    private static bool HasOptionalChaining(ExpressionNode? expression)
    {
        while (expression is not null)
        {
            switch (expression)
            {
                case MemberExpression { IsOptional: true }:
                case CallExpression { IsOptional: true }:
                    return true;
                case MemberExpression member:
                    expression = member.Target;
                    break;
                case CallExpression call:
                    expression = call.Callee;
                    break;
                default:
                    return false;
            }
        }
        return false;
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static object? Add(object? left, object? right, EvaluationContext context)
    {
        // Fast path: both operands are already doubles (very common in loops)
        if (left is double leftDouble && right is double rightDouble)
        {
            return JsValueCache.GetNumber(leftDouble + rightDouble);
        }

        var leftPrimitive = JsOps.ToPrimitive(left, ToPrimitiveHint.Default, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightPrimitive = JsOps.ToPrimitive(right, ToPrimitiveHint.Default, context);
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

        // Use NumericResult to avoid boxing
        var leftNumeric = JsOps.ToNumericResult(leftPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = JsOps.ToNumericResult(rightPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        // Both are numbers - most common case
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
        {
            return JsValueCache.GetNumber(leftNumeric.NumberValue + rightNumeric.NumberValue);
        }

        // Both are BigInt
        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            return leftNumeric.BigIntValue! + rightNumeric.BigIntValue!;
        }

        // Mixed types - error
        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        // Fallback
        return JsValueCache.GetNumber(leftNumeric.NumberValue + rightNumeric.NumberValue);
    }

    private static object Subtract(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r, _) => l - r,
            (l, r) => l - r,
            context);
    }

    private static object Multiply(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r, _) => l * r,
            (l, r) => l * r,
            context);
    }

    private static object Divide(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r, ctx) =>
            {
                if (r.Value.IsZero)
                {
                    throw StandardLibrary.ThrowRangeError("Division by zero", ctx);
                }

                return l / r;
            },
            (l, r) => l / r,
            context);
    }

    private static object Modulo(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r, ctx) =>
            {
                if (r.Value.IsZero)
                {
                    throw StandardLibrary.ThrowRangeError("Division by zero", ctx);
                }

                return l % r;
            },
            (l, r) => l % r,
            context);
    }

    private static object Power(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r, ctx) =>
            {
                try
                {
                    return JsBigInt.Pow(l, r);
                }
                catch (InvalidOperationException ex)
                {
                    throw StandardLibrary.ThrowRangeError(ex.Message, ctx);
                }
            },
            (l, r) => JsOps.MathPow(l, r),
            context);
    }

    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, EvaluationContext, object> bigIntOp,
        Func<double, double, double> numericOp,
        EvaluationContext context)
    {
        // Use NumericResult to avoid boxing during the operation
        var leftNumeric = JsOps.ToNumericResult(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        var rightNumeric = JsOps.ToNumericResult(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        // Both are numbers - most common case
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
        {
            return JsValueCache.GetNumber(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
        }

        // Both are BigInt
        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            return bigIntOp(leftNumeric.BigIntValue!, rightNumeric.BigIntValue!, context);
        }

        // Mixed types - error
        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        // Fallback (shouldn't reach here normally)
        return JsValueCache.GetNumber(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
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
        // Fast path for double (most common case)
        if (operand is double d)
        {
            var int32 = JsNumericConversions.ToInt32(d);
            return JsValueCache.GetNumber(~int32);
        }

        var numeric = JsOps.ToNumeric(operand, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (numeric is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var int32Val = JsNumericConversions.ToInt32(JsOps.ToNumber(numeric, context));
        return JsValueCache.GetNumber(~int32Val);
    }

    private static object UnaryMinus(object? operand, EvaluationContext context)
    {
        // Fast path for double (most common case)
        if (operand is double d)
        {
            return JsValueCache.GetNumber(-d);
        }

        var numeric = JsOps.ToNumeric(operand, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (numeric is JsBigInt bigInt)
        {
            return -bigInt;
        }

        return JsValueCache.GetNumber(-JsOps.ToNumber(numeric, context));
    }

    private static object LeftShift(object? left, object? right, EvaluationContext context)
    {
        // Fast path for double operands (most common case)
        if (left is double leftD && right is double rightD)
        {
            var leftInt = JsNumericConversions.ToInt32(leftD);
            var rightInt = JsNumericConversions.ToInt32(rightD) & 0x1F;
            return JsValueCache.GetNumber(leftInt << rightInt);
        }

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
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftIntVal = ToInt32(leftNumeric, context);
        var rightIntVal = ToInt32(rightNumeric, context) & 0x1F;
        return JsValueCache.GetNumber(leftIntVal << rightIntVal);
    }

    private static object RightShift(object? left, object? right, EvaluationContext context)
    {
        // Fast path for double operands (most common case)
        if (left is double leftD && right is double rightD)
        {
            var leftInt = JsNumericConversions.ToInt32(leftD);
            var rightInt = JsNumericConversions.ToInt32(rightD) & 0x1F;
            return JsValueCache.GetNumber(leftInt >> rightInt);
        }

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
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftIntVal = ToInt32(leftNumeric, context);
        var rightIntVal = ToInt32(rightNumeric, context) & 0x1F;
        return JsValueCache.GetNumber(leftIntVal >> rightIntVal);
    }

    private static object UnsignedRightShift(object? left, object? right, EvaluationContext context)
    {
        // Fast path for double operands (most common case)
        if (left is double leftD && right is double rightD)
        {
            var leftUInt = JsNumericConversions.ToUInt32(leftD);
            var rightInt = JsNumericConversions.ToInt32(rightD) & 0x1F;
            return JsValueCache.GetNumber(leftUInt >> rightInt);
        }

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

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("BigInts have no unsigned right shift, use >> instead", context);
        }

        var leftUIntVal = ToUInt32(leftNumeric, context);
        var rightIntVal = ToInt32(rightNumeric, context) & 0x1F;
        return JsValueCache.GetNumber(leftUIntVal >> rightIntVal);
    }

    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op,
        EvaluationContext context)
    {
        // Fast path for double operands (most common case)
        if (left is double leftD && right is double rightD)
        {
            var leftInt = JsNumericConversions.ToInt32(leftD);
            var rightInt = JsNumericConversions.ToInt32(rightD);
            return JsValueCache.GetNumber(int32Op(leftInt, rightInt));
        }

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

        var leftIntVal = JsNumericConversions.ToInt32(JsOps.ToNumber(leftNumeric, context));
        var rightIntVal = JsNumericConversions.ToInt32(JsOps.ToNumber(rightNumeric, context));
        return JsValueCache.GetNumber(int32Op(leftIntVal, rightIntVal));
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
            double d => JsValueCache.GetNumber(d + 1),
            _ => JsValueCache.GetNumber(JsOps.ToNumber(value, context) + 1)
        };
    }

    private static object DecrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value - BigInteger.One),
            double d => JsValueCache.GetNumber(d - 1),
            _ => JsValueCache.GetNumber(JsOps.ToNumber(value, context) - 1)
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

    private static bool InOperator(object? property, object? target, EvaluationContext context)
    {
        // Per ECMA-262 §13.10.2, the right-hand side of 'in' must be an object
        // Throw TypeError for primitives (boolean, number, string, null, undefined)
        if (target is not IJsPropertyAccessor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError(
                "Right-hand side of 'in' is not an object",
                context,
                context.RealmState));
            return false;
        }

        var propertyName = JsOps.GetRequiredPropertyName(property, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            // Use HasProperty which triggers evaluation for deferred namespaces per ES spec
            return moduleNamespace.HasProperty(propertyName);
        }

        if (propertyName.IsPrivateSlotName())
        {
            var handle = PropertyHandle.Resolve(target, propertyName, context, context.CurrentScope.IsStrict);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return handle.Exists();
        }

        // Use [[HasProperty]] semantics - check if property exists in object or its prototype chain
        return JsOps.HasProperty(target, propertyName, context);
    }

    private static bool InstanceofOperator(object? left, object? right, EvaluationContext context)
    {
        if (right is not IJsPropertyAccessor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not an object",
                context));
            return false;
        }

        var hasInstanceSymbol = Symbols.HasInstance;
        if (TryGetPropertyValue(right, hasInstanceSymbol, out var hasInstance, context))
        {
            // Check if an error was thrown during property access
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (!IsNullish(hasInstance))
            {
                if (hasInstance is not IJsCallable callable)
                {
                    context.SetThrow(StandardLibrary.CreateTypeError("@@hasInstance is not callable", context));
                    return false;
                }

                try
                {
                    var result = callable.Invoke([JsValue.FromObject(left)], JsValue.FromObject(right));
                    return JsOps.ToBoolean(result.ToObject());
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
            prototype is not IJsPropertyAccessor prototypeObject)
        {
            context.SetThrow(
                StandardLibrary.CreateTypeError("Function has non-object prototype in instanceof check", context));
            return false;
        }

        var current = JsOps.GetPrototypePointer(candidate);

        while (current is not null)
        {
            if (ReferenceEquals(current, prototypeObject))
            {
                return true;
            }

            current = JsOps.GetPrototypePointer(current);
        }

        return false;
    }

    private static string GetTypeofString(object? value)
    {
        return JsOps.GetTypeofString(value);
    }


    // Array/object destructuring uses iterator protocol (ECMA-262 §14.1.5).
    private static bool TryGetIteratorForDestructuring(object? value, EvaluationContext context,
        out IJsObjectLike? iterator, [MustDisposeResource] out IEnumerator<JsValue>? enumerator)
    {
        iterator = null;
        enumerator = null;

        if (value is TypedArrayBase typedArray)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            enumerator = EnumerateTypedArrayValues(typedArray);
            return true;
        }

        var iteratorTarget = value as IJsPropertyAccessor;
        var thisArg = value;
        if (iteratorTarget is null && value is not null && !ReferenceEquals(value, Symbol.Undefined))
        {
            iteratorTarget = ToObjectForDestructuring(value, context);
            thisArg = iteratorTarget;
        }

        if (iteratorTarget is not null)
        {
            var gotIterator = TryGetIteratorFromProtocols(iteratorTarget, context, out var iteratorCandidate);
            if (context.ShouldStopEvaluation)
            {
                iterator = null;
                enumerator = null;
                return false;
            }

            if (gotIterator && iteratorCandidate is not null)
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

            iterator = thisArg as IJsObjectLike;
            return iterator is not null;
        }

        switch (value)
        {
            case string s:
                enumerator = EnumerateStringCharacters(s);
                return true;
            case IEnumerable<JsValue> enumerable:
                enumerator = enumerable.GetEnumerator();
                return true;
        }

        return false;
    }


    [MustDisposeResource]
    private static IEnumerator<JsValue> EnumerateStringCharacters(string value)
    {
        IEnumerable<JsValue> Enumerate()
        {
            foreach (var ch in value)
            {
                yield return ch.ToString();
            }
        }

        return Enumerate().GetEnumerator();
    }

    [MustDisposeResource]
    private static IEnumerator<JsValue> EnumerateTypedArrayValues(TypedArrayBase typedArray)
    {
        IEnumerable<JsValue> Enumerate()
        {
            var length = typedArray.Length;
            for (var i = 0; i < length; i++)
            {
                yield return JsValue.FromObject(typedArray.GetValueForIndex(i));
            }
        }

        return Enumerate().GetEnumerator();
    }

    private static IJsObjectLike ToObjectForDestructuring(object? value, EvaluationContext context)
    {
        var realm = context.RealmState;
        switch (value)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
                throw StandardLibrary.ThrowTypeError("Cannot destructure undefined or null", context, realm);
            case IJsObjectLike objectLike:
                return objectLike;
        }

        if (realm is not null && StandardLibrary.TryGetObject(value, realm, out var coerced))
        {
            return coerced;
        }

        var obj = new JsObject();
        if (realm?.ObjectPrototype is not null)
        {
            obj.SetPrototype(realm.ObjectPrototype);
        }

        return obj;
    }

    private static JsObject CreateGeneratorIteratorObject(
        Func<IReadOnlyList<JsValue>, JsValue> next,
        Func<IReadOnlyList<JsValue>, JsValue> @return,
        Func<IReadOnlyList<JsValue>, JsValue> @throw,
        JsObject? prototype)
    {
        var iterator = new JsObject();
        if (prototype is not null)
        {
            iterator.SetPrototype(prototype);
        }

        iterator.SetProperty("next", new HostFunction(next));
        iterator.SetProperty("return", new HostFunction(@return));
        iterator.SetProperty("throw", new HostFunction(@throw));
        return iterator;
    }
}
