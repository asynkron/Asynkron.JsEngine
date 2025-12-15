using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ForEachStatement statement)
    {
        private object? EvaluateForEach(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            return EvaluateForEachJsValue(statement, environment, context, loopLabel).ToObject();
        }

        private JsValue EvaluateForEachJsValue(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            if (statement.Kind == ForEachKind.AwaitOf)
            {
                return EvaluateForAwaitOfJsValue(statement, environment, context, loopLabel);
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

            var iterableJsValue = EvaluateExpression(statement.Iterable, iterableEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var iterable = iterableJsValue.ToObject();

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
            var lastValueJs = JsValue.Undefined;

            if (statement.Kind == ForEachKind.Of)
            {
                var iteratorTarget = NormalizeIterableTarget(iterable, context);
                if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
                {
                    var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                    var completion = ExecuteIteratorDriverJsValue(
                        plan,
                        iterator,
                        enumerator: null,
                        loopEnvironment,
                        environment,
                        context,
                        loopLabel);
                    return completion;
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

                lastValueJs = EvaluateStatementJsValue(statement.Body, iterationEnvironment, context);

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

            return lastValueJs;
        }

        private object? EvaluateForAwaitOf(JsEnvironment environment,
            EvaluationContext context, Symbol? loopLabel)
        {
            return EvaluateForAwaitOfJsValue(statement, environment, context, loopLabel).ToObject();
        }

        private JsValue EvaluateForAwaitOfJsValue(JsEnvironment environment,
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

            var iterableJs = EvaluateExpression(statement.Iterable, iterableEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var iterable = iterableJs.ToObject();
            EnsureObjectCoercibleForIteration(iterable, context);
            var iteratorTarget = NormalizeIterableTarget(iterable, context);

            var loopEnvironment =
                new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");

            if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
            {
                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                var completion =
                    ExecuteIteratorDriverJsValue(plan, iterator!, null, loopEnvironment, environment, context, loopLabel);
                return completion;
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
        }
    }
}
