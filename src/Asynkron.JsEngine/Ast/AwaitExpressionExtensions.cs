using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(AwaitExpression expression)
    {
        private object? EvaluateAwait(JsEnvironment environment,
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
                return generator.EvaluateAwaitInGenerator(expression, environment, context);
            }

            var awaited = EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaited;
            }

            // For top-level await (module level, no generator), use synchronous blocking.
            // This ensures promises are resolved before continuing.
            if (!AwaitScheduler.TryAwaitPromiseSync(awaited, context, out var resolved))
            {
                // TryAwaitPromiseSync returns false if there was a rejection that set context.IsThrow
                return resolved;
            }

            return resolved;
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
