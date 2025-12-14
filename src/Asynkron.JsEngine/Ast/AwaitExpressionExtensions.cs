using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(AwaitExpression expression)
    {
        private JsValue EvaluateAwait(JsEnvironment environment,
            EvaluationContext context)
        {
            // Async generators execute on the generator IR path via TypedGeneratorInstance.
            // When an await expression runs under that executor, the execution environment
            // carries a back-reference to the active generator instance so we can surface
            // pending promises instead of blocking. In that case the generator instance
            // is responsible for evaluating the awaited expression and managing resume.
            if (environment.TryGet(Symbol.GeneratorInstanceSymbol, out var instanceObj) &&
                instanceObj is TypedGeneratorInstance generator)
            {
                return JsValue.FromObject(generator.EvaluateAwaitInGenerator(expression, environment, context));
            }

            var awaitedValue = EvaluateExpression(expression.Expression, environment, context);
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
                    resolveValue is IJsCallable resolveCallable)
                {
                    var resolveResult = resolveCallable.Invoke([awaitedValue], JsValue.FromObject(promiseCtor));
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

            if (!AwaitScheduler.TryAwaitPromiseSync(
                    awaitedValue,
                    context,
                    out var resolvedValue,
                    context.DrainAwaitMicrotasks))
            {
                return resolvedValue;
            }

            return resolvedValue;
        }

        private Symbol? GetAwaitStateKey()
        {
            if (expression.Source is null)
            {
                return null;
            }

            var key = $"__await_state_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
            return Symbol.Intern(key);
        }
    }
}
