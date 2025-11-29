using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ForEachStatement statement)
    {
        private object? EvaluateForEach(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            if (statement.Kind == ForEachKind.AwaitOf)
            {
                return EvaluateForAwaitOf(statement, environment, context, loopLabel);
            }

            var iterable = EvaluateExpression(statement.Iterable, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            // In JavaScript, `for...in` requires an object value; iterating
            // over `null` or `undefined` throws a TypeError. Treat other
            // non-object values as errors as well so engine bugs surface
            // as JavaScript throws rather than host exceptions.
            if (statement.Kind == ForEachKind.In &&
                iterable is not IJsObjectLike &&
                iterable is not JsObject &&
                iterable is not JsArray &&
                iterable is not string &&
                iterable is not null &&
                !ReferenceEquals(iterable, Symbol.Undefined))
            {
                throw new ThrowSignal("Cannot iterate properties of non-object value.");
            }

            var loopEnvironment =
                new JsEnvironment(environment, creatingSource: statement.Source, description: "for-each-loop");
            object? lastValue = Symbol.Undefined;

            if (statement.Kind == ForEachKind.Of &&
                TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
            {
                var plan = IteratorDriverFactory.CreatePlan(statement,
                    statement.Body is BlockStatement b
                        ? b
                        : new BlockStatement(statement.Source, [statement.Body], IsStrictBlock(statement.Body)));
                return ExecuteIteratorDriver(plan, iterator, null, loopEnvironment, environment, context, loopLabel);
            }

            var values = statement.Kind switch
            {
                ForEachKind.In => EnumeratePropertyKeys(iterable),
                ForEachKind.Of => EnumerateValues(iterable),
                _ => throw new ArgumentOutOfRangeException()
            };

            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                var iterationEnvironment = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(statement.Target, value, iterationEnvironment, environment, context,
                    statement.DeclarationKind);

                lastValue = EvaluateStatement(statement.Body, iterationEnvironment, context);

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            return ReferenceEquals(lastValue, EmptyCompletion) ? Symbol.Undefined : lastValue;
        }
    }

    extension(ForEachStatement statement)
    {
        private object? EvaluateForAwaitOf(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            var iterable = EvaluateExpression(statement.Iterable, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var loopEnvironment =
                new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");
            object? lastValue = Symbol.Undefined;

            if (TryGetIteratorFromProtocols(iterable, out var iterator))
            {
                var plan = IteratorDriverFactory.CreatePlan(statement,
                    statement.Body is BlockStatement b
                        ? b
                        : new BlockStatement(statement.Source, [statement.Body], IsStrictBlock(statement.Body)));
                return ExecuteIteratorDriver(plan, iterator!, null, loopEnvironment, environment, context, loopLabel);
            }

            var values = EnumerateValues(iterable);
            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                if (IsPromiseLike(value))
                {
                    throw new NotSupportedException(
                        "for await...of in this context must be lowered via the async CPS/iterator helpers; promise-valued iteration values are not supported in the direct evaluator.");
                }

                AssignLoopBinding(statement.Target, value, loopEnvironment, environment, context,
                    statement.DeclarationKind);
                lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }
            }

            return lastValue;
        }
    }
}
