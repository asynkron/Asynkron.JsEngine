using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

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
            if (environment.TryGet(GeneratorInstanceSymbol, out var instanceObj) &&
                instanceObj is TypedGeneratorInstance generator)
            {
                return generator.EvaluateAwaitInGenerator(expression, environment, context);
            }

            var awaited = EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaited;
            }

            // Plain async functions now honor pending promises via the shared scheduler.
            object? pendingPromise = null;
            if (!AwaitScheduler.TryAwaitPromiseOrSchedule(awaited, true, ref pendingPromise, context,
                    out var resolved))
            {
                if (context.IsThrow || context.IsReturn)
                {
                    return resolved;
                }

                // if (pendingPromise is JsObject promise && AwaitScheduler.IsPromiseLike(promise))
                // {
                //     return new PendingAwaitResult(promise);
                // }
            }

            return resolved;
        }
    }

extension(AwaitExpression expression)
    {
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
