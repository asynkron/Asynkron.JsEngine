using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

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

            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                iterableEnvironment = new JsEnvironment(environment, creatingSource: statement.Source,
                    description: "for-each-head-tdz");
                var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing;
                statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
            }

            var iterable = EvaluateExpression(statement.Iterable, iterableEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (statement.Kind == ForEachKind.Of)
            {
                EnsureObjectCoercibleForIteration(iterable, context);
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

            if (statement.Kind == ForEachKind.Of)
            {
                var iteratorTarget = NormalizeIterableTarget(iterable, context);
                if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
                {
                    var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                    var completion = ExecuteIteratorDriver(plan, iterator, null, loopEnvironment, environment, context,
                        loopLabel);
                    return ReferenceEquals(completion, EmptyCompletion) ? Symbol.Undefined : completion;
                }

                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var values = statement.Kind switch
            {
                ForEachKind.In => EnumeratePropertyKeys(iterable),
                _ => throw new ArgumentOutOfRangeException()
            };

            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                var iterationEnvironment = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                    or VariableKind.Using or VariableKind.AwaitUsing
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

        private object? EvaluateForAwaitOf(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            var iterableEnvironment = environment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing)
            {
                iterableEnvironment = new JsEnvironment(environment, creatingSource: statement.Source,
                    description: "for-each-head-tdz");
                var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing;
                statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
            }

            var iterable = EvaluateExpression(statement.Iterable, iterableEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            EnsureObjectCoercibleForIteration(iterable, context);
            var iteratorTarget = NormalizeIterableTarget(iterable, context);

            var loopEnvironment =
                new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");
            object? lastValue = Symbol.Undefined;

            if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
            {
                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                var completion =
                    ExecuteIteratorDriver(plan, iterator!, null, loopEnvironment, environment, context, loopLabel);
                return ReferenceEquals(completion, EmptyCompletion) ? Symbol.Undefined : completion;
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
        }
    }
}
