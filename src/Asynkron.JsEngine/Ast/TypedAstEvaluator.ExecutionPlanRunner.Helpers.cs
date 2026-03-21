#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static JsValue CreateIteratorResult(JsValue value, bool done)
        {
            // Use singleton for the common done case with undefined value
            if (done && value.IsUndefined)
            {
                return IteratorResultObject.DoneUndefined.AsJsValue;
            }

            return IteratorResultObjectPool.Rent(value, done).AsJsValue;
        }

        private static IteratorDriverState CreateIteratorDriverState(
            JsValue iterable,
            IteratorDriverKind kind,
            EvaluationContext context)
        {
            // FAST PATH: Use IEnumerator<JsValue> for arrays to avoid iterator object allocation.
            // This bypasses creating iterator objects with next() methods for JsArray.
            var fastEnumerator = TryGetFastEnumeratorForIteration(iterable);
            if (fastEnumerator is not null)
            {
                return new IteratorDriverState
                {
                    IteratorObject = null,
                    Enumerator = fastEnumerator,
                    IsAsyncIterator = kind == IteratorDriverKind.Await,
                    NextMethod = null
                };
            }

            // SLOW PATH: Full iterator protocol for custom iterables
            var iteratorTarget = NormalizeIterableTarget(iterable, context);

            if (!TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) || iterator is null)
            {
                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var nextMethod = iterator.GetIteratorNextCallable(context);
            return new IteratorDriverState
            {
                IteratorObject = iterator,
                Enumerator = null,
                IsAsyncIterator = kind == IteratorDriverKind.Await,
                NextMethod = nextMethod
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? /* intentional */ value)
        {
            // Handle case where value is already a boxed JsValue
            var jsVal = value is JsValue jv ? jv : JsValue.FromObjectUnsafe(value);
            StoreSymbolValueJsValue(environment, symbol, jsVal);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void StoreSymbolValueJsValue(JsEnvironment environment, Symbol symbol, JsValue value)
        {
            // DefineOrAssignJsValue is O(1) on the current environment -
            // it only looks at environment.Values, no scope chain walk.
            // This is optimal for generator symbols defined in the execution environment.
            environment.DefineOrAssignJsValue(symbol, value);
        }

        /// <summary>
        /// Gets the actual slot index, applying offset for GlobalEnvironment access in script mode.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private int GetActualSlotIndex(JsEnvironment environment, int slotIndex)
        {
            // Apply offset only when accessing the GlobalEnvironment (stored in _closure for scripts).
            // Child environments created during execution have their own fresh slots.
            var isClosure = ReferenceEquals(environment, _closure);
            if (_slotOffset > 0 && !isClosure)
            {
                _realmState.Logger?.LogWarning(
                    "[DEBUG] GetActualSlotIndex: _slotOffset={Offset} but env != _closure. env.ScopeId={EnvScope}, _closure?.ScopeId={ClosureScope}, sameRef={Same}",
                    _slotOffset, environment.ScopeId, _closure?.ScopeId, isClosure);
            }
            return _slotOffset > 0 && isClosure
                ? slotIndex + _slotOffset
                : slotIndex;
        }

        /// <summary>
        /// Stores a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based storage if slot index is invalid.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void StoreValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex, JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
                environment.SetSlotDirect(actualSlotIndex, value);
                // Also update dictionary for symbol-based lookups elsewhere
            }

            environment.DefineOrAssignJsValue(symbol, value);
        }

        /// <summary>
        /// Reads a value using pre-resolved slot index for O(1) access.
        /// Falls back to dictionary-based lookup if slot index is invalid.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryGetValueBySlot(JsEnvironment environment, Symbol symbol, int slotIndex,
            out JsValue value)
        {
            if (slotIndex >= 0 && environment.HasSlots)
            {
                var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
                value = environment.GetSlotRef(actualSlotIndex);
                return true;
            }

            return TryGetSymbolValueJsValue(environment, symbol, out value);
        }

        /// <summary>
        /// Creates a JsVariable for slot-based access, applying offset for GlobalEnvironment.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsVariable CreateSlotVariable(JsEnvironment environment, int slotIndex)
        {
            var actualSlotIndex = GetActualSlotIndex(environment, slotIndex);
            return new JsVariable(environment, actualSlotIndex);
        }

        private static bool TryGetSymbolValueJsValue(JsEnvironment environment, Symbol symbol, out JsValue value)
        {
            if (environment.TryGetJsValue(symbol, out value))
            {
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        private static JsValue FinishExternalCompletion(ResumeMode mode, JsValue value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value),
                _ => CreateIteratorResult(value, true)
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryEvaluateSimpleExpression(
            ExpressionNode expression,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue value)
        {
            switch (expression)
            {
                case LiteralExpression { Value: var literalValue }:
                    value = literalValue;
                    return true;

                case IdentifierExpression identifier:
                    value = EvaluateSimpleIdentifier(identifier, environment, context);
                    return true;

                case UnaryExpression { Operator: UnaryOperator.LogicalNot } unary:
                    if (!TryEvaluateSimpleExpression(unary.Operand, environment, context, out var operandValue))
                    {
                        value = default;
                        return false;
                    }

                    value = operandValue.IsTruthy ? JsValue.False : JsValue.True;
                    return true;

                case BinaryExpression binary:
                    return TryEvaluateSimpleBinaryExpression(binary, environment, context, out value);

                default:
                    value = default;
                    return false;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue EvaluateSimpleIdentifier(
            IdentifierExpression identifier,
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (ReferenceEquals(identifier.Name, Symbol.Arguments))
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue)
                    ? argumentsValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (!context.AllowIdentifierCache)
            {
                return environment.TryGetIdentifierJsValue(identifier.Name, context, out var resolvedValue)
                    ? resolvedValue
                    : HandleIdentifierNotFound(identifier.Name, context);
            }

            if (environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
            {
                return slotValue;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryEvaluateSimpleBinaryExpression(
            BinaryExpression expression,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue value)
        {
            if (!TryEvaluateSimpleExpression(expression.Left, environment, context, out var leftValue))
            {
                value = default;
                return false;
            }

            if (context.ShouldStopEvaluation)
            {
                value = leftValue;
                return true;
            }

            switch (expression.Operator)
            {
                case BinaryOperator.LogicalAnd when !leftValue.IsTruthy:
                case BinaryOperator.LogicalOr when leftValue.IsTruthy:
                case BinaryOperator.NullishCoalescing when !leftValue.IsNullish:
                    value = leftValue;
                    return true;
            }

            if (!TryEvaluateSimpleExpression(expression.Right, environment, context, out var rightValue))
            {
                value = default;
                return false;
            }

            if (context.ShouldStopEvaluation)
            {
                value = rightValue;
                return true;
            }

            if (expression.Operator == BinaryOperator.Add)
            {
                var fastAdd = ProfileCompoundAdd(leftValue, rightValue);
                value = !fastAdd.IsUndefined
                    ? fastAdd
                    : ProfileApplyBinaryOperator(expression.Operator, leftValue, rightValue, context);
                return true;
            }

            value = expression.Operator switch
            {
                BinaryOperator.LessThan or
                BinaryOperator.LessThanOrEqual or
                BinaryOperator.GreaterThan or
                BinaryOperator.GreaterThanOrEqual =>
                    ProfileBranchCompare(expression.Operator, leftValue, rightValue, context),
                _ => ProfileApplyBinaryOperator(expression.Operator, leftValue, rightValue, context)
            };

            return true;
        }
    }
}
