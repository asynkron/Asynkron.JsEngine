#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        internal JsValue EvaluateAwaitInGeneratorLegacy(
            AwaitExpression expression,
            JsEnvironment environment,
            EvaluationContext context)
        {
            var awaitKey = expression.GetAwaitStateKey();

            if (!AsyncStateRef.AsyncStepMode)
            {
                var awaitedValueSync = expression.Expression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                TryAwaitPromise(awaitedValueSync, context, out var resolvedSync);
                return resolvedSync;
            }

            if (environment.TryGetObject<AwaitState>(awaitKey, out var state) &&
                state.HasResult)
            {
                var result = state.Result;
                var isThrow = state.IsThrow;
                RecordAwaitKeyForReset(awaitKey);

                if (isThrow)
                {
                    throw new ThrowSignal(result);
                }

                return result;
            }

            var awaitedValue = expression.Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            var existingState = JsValue.FromObjectUnsafe(new AwaitState());
            if (environment.HasBinding(awaitKey))
            {
                environment.AssignJsValue(awaitKey, existingState);
            }
            else
            {
                environment.DefineJsValue(awaitKey, existingState);
            }

            if (TryResolvePromiseOrYield(awaitedValue, context, out var resolved))
            {
                return resolved;
            }

            if (!HasPendingPromise())
            {
                return resolved;
            }

            AsyncStateRef.PendingAwaitKey = awaitKey;
            _state = GeneratorState.Suspended;
            _programCounter = _currentInstructionIndex;
            context.SetPendingAwait();
            return JsValue.Undefined;
        }
    }
}
