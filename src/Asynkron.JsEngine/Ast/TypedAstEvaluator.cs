#region

using System.Collections.Immutable;
using System.Globalization;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private const string GeneratorBrandPropertyName = "__generator_brand__";

    private static readonly string IteratorSymbolPropertyName = SymbolKeys.Iterator;

    private static readonly object GeneratorBrandMarker = new();

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
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
                context.SetThrow(JsValue.FromObjectUnsafe(error));
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

    /// <summary>
    /// Fast path for iteration: returns an IEnumerator&lt;JsValue&gt; for known iterable types
    /// without going through the full iterator protocol (which allocates {done, value} objects).
    /// Falls back to null for types that need the full protocol.
    ///
    /// The enumerators are spec-compliant:
    /// - JsArray: checks length on each iteration, only used if no custom indexed properties
    /// - String: iterates by Unicode code points (handles surrogate pairs)
    ///
    /// NOTE: TypedArray is NOT fast-pathed because resizable buffer shrink requires proper error propagation.
    /// </summary>
    [MustDisposeResource]
    private static IEnumerator<JsValue>? TryGetFastEnumeratorForIteration(in JsValue value)
    {
        // JsArray - fast path only if no custom indexed properties (getters/setters)
        // Arrays with Object.defineProperty on numeric indices need full iterator protocol
        if (value is { Kind: JsValueKind.Object, ObjectValue: JsArray { HasCustomIndexedProperties: false } array })
        {
            return array.GetEnumerator();
        }

        // Strings - Unicode code point enumeration (handles surrogate pairs)
        // Strings are immutable so no modification concerns
        if (value is { Kind: JsValueKind.String, ObjectValue: string s })
        {
            return EnumerateStringCharacters(s);
        }

        // Fall back to null - caller should use full iterator protocol
        // TypedArray: resizable buffer shrink needs proper error propagation
        return null;
    }

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
    // Per ECMA-262 §7.4.1/§7.4.2 (GetIterator / GetAsyncIterator) via @@iterator/@@asyncIterator.
    private static bool TryGetIteratorFromProtocols(object? iterable, EvaluationContext context,
        out IJsObjectLike? iterator)
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
            var keysPreview = string.Join(',', objectLike.Keys.Take(8));
            logger?.LogInformation("TryGetIteratorFromProtocols keys={Keys}", keysPreview);
        }

        var iterableValue = JsValue.FromObjectUnsafe(iterable);
        if (accessor.TryInvokeSymbolMethod(iterableValue, Symbols.AsyncIterator, context, out var asyncIterator))
        {
            logger?.LogInformation("TryGetIteratorFromProtocols asyncIterator invoked stop={Stop} kind={IterKind}",
                context.ShouldStopEvaluation,
                asyncIterator.Kind);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (asyncIterator.TryGetObject<IJsObjectLike>(out var asyncObj))
            {
                iterator = asyncObj;
                return true;
            }

            var typeError = StandardLibrary.CreateTypeError("Iterator is not an object", context, context.RealmState);
            context.SetThrow(JsValue.FromObjectUnsafe(typeError));
            return false;
        }

        if (accessor.TryInvokeSymbolMethod(iterableValue, Symbols.Iterator, context, out var iteratorValue))
        {
            logger?.LogInformation("TryGetIteratorFromProtocols iterator invoked stop={Stop} kind={IterKind}",
                context.ShouldStopEvaluation,
                iteratorValue.Kind);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (iteratorValue.TryGetObject<IJsObjectLike>(out var iteratorObj))
            {
                iterator = iteratorObj;
                return true;
            }

            var typeError = StandardLibrary.CreateTypeError("Iterator is not an object", context, context.RealmState);
            context.SetThrow(JsValue.FromObjectUnsafe(typeError));
            return false;
        }

        return false;
    }


    private static bool IsPromiseLike(JsValue candidate)
    {
        return AwaitScheduler.IsPromiseLike(candidate);
    }

    // ToObject for iteration lookup: primitives must be wrapped so @@iterator can be
    // found on their prototypes (ES2024 GetIterator/ToObject step).
    private static object NormalizeIterableTarget(JsValue jsValue, EvaluationContext context)
    {
        if (jsValue.IsNull || jsValue.IsUndefined)
        {
            var realm = context.RealmState;
            throw StandardLibrary.ThrowTypeError("Cannot iterate over null or undefined", context, realm);
        }

        // For objects, check if already an IJsPropertyAccessor
        if (jsValue is { Kind: JsValueKind.Object, ObjectValue: IJsPropertyAccessor accessor })
        {
            return accessor;
        }

        // Use the JsValue overload for proper type handling
        return ToObjectForDestructuringJsValue(jsValue, context);
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


    private static IEnumerable<JsValue> EnumeratePropertyKeys(JsValue value)
    {
        // Per ES spec, for-in over null or undefined should not iterate (no properties to enumerate)
        if (value.IsNull || value.IsUndefined)
        {
            yield break;
        }

        switch (value.Kind)
        {
            case JsValueKind.Object when value.ObjectValue is JsArray array:
            {
                // First, enumerate numeric indices (array elements)
                for (var i = 0; i < array.Items.Count; i++)
                {
                    yield return JsValue.FromString(i.ToString(CultureInfo.InvariantCulture));
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

                        yield return JsValue.FromString(key);
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

            case JsValueKind.Object when value.ObjectValue is TypedArrayBase typedArray:
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
                    if (desc is null or { Enumerable: false })
                    {
                        continue;
                    }

                    yield return JsValue.FromString(key);
                }

                yield break;
            }

            case JsValueKind.String when value.ObjectValue is string s:
            {
                for (var i = 0; i < s.Length; i++)
                {
                    yield return JsValue.FromString(i.ToString(CultureInfo.InvariantCulture));
                }

                yield break;
            }

            case JsValueKind.Object when value.ObjectValue is IJsObjectLike accessor:
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

                        yield return JsValue.FromString(key);
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

    private static DelegatedYieldState CreateDelegatedState(JsValue iterable, EvaluationContext context)
    {
        var iteratorTarget = NormalizeIterableTarget(iterable, context);
        if (context.ShouldStopEvaluation)
        {
            return DelegatedYieldState.FromEnumerable([]);
        }

        if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
        {
            return DelegatedYieldState.FromIterator(iterator);
        }

        if (context.ShouldStopEvaluation)
        {
            return DelegatedYieldState.FromEnumerable([]);
        }

        throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
    }


    private static ImmutableArray<JsValue> FreezeArguments(ImmutableArray<JsValue>.Builder builder)
    {
        return builder.Count == builder.Capacity
            ? builder.MoveToImmutable()
            : builder.ToImmutable();
    }

    private static JsValue CreateRejectedPromise(JsValue reason, JsEnvironment environment)
    {
        if (!environment.TryGetObject<IJsPropertyAccessor>(Symbol.PromiseIdentifier, out var accessor) ||
            !accessor.TryGetProperty("reject", out var rejectValue) ||
            !rejectValue.TryGetCallable(out var rejectCallable))
        {
            return reason;
        }

        try
        {
            return rejectCallable.Invoke([reason], JsValue.FromObjectUnsafe(accessor));
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }

    private static JsValue CreateResolvedPromise(JsValue value, JsEnvironment environment)
    {
        var resolveCandidate = JsValue.Undefined;
        if (!environment.TryGetObject<IJsPropertyAccessor>(Symbol.PromiseIdentifier, out var accessor) ||
            !accessor.TryGetProperty("resolve", out resolveCandidate) ||
            !resolveCandidate.TryGetCallable(out var resolveCallable))
        {
            environment.RealmState?.Logger?.LogInformation(
                "CreateResolvedPromise falling back (hasAccessor={HasAccessor}, hasResolve={HasResolve}, resolveCallable={ResolveCallable})",
                accessor is not null,
                accessor?.TryGetProperty("resolve", out _) ?? false,
                resolveCandidate.TryGetCallable(out _));
            return value;
        }

        try
        {
            return resolveCallable.Invoke([value], JsValue.FromObjectUnsafe(accessor));
        }
        catch (ThrowSignal signal)
        {
            return signal.ThrownValue;
        }
    }


    // SpreadElement runtime semantics (ECMA-262 §12.2.5.2) use GetIterator on the operand.
    private static IEnumerable<JsValue> EnumerateSpread(JsValue value, EvaluationContext context)
    {
        if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
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
        logger?.LogInformation(
            "EnumerateSpread start valueKind={Kind} hasIterator={HasIterator} hasEnumerator={HasEnum}",
            value.Kind,
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
                    iterator?.IteratorClose(context);

                    throw new ThrowSignal(context.FlowValue);
                }

                if (done)
                {
                    logger?.LogInformation("EnumerateSpread done at index={Index}", index);
                    yield break;
                }

                if (index < 5 || index % 1000 == 0)
                {
                    logger?.LogInformation("EnumerateSpread yield index={Index} kind={Kind}", index, item.Kind);
                }

                yield return item;
                index++;
            }
        }
        finally
        {
            if (iterator is not null && context.IsThrow)
            {
                iterator.IteratorClose(context);
            }

            enumerator?.Dispose();
        }
    }


    [Obsolete("Use JsValue overloads instead to avoid boxing")]
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

    /// <summary>
    /// JsValue overload for BigInt or numeric operations - avoids boxing.
    /// </summary>
    private static JsValue PerformBigIntOrNumericOperationJsValue(
        in JsValue left,
        in JsValue right,
        Func<JsBigInt, JsBigInt, EvaluationContext, object> bigIntOp,
        Func<double, double, double> numericOp,
        EvaluationContext context)
    {
        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        // Both are numbers - most common case
        if (leftNumeric.IsNumber && rightNumeric.IsNumber)
        {
            return JsValue.FromDouble(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
        }

        // Both are BigInt
        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            return JsValue.FromObjectUnsafe(bigIntOp(leftNumeric.AsBigInt(), rightNumeric.AsBigInt(), context));
        }

        // Mixed types - error
        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        // Fallback (shouldn't reach here normally)
        return JsValue.FromDouble(numericOp(leftNumeric.NumberValue, rightNumeric.NumberValue));
    }

    /// <summary>
    /// JsValue overload for BitwiseNot. Avoids boxing for numeric types.
    /// </summary>
    private static JsValue BitwiseNotJsValue(in JsValue operand, EvaluationContext context)
    {
        // Fast path for number (most common case)
        if (operand.IsNumber)
        {
            var int32 = JsNumericConversions.ToInt32(operand.NumberValue);
            return new JsValue((double)~int32);
        }

        var numeric = JsOps.ToNumeric(operand, context);
        if (context.IsThrow)
        {
            return context.FlowValue;
        }

        if (numeric is { Kind: JsValueKind.BigInt, ObjectValue: JsBigInt bigInt })
        {
            return (JsValue)~bigInt;
        }

        var int32Val = JsNumericConversions.ToInt32(JsOps.ToNumber(numeric, context));
        return new JsValue((double)~int32Val);
    }

    /// <summary>
    /// JsValue overload for left shift - avoids boxing.
    /// </summary>
    private static JsValue LeftShiftJsValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path for number operands (most common case)
        if (left.IsNumber && right.IsNumber)
        {
            var leftInt = JsNumericConversions.ToInt32(left.NumberValue);
            var rightInt = JsNumericConversions.ToInt32(right.NumberValue) & 0x1F;
            return JsValue.FromDouble(leftInt << rightInt);
        }

        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            var rightBigInt = rightNumeric.AsBigInt();
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return JsValue.FromObjectUnsafe(leftNumeric.AsBigInt() << (int)rightBigInt.Value);
        }

        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftIntVal = JsNumericConversions.ToInt32(leftNumeric.NumberValue);
        var rightIntVal = JsNumericConversions.ToInt32(rightNumeric.NumberValue) & 0x1F;
        return JsValue.FromDouble(leftIntVal << rightIntVal);
    }

    /// <summary>
    /// JsValue overload for right shift - avoids boxing.
    /// </summary>
    private static JsValue RightShiftJsValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path for number operands (most common case)
        if (left.IsNumber && right.IsNumber)
        {
            var leftInt = JsNumericConversions.ToInt32(left.NumberValue);
            var rightInt = JsNumericConversions.ToInt32(right.NumberValue) & 0x1F;
            return JsValue.FromDouble(leftInt >> rightInt);
        }

        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            var rightBigInt = rightNumeric.AsBigInt();
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return JsValue.FromObjectUnsafe(leftNumeric.AsBigInt() >> (int)rightBigInt.Value);
        }

        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftIntVal = JsNumericConversions.ToInt32(leftNumeric.NumberValue);
        var rightIntVal = JsNumericConversions.ToInt32(rightNumeric.NumberValue) & 0x1F;
        return JsValue.FromDouble(leftIntVal >> rightIntVal);
    }

    /// <summary>
    /// JsValue overload for unsigned right shift - avoids boxing.
    /// </summary>
    private static JsValue UnsignedRightShiftJsValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        // Fast path for number operands (most common case)
        if (left.IsNumber && right.IsNumber)
        {
            var leftUInt = JsNumericConversions.ToUInt32(left.NumberValue);
            var rightInt = JsNumericConversions.ToInt32(right.NumberValue) & 0x1F;
            return JsValue.FromDouble(leftUInt >> rightInt);
        }

        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("BigInts have no unsigned right shift, use >> instead", context);
        }

        var leftUIntVal = JsNumericConversions.ToUInt32(leftNumeric.NumberValue);
        var rightIntVal = JsNumericConversions.ToInt32(rightNumeric.NumberValue) & 0x1F;
        return JsValue.FromDouble(leftUIntVal >> rightIntVal);
    }

    /// <summary>
    /// JsValue overload for BigInt or Int32 operations - avoids boxing.
    /// </summary>
    private static JsValue PerformBigIntOrInt32OperationJsValue(
        in JsValue left,
        in JsValue right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op,
        EvaluationContext context)
    {
        // Fast path for number operands (most common case)
        if (left.IsNumber && right.IsNumber)
        {
            var leftInt = JsNumericConversions.ToInt32(left.NumberValue);
            var rightInt = JsNumericConversions.ToInt32(right.NumberValue);
            return JsValue.FromDouble(int32Op(leftInt, rightInt));
        }

        var leftNumeric = ToNumericValue(left, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = ToNumericValue(right, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric.IsBigInt && rightNumeric.IsBigInt)
        {
            return JsValue.FromObjectUnsafe(bigIntOp(leftNumeric.AsBigInt(), rightNumeric.AsBigInt()));
        }

        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftIntVal = JsNumericConversions.ToInt32(leftNumeric.NumberValue);
        var rightIntVal = JsNumericConversions.ToInt32(rightNumeric.NumberValue);
        return JsValue.FromDouble(int32Op(leftIntVal, rightIntVal));
    }

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        return JsOps.TryGetPropertyValue(target, propertyName, out value);
    }

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
    private static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context = null)
    {
        return JsOps.TryGetPropertyValue(target, propertyKey, out value, context);
    }

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
    private static void AssignPropertyValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context = null)
    {
        JsOps.AssignPropertyValue(target, propertyKey, value, context);
    }

    /// <summary>
    /// JsValue overload for 'in' operator - avoids boxing.
    /// </summary>
    private static bool InOperatorJsValue(in JsValue property, in JsValue target, EvaluationContext context)
    {
        // Per ECMA-262 §13.10.2, the right-hand side of 'in' must be an object
        if (target.Kind != JsValueKind.Object || target.ObjectValue is not IJsPropertyAccessor accessor)
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

        if (accessor is ModuleNamespace moduleNamespace)
        {
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

        return JsOps.HasProperty(target, propertyName);
    }

    /// <summary>
    /// JsValue overload for 'instanceof' operator - avoids boxing.
    /// </summary>
    private static bool InstanceofOperatorJsValue(in JsValue left, in JsValue right, EvaluationContext context)
    {
        if (right.Kind != JsValueKind.Object || right.ObjectValue is not IJsPropertyAccessor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not an object",
                context));
            return false;
        }

        var hasInstanceKey = SymbolKeys.HasInstance;
        if (JsOps.TryGetPropertyValue(right, hasInstanceKey, out var hasInstanceJs, context))
        {
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            if (!hasInstanceJs.IsNullOrUndefined)
            {
                if (!hasInstanceJs.TryGetObject<IJsCallable>(out var callable))
                {
                    context.SetThrow(StandardLibrary.CreateTypeError("@@hasInstance is not callable", context));
                    return false;
                }

                try
                {
                    var result = callable.Invoke([left], right);
                    return result.IsTruthy;
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

        if (right.ObjectValue is IJsCallable)
        {
            return OrdinaryHasInstanceJsValue(left, right, context);
        }

        context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not callable",
            context));
        return false;
    }

    private static bool OrdinaryHasInstanceJsValue(in JsValue candidate, in JsValue constructor, EvaluationContext context)
    {
        if (constructor.ObjectValue is not IJsCallable)
        {
            return false;
        }

        if (candidate.Kind != JsValueKind.Object)
        {
            return false;
        }

        if (!JsOps.TryGetPropertyValue(constructor, "prototype", out var prototypeJs, context) ||
            !prototypeJs.TryGetObject<IJsPropertyAccessor>(out var prototypeObject))
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

    // Array/object destructuring uses iterator protocol (ECMA-262 §14.1.5).
    private static bool TryGetIteratorForDestructuring(JsValue jsValue, EvaluationContext context,
        out IJsObjectLike? iterator, [MustDisposeResource] out IEnumerator<JsValue>? enumerator)
    {
        iterator = null;
        enumerator = null;

        // Fast path for strings
        if (jsValue is { Kind: JsValueKind.String, ObjectValue: string s })
        {
            enumerator = EnumerateStringCharacters(s);
            return true;
        }

        // Handle objects (including TypedArrays)
        if (jsValue.Kind == JsValueKind.Object)
        {
            var objValue = jsValue.ObjectValue;

            // Fast path for TypedArray
            if (objValue is TypedArrayBase typedArray)
            {
                if (typedArray.IsDetachedOrOutOfBounds())
                {
                    throw typedArray.CreateOutOfBoundsTypeError();
                }

                enumerator = EnumerateTypedArrayValues(typedArray);
                return true;
            }

            // Try iterator protocol on property accessor (must be before IEnumerable fallback
            // because JsArray implements IEnumerable<JsValue> but needs iterator protocol for
            // tests that modify Array.prototype[Symbol.iterator])
            if (objValue is IJsPropertyAccessor propertyAccessor)
            {
                var gotIterator = TryGetIteratorFromProtocols(propertyAccessor, context, out var iteratorCandidate);
                if (context.ShouldStopEvaluation)
                {
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
                if (propertyAccessor.TryGetProperty("next", out var nextVal) &&
                    nextVal.TryGetObject<IJsCallable>(out _))
                {
                    iterator = objValue as IJsObjectLike;
                    return iterator is not null;
                }
            }

            // Fallback for non-JS IEnumerable<JsValue> (e.g., host-provided collections)
            // This must be AFTER the iterator protocol check because JsArray implements IEnumerable
            if (objValue is IEnumerable<JsValue> enumerable and not IJsPropertyAccessor)
            {
                enumerator = enumerable.GetEnumerator();
                return true;
            }

            return false;
        }

        // For primitives (numbers, booleans), they cannot be directly iterated
        return false;
    }

    [MustDisposeResource]
    private static IEnumerator<JsValue> EnumerateStringCharacters(string value)
    {
        return Enumerate().GetEnumerator();

        IEnumerable<JsValue> Enumerate()
        {
            // Iterate by Unicode code points, not UTF-16 code units.
            // Astral plane characters (U+10000 and above) are stored as surrogate pairs
            // and should be yielded as a single string (Test262: string-astral.js)
            var i = 0;
            while (i < value.Length)
            {
                var ch = value[i];
                if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    // Surrogate pair - yield both chars as one string
                    yield return value.Substring(i, 2);
                    i += 2;
                }
                else
                {
                    // Single char (BMP character or unpaired surrogate)
                    yield return ch.ToString();
                    i++;
                }
            }
        }
    }

    [MustDisposeResource]
    private static IEnumerator<JsValue> EnumerateTypedArrayValues(TypedArrayBase typedArray)
    {
        return Enumerate().GetEnumerator();

        IEnumerable<JsValue> Enumerate()
        {
            // Check length and bounds on each iteration - buffer may be resized during iteration
            // (Test262: typedarray-backed-by-resizable-buffer-*.js tests)
            for (var i = 0; i < typedArray.Length; i++)
            {
                // Check if buffer is still valid on each iteration
                if (typedArray.IsDetachedOrOutOfBounds())
                {
                    yield break;
                }

                yield return typedArray.GetValueForIndex(i);
            }
        }
    }

    /// <summary>
    /// JsValue overload - avoids boxing when caller has JsValue.
    /// </summary>
    private static IJsObjectLike ToObjectForDestructuringJsValue(JsValue jsValue, EvaluationContext context)
    {
        var realm = context.RealmState;

        if (jsValue.IsNull || jsValue.IsUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot destructure undefined or null", context, realm);
        }

        // Fast path: if it's already an IJsObjectLike, return directly
        if (jsValue is { Kind: JsValueKind.Object, ObjectValue: IJsObjectLike objectLike })
        {
            return objectLike;
        }

        // For primitives, extract and coerce
        var value = jsValue.Kind switch
        {
            JsValueKind.Boolean => jsValue.NumberValue != 0,
            JsValueKind.Number => jsValue.NumberValue,
            _ => jsValue.ObjectValue
        };

        if (StandardLibrary.TryGetObject(value, realm, out var coerced))
        {
            return coerced;
        }

        var obj = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            obj.SetPrototype(realm.ObjectPrototype);
        }

        return obj;
    }

    [Obsolete("Use JsValue overloads instead to avoid boxing")]
    private static IJsObjectLike ToObjectForDestructuring(object? value, EvaluationContext context)
    {
        var realm = context.RealmState;

        // Handle boxed JsValue structs - unwrap and check for null/undefined
        if (value is JsValue jsValue)
        {
            return ToObjectForDestructuringJsValue(jsValue, context);
        }

        switch (value)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
                throw StandardLibrary.ThrowTypeError("Cannot destructure undefined or null", context, realm);
            case IJsObjectLike objectLike:
                return objectLike;
        }

        if (StandardLibrary.TryGetObject(value, realm, out var coerced))
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

        iterator.SetProperty("next", (JsValue)new HostFunction(next));
        iterator.SetProperty("return", (JsValue)new HostFunction(@return));
        iterator.SetProperty("throw", (JsValue)new HostFunction(@throw));
        return iterator;
    }
}
