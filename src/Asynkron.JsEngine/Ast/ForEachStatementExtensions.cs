using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ForEachStatement statement)
    {
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
                var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
                Func<JsEnvironment>? rentIterationEnvironment = null;
                if (plan.IterationSlotCount >= 0 && plan.IterationScopeId >= 0 &&
                    statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                        or VariableKind.AwaitUsing)
                {
                    rentIterationEnvironment = () =>
                    {
                        var env = new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                            description: "for-each-iteration");
                        env.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
                        return env;
                    };
                }

                // FAST PATH: Use IEnumerator<JsValue> for known types (JsArray, TypedArray, string)
                // This avoids allocating iterator result objects {done, value} per iteration.
                var fastEnumerator = TryGetFastEnumeratorForIteration(iterable, context);
                if (fastEnumerator is not null)
                {
                    try
                    {
                        return ExecuteIteratorDriverJsValue(
                            plan,
                            iterator: null,
                            enumerator: fastEnumerator,
                            loopEnvironment,
                            environment,
                            context,
                            loopLabel,
                            rentIterationEnvironment);
                    }
                    finally
                    {
                        fastEnumerator.Dispose();
                    }
                }

                // SLOW PATH: Full iterator protocol for custom iterables
                var iteratorTarget = NormalizeIterableTarget(iterable, context);
                if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
                {
                    return ExecuteIteratorDriverJsValue(
                        plan,
                        iterator,
                        enumerator: null,
                        loopEnvironment,
                        environment,
                        context,
                        loopLabel,
                        rentIterationEnvironment);
                }

                throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
            }

            var values = statement.Kind switch
            {
                ForEachKind.In => EnumeratePropertyKeys(iterable),
                _ => throw new ArgumentOutOfRangeException()
            };

            Func<JsEnvironment>? rentLoopIterationEnv = null;
            var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
            if (cachedPlan.IterationSlotCount >= 0 && cachedPlan.IterationScopeId >= 0 &&
                statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing)
            {
                rentLoopIterationEnv = () =>
                {
                    var env = new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                        description: "for-each-iteration");
                    env.InitializeSlots(cachedPlan.IterationSlotCount, cachedPlan.IterationScopeId);
                    return env;
                };
            }

            foreach (var value in values)
            {
                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                var iterationEnvironment = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                    or VariableKind.Using or VariableKind.AwaitUsing
                    ? rentLoopIterationEnv is not null
                        ? rentLoopIterationEnv()
                        : new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                            description: "for-each-iteration")
                : loopEnvironment;

            AssignLoopBinding(statement.Target, value, iterationEnvironment, environment, context,
                statement.DeclarationKind);

            SyncIterationSlots(cachedPlan, iterationEnvironment, context);

            // Per ES spec 14.7.5.7 ForIn/OfBodyEvaluation step 5.k-l:
            // Only update V (completion value) if result.[[Value]] is not empty
            var bodyResult = EvaluateStatementJsValue(statement.Body, iterationEnvironment, context);
            if (!bodyResult.IsUnit)
            {
                    lastValueJs = bodyResult;
                }

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

            var loopEnvironment =
                new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");

            var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
            Func<JsEnvironment>? rentIterationEnvironment = null;
            if (plan.IterationSlotCount >= 0 && plan.IterationScopeId >= 0 &&
                statement.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                    or VariableKind.AwaitUsing)
            {
                rentIterationEnvironment = () =>
                {
                    var env = new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                        description: "for-each-iteration");
                    env.InitializeSlots(plan.IterationSlotCount, plan.IterationScopeId);
                    return env;
                };
            }

            // FAST PATH: Use IEnumerator<JsValue> for sync iterables (arrays, typed arrays, strings)
            // This avoids iterator result object allocations while maintaining async semantics.
            var fastEnumerator = TryGetFastEnumeratorForIteration(iterable, context);
            if (fastEnumerator is not null)
            {
                try
                {
                    return ExecuteIteratorDriverJsValue(
                        plan,
                        iterator: null,
                        enumerator: fastEnumerator,
                        loopEnvironment,
                        environment,
                        context,
                        loopLabel,
                        rentIterationEnvironment);
                }
                finally
                {
                    fastEnumerator.Dispose();
                }
            }

            // SLOW PATH: Full iterator protocol for custom async/sync iterables
            var iteratorTarget = NormalizeIterableTarget(iterable, context);
            if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
            {
                return ExecuteIteratorDriverJsValue(
                    plan,
                    iterator,
                    enumerator: null,
                    loopEnvironment,
                    environment,
                    context,
                    loopLabel,
                    rentIterationEnvironment);
            }

            throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
        }
    }
}
