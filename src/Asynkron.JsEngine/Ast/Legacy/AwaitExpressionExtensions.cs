#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateAwait(this AwaitExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Async generators execute on the generator IR path via ExecutionPlanRunner.
        // When an await expression runs under that executor, the execution environment
        // carries a back-reference to the active generator instance so we can surface
        // pending promises instead of blocking. In that case the generator instance
        // is responsible for evaluating the awaited expression and managing resume.
        if (environment.TryGetObject<ExecutionPlanRunner>(Symbol.GeneratorInstanceSymbol, out var generator))
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    expression.Expression,
                    out var awaitedProgram,
                    out var failureReason))
            {
                throw new NotSupportedException(
                    $"Async generator await operand could not be lowered to expression bytecode: {failureReason}");
            }

            return generator.EvaluateAwaitInGenerator(
                expression.GetAwaitStateKey(),
                awaitedProgram,
                environment,
                context);
        }

        var awaitedValue = expression.Expression.EvaluateDynamicExpressionOperand(
            environment,
            context,
            "Dynamic await operand");
        if (context.ShouldStopEvaluation)
        {
            return awaitedValue;
        }

        // Always await asynchronously: wrap non-promises with Promise.resolve and drive through scheduler.
        if (!awaitedValue.IsObject || !AwaitScheduler.IsPromiseLike(awaitedValue))
        {
            var promiseCtor = context.RealmState.PromiseConstructor;
            JsObject? wrappedPromise = null;

            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("resolve", out var resolveValue) &&
                resolveValue.TryGetObject<IJsCallable>(out var resolveCallable))
            {
                var resolveResult = resolveCallable.Invoke(new SingleValueArgs(awaitedValue), JsValue.FromObjectUnsafe(promiseCtor));
                if (resolveResult.IsObject)
                {
                    wrappedPromise = resolveResult.AsObject();
                }
            }

            if (wrappedPromise is null)
            {
                // Fallback: create a resolved promise in the current realm.
                var engine = context.RealmState.Engine;
                var promise = engine?.CreateRealmPromise();
                promise?.Resolve(awaitedValue);
                wrappedPromise = promise?.JsObject;
            }

            awaitedValue = wrappedPromise is not null ? new JsValue(wrappedPromise) : awaitedValue;
        }

        var completed = AwaitScheduler.TryAwaitPromiseSync(
            awaitedValue,
            context,
            out var resolvedValue,
            context.DrainAwaitMicrotasks,
            blockUntilSettled: true);

        if (!completed)
        {
            if (!context.IsThrow)
            {
                throw new InvalidOperationException("Legacy await did not settle synchronously.");
            }

            return JsValue.Undefined;
        }

        return resolvedValue;
    }

    private static Symbol GetAwaitStateKey(this AwaitExpression expression)
    {
        return ((IAstCacheable<Symbol>)expression).GetOrCreateCache();
    }
}
