using System.Collections;
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
using JsSymbols = Asynkron.JsEngine.Ast.Symbols;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Proof-of-concept evaluator that executes the new typed AST directly instead of walking cons cells.
///     The goal is to showcase the recommended shape: a dedicated evaluator with explicit pattern matching
///     rather than virtual methods on the node hierarchy. Only a focused subset of JavaScript semantics is
///     implemented for now so the skeleton stays approachable.
/// </summary>
public static class TypedAstEvaluator
{
    private const string GeneratorBrandPropertyName = "__generator_brand__";
    private static readonly Symbol YieldTrackerSymbol = Symbol.Intern("__yieldTracker__");
    private static readonly Symbol YieldResumeContextSymbol = Symbol.Intern("__yieldResume__");
    private static readonly Symbol GeneratorPendingCompletionSymbol = Symbol.Intern("__generatorPending__");
    private static readonly Symbol GeneratorInstanceSymbol = Symbol.Intern("__generatorInstance__");

    private static readonly string IteratorSymbolPropertyName =
        $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";

    private static readonly object GeneratorBrandMarker = new();

    public static object? EvaluateProgram(
        ProgramNode program,
        JsEnvironment environment,
        RealmState realmState,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script)
    {
        var context = new EvaluationContext(realmState, cancellationToken, executionKind)
        {
            SourceReference = program.Source
        };
        var executionEnvironment = program.IsStrict ? new JsEnvironment(environment, true, true) : environment;

        // Hoist var and function declarations in the program body so that
        // function declarations like `function formatArgs(...) {}` are
        // available before earlier statements that reference them.
        var programBlock = new BlockStatement(program.Source, program.Body, program.IsStrict);
        var blockedNames = CollectLexicalNames(programBlock);
        context.BlockedFunctionVarNames = blockedNames;
        HoistVarDeclarations(programBlock, executionEnvironment, context, lexicalNames: blockedNames);

        object? result = JsSymbols.Undefined;
        foreach (var statement in program.Body)
        {
            context.ThrowIfCancellationRequested();
            result = EvaluateStatement(statement, executionEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    private static object? EvaluateStatement(StatementNode statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? activeLabel = null)
    {
        context.SourceReference = statement.Source;
        context.ThrowIfCancellationRequested();

        return statement switch
        {
            BlockStatement block => EvaluateBlock(block, environment, context),
            ExpressionStatement expressionStatement => EvaluateExpression(expressionStatement.Expression, environment,
                context),
            ReturnStatement returnStatement => EvaluateReturn(returnStatement, environment, context),
            ThrowStatement throwStatement => EvaluateThrow(throwStatement, environment, context),
            VariableDeclaration declaration => EvaluateVariableDeclaration(declaration, environment, context),
            FunctionDeclaration functionDeclaration => EvaluateFunctionDeclaration(functionDeclaration, environment,
                context),
            IfStatement ifStatement => EvaluateIf(ifStatement, environment, context),
            WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context, activeLabel),
            DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context, activeLabel),
            ForStatement forStatement => EvaluateFor(forStatement, environment, context, activeLabel),
            ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context, activeLabel),
            BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
            ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
            LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
            TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
            SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context, activeLabel),
            ClassDeclaration classDeclaration => EvaluateClass(classDeclaration, environment, context),
            EmptyStatement => JsSymbols.Undefined,
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
        };
    }

    private static object? EvaluateBlock(BlockStatement block, JsEnvironment environment, EvaluationContext context)
    {
        var scope = new JsEnvironment(environment, false, block.IsStrict);
        object? result = JsSymbols.Undefined;

        foreach (var statement in block.Statements)
        {
            context.ThrowIfCancellationRequested();
            result = EvaluateStatement(statement, scope, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return result;
    }

    private static object? EvaluateReturn(ReturnStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        var value = statement.Expression is null
            ? null
            : EvaluateExpression(statement.Expression, environment, context);
        if (context.IsYield)
        {
            // Defer completing the return until the pending yield chain resumes and finishes.
            return value;
        }

        context.SetReturn(value);
        return value;
    }

    private static object? EvaluateThrow(ThrowStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var value = EvaluateExpression(statement.Expression, environment, context);
        context.SetThrow(value);
        return value;
    }

    private static object? EvaluateBreak(BreakStatement statement, EvaluationContext context)
    {
        context.SetBreak(statement.Label);
        return JsSymbols.Undefined;
    }

    private static object? EvaluateContinue(ContinueStatement statement, EvaluationContext context)
    {
        context.SetContinue(statement.Label);
        return JsSymbols.Undefined;
    }

    private static object? EvaluateIf(IfStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var test = EvaluateExpression(statement.Condition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var branch = IsTruthy(test) ? statement.Then : statement.Else;
        if (branch is null)
        {
            return JsSymbols.Undefined;
        }

        if (branch is BlockStatement block)
        {
            return EvaluateBlock(block, environment, context);
        }

        var branchScope = new JsEnvironment(environment, false, environment.IsStrict);
        return EvaluateStatement(branch, branchScope, context);
    }

    private static object? EvaluateWhile(WhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize while loop.");
        }

        return EvaluateLoopPlan(plan, environment, context, loopLabel);
    }

    private static object? EvaluateDoWhile(DoWhileStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize do/while loop.");
        }

        return EvaluateLoopPlan(plan, environment, context, loopLabel);
    }

    private static object? EvaluateFor(ForStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize for loop.");
        }

        var loopEnvironment = new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop");
        return EvaluateLoopPlan(plan, loopEnvironment, context, loopLabel);
    }

    private static object? EvaluateForEach(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            return EvaluateForAwaitOf(statement, environment, context, loopLabel);
        }

        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
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
            !ReferenceEquals(iterable, JsSymbols.Undefined))
        {
            throw new ThrowSignal("Cannot iterate properties of non-object value.");
        }

        var loopEnvironment =
            new JsEnvironment(environment, creatingSource: statement.Source, description: "for-each-loop");
        object? lastValue = JsSymbols.Undefined;

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

        return lastValue;
    }

    private static object? EvaluateForAwaitOf(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var loopEnvironment =
            new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");
        object? lastValue = JsSymbols.Undefined;

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

    private static bool TryGetIteratorFromProtocols(object? iterable, out JsObject? iterator)
    {
        iterator = null;
        if (iterable is not JsObject jsObject)
        {
            return false;
        }

        if (TryInvokeSymbolMethod(jsObject, "Symbol.asyncIterator", out var asyncIterator) &&
            asyncIterator is JsObject asyncObj)
        {
            iterator = asyncObj;
            return true;
        }

        if (TryInvokeSymbolMethod(jsObject, "Symbol.iterator", out var iteratorValue) &&
            iteratorValue is JsObject iteratorObj)
        {
            iterator = iteratorObj;
            return true;
        }

        return false;
    }

    private static bool TryInvokeSymbolMethod(JsObject target, string symbolName, out object? result)
    {
        var symbol = TypedAstSymbol.For(symbolName);
        var propertyName = $"@@symbol:{symbol.GetHashCode()}";
        if (target.TryGetProperty(propertyName, out var candidate) && candidate is IJsCallable callable)
        {
            result = callable.Invoke([], target);
            return true;
        }

        result = null;
        return false;
    }

    private static object? InvokeIteratorNext(JsObject iterator, object? sendValue = null, bool hasSendValue = false)
    {
        if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Iterator must expose a 'next' method.");
        }

        var args = hasSendValue ? new[] { sendValue } : Array.Empty<object?>();
        return callable.Invoke(args, iterator);
    }

    private static bool TryInvokeIteratorMethod(
        JsObject iterator,
        string methodName,
        object? argument,
        EvaluationContext context,
        out object? result)
    {
        result = null;
        if (!iterator.TryGetProperty(methodName, out var methodValue))
        {
            return false;
        }

        if (methodValue is null)
        {
            return false;
        }

        if (methodValue is IIsHtmlDda && methodValue is not IJsCallable)
        {
            return false;
        }

        if (methodValue is not IJsCallable callable)
        {
            throw StandardLibrary.ThrowTypeError($"Iterator method '{methodName}' is not callable.", context);
        }

        result = callable.Invoke([argument], iterator);
        return true;
    }

    private static object? ExecuteIteratorDriver(IteratorDriverPlan plan,
        JsObject iterator,
        IEnumerator<object?>? enumerator,
        JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = JsSymbols.Undefined;

        var state = new IteratorDriverState
        {
            IteratorObject = iterator,
            Enumerator = enumerator,
            IsAsyncIterator = plan.Kind == IteratorDriverKind.Await
        };

        while (!context.ShouldStopEvaluation)
        {
            context.ThrowIfCancellationRequested();

            object? nextResult = null;
            if (state.IteratorObject is not null)
            {
                nextResult = InvokeIteratorNext(state.IteratorObject);
            }
            else if (state.Enumerator is not null)
            {
                if (!state.Enumerator.MoveNext())
                {
                    break;
                }

                nextResult = state.Enumerator.Current;
            }

            if (nextResult is JsObject resultObj)
            {
                var done = resultObj.TryGetProperty("done", out var doneValue) &&
                           doneValue is bool and true;
                if (done)
                {
                    break;
                }

                var value = resultObj.TryGetProperty("value", out var yielded)
                    ? yielded
                    : JsSymbols.Undefined;

                var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(plan.Target, value, iterationEnvironment, outerEnvironment, context,
                    plan.DeclarationKind);
                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
            }
            else
            {
                // Enumerator path (non-object next)
                var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(plan.Target, nextResult, iterationEnvironment, outerEnvironment, context,
                    plan.DeclarationKind);
                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
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

        return lastValue;
    }

    private static bool IsPromiseLike(object? candidate)
    {
        return AwaitScheduler.IsPromiseLike(candidate);
    }

    // WAITING ON FULL ASYNC/AWAIT + ASYNC GENERATOR IR SUPPORT:
    // This helper synchronously blocks on promise resolution using TaskCompletionSource.
    // It keeps async/await and async iteration usable for now but must be replaced by
    // a non-blocking, event-loop-integrated continuation model once the async IR
    // pipeline is in place.
    private static bool TryAwaitPromise(object? candidate, EvaluationContext context, out object? resolvedValue)
    {
        return AwaitScheduler.TryAwaitPromiseSync(candidate, context, out resolvedValue);
    }

    private static void AssignLoopBinding(BindingTarget target, object? value, JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment, EvaluationContext context, VariableKind? declarationKind)
    {
        if (declarationKind is null)
        {
            AssignBindingTarget(target, value, outerEnvironment, context);
            return;
        }

        switch (declarationKind)
        {
            case VariableKind.Var:
                DefineOrAssignVar(target, value, loopEnvironment, context);
                break;
            case VariableKind.Let:
            case VariableKind.Const:
                DefineBindingTarget(target, value, loopEnvironment, context,
                    declarationKind == VariableKind.Const);
                CollectSymbolsFromBinding(target, context.BlockedFunctionVarNames);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static IEnumerable<object?> EnumeratePropertyKeys(object? value)
    {
        switch (value)
        {
            case JsArray array:
            {
                for (var i = 0; i < array.Items.Count; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case string s:
            {
                for (var i = 0; i < s.Length; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case IJsObjectLike accessor:
            {
                foreach (var key in accessor.GetOwnPropertyNames())
                {
                    var desc = accessor.GetOwnPropertyDescriptor(key);
                    if (desc is { Enumerable: false })
                    {
                        continue;
                    }

                    yield return key;
                }

                yield break;
            }
        }

        throw new InvalidOperationException("Cannot iterate properties of non-object value.");
    }

    private static IEnumerable<object?> EnumerateValues(object? value)
    {
        switch (value)
        {
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable<object?> enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
        }

        throw new InvalidOperationException("Value is not iterable.");
    }

    private static object? EvaluateTry(TryStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var result = EvaluateBlock(statement.TryBlock, environment, context);
        if (context.IsThrow && statement.Catch is not null)
        {
            var thrownValue = context.FlowValue;
            context.Clear();
            var catchEnv = new JsEnvironment(environment, creatingSource: statement.Catch.Body.Source,
                description: "catch");
            DefineBindingTarget(statement.Catch.Binding, thrownValue, catchEnv, context, false);
            result = EvaluateBlock(statement.Catch.Body, catchEnv, context);
        }

        if (statement.Finally is not null)
        {
            var savedSignal = context.CurrentSignal;

            GeneratorPendingCompletion? pending = null;
            var isGenerator = IsGeneratorContext(environment);
            if (isGenerator && savedSignal is not null)
            {
                pending = GetGeneratorPendingCompletion(environment);
                switch (savedSignal)
                {
                    case ThrowFlowSignal throwSignal:
                        pending.HasValue = true;
                        pending.IsThrow = true;
                        pending.IsReturn = false;
                        pending.Value = throwSignal.Value;
                        break;
                    case ReturnSignal returnSignal:
                        pending.HasValue = true;
                        pending.IsThrow = false;
                        pending.IsReturn = true;
                        pending.Value = returnSignal.Value;
                        break;
                }
            }

            context.Clear();
            _ = EvaluateBlock(statement.Finally, environment, context);
            if (context.CurrentSignal is null)
            {
                if (isGenerator && pending?.HasValue == true)
                {
                    if (pending.IsThrow)
                    {
                        context.SetThrow(pending.Value);
                    }
                    else if (pending.IsReturn)
                    {
                        context.SetReturn(pending.Value);
                    }

                    pending.HasValue = false;
                    pending.IsThrow = false;
                    pending.IsReturn = false;
                    pending.Value = null;
                }
                else
                {
                    RestoreSignal(context, savedSignal);
                }
            }
        }

        return result;
    }

    private static object? EvaluateSwitch(SwitchStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? targetLabel)
    {
        var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        object? lastValue = JsSymbols.Undefined;
        var hasMatched = false;

        foreach (var switchCase in statement.Cases)
        {
            if (!hasMatched)
            {
                if (switchCase.Test is null)
                {
                    hasMatched = true;
                }
                else
                {
                    var test = EvaluateExpression(switchCase.Test, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return lastValue;
                    }

                    hasMatched = StrictEquals(discriminant, test);
                }

                if (!hasMatched)
                {
                    continue;
                }
            }

            lastValue = EvaluateBlock(switchCase.Body, environment, context);
            if (context.TryClearBreak(targetLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }
        }

        return lastValue;
    }

    private static object? EvaluateClass(ClassDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        var constructorValue = CreateClassValue(declaration.Definition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return constructorValue;
        }

        environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
        return constructorValue;
    }

    private static object? EvaluateLoopPlan(LoopPlan plan, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = JsSymbols.Undefined;

        if (!plan.LeadingStatements.IsDefaultOrEmpty)
        {
            foreach (var statement in plan.LeadingStatements)
            {
                lastValue = EvaluateStatement(statement, environment, context, loopLabel);
                if (context.ShouldStopEvaluation)
                {
                    return lastValue;
                }
            }
        }

        while (true)
        {
            context.ThrowIfCancellationRequested();

            if (!plan.ConditionAfterBody)
            {
                if (!ExecuteCondition(plan, environment, context))
                {
                    break;
                }
            }

            lastValue = EvaluateStatement(plan.Body, environment, context, loopLabel);
            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                if (!ExecutePostIteration(plan, environment, context, ref lastValue))
                {
                    break;
                }

                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.ShouldStopEvaluation)
            {
                break;
            }

            if (!ExecutePostIteration(plan, environment, context, ref lastValue))
            {
                break;
            }

            if (plan.ConditionAfterBody)
            {
                if (!ExecuteCondition(plan, environment, context))
                {
                    break;
                }
            }
        }

        return lastValue;
    }

    private static bool ExecuteCondition(LoopPlan plan, JsEnvironment environment, EvaluationContext context)
    {
        if (!plan.ConditionPrologue.IsDefaultOrEmpty)
        {
            foreach (var statement in plan.ConditionPrologue)
            {
                _ = EvaluateStatement(statement, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }
            }
        }

        var test = EvaluateExpression(plan.Condition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        return IsTruthy(test);
    }

    private static bool ExecutePostIteration(LoopPlan plan, JsEnvironment environment, EvaluationContext context,
        ref object? lastValue)
    {
        if (plan.PostIteration.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var statement in plan.PostIteration)
        {
            lastValue = EvaluateStatement(statement, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStrictBlock(StatementNode statement)
    {
        return statement is BlockStatement { IsStrict: true };
    }

    private static object? EvaluateClassExpression(ClassExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return CreateClassValue(expression.Definition, environment, context);
    }

    private static object? CreateClassValue(ClassDefinition definition, JsEnvironment environment,
        EvaluationContext context)
    {
        var (superConstructor, superPrototype) = ResolveSuperclass(definition.Extends, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var constructorValue = EvaluateExpression(definition.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (constructorValue is not IJsEnvironmentAwareCallable constructor ||
            constructorValue is not IJsPropertyAccessor constructorAccessor)
        {
            throw new InvalidOperationException("Class constructor must be callable.");
        }

        var realm = context.RealmState;
        var prototype = EnsurePrototype(constructorAccessor, realm);
        if (superPrototype is not null)
        {
            prototype.SetPrototype(superPrototype);
        }

        if (constructorValue is TypedFunction typedFunction)
        {
            typedFunction.SetSuperBinding(superConstructor, superPrototype);
            var instanceFields = definition.Fields.Where(field => !field.IsStatic).ToImmutableArray();
            typedFunction.SetInstanceFields(instanceFields);
            typedFunction.SetIsClassConstructor(superConstructor is not null);
        }

        if (superConstructor is not null)
        {
            constructorAccessor.SetProperty("__proto__", superConstructor);
            if (constructorAccessor is IJsObjectLike ctorObject)
            {
                ctorObject.SetPrototype(superConstructor);
            }
        }
        else if (constructorAccessor is IJsObjectLike { Prototype: null } baseCtor &&
                 realm.FunctionPrototype is not null)
        {
            baseCtor.SetPrototype(realm.FunctionPrototype);
        }

        prototype.SetProperty("constructor", constructorValue);

        AssignClassMembers(definition.Members, constructorAccessor, prototype, superConstructor, superPrototype,
            environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var staticFields = definition.Fields.Where(field => field.IsStatic).ToImmutableArray();
        InitializeStaticFields(staticFields, constructorAccessor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return constructorValue;
    }

    private static (IJsEnvironmentAwareCallable? Constructor, JsObject? Prototype) ResolveSuperclass(
        ExpressionNode? extendsExpression, JsEnvironment environment, EvaluationContext context)
    {
        if (extendsExpression is null)
        {
            return (null, null);
        }

        var baseValue = EvaluateExpression(extendsExpression, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (baseValue is null)
        {
            return (null, null);
        }

        if (baseValue is not IJsEnvironmentAwareCallable callable ||
            baseValue is not IJsPropertyAccessor accessor)
        {
            throw new InvalidOperationException("Classes can only extend other constructors or null.");
        }

        if (!TryGetPropertyValue(baseValue, "prototype", out var prototypeValue) ||
            prototypeValue is not JsObject prototype)
        {
            prototype = new JsObject();
            accessor.SetProperty("prototype", prototype);
        }

        return (callable, prototype);
    }

    private static JsObject EnsurePrototype(IJsPropertyAccessor constructor, RealmState realm)
    {
        if (constructor.TryGetProperty("prototype", out var prototypeValue) && prototypeValue is JsObject prototype)
        {
            if (prototype.Prototype is null && realm.ObjectPrototype is not null)
            {
                prototype.SetPrototype(realm.ObjectPrototype);
            }

            return prototype;
        }

        var created = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            created.SetPrototype(realm.ObjectPrototype);
        }

        constructor.SetProperty("prototype", created);
        return created;
    }

    private static void AssignClassMembers(ImmutableArray<ClassMember> members, IJsPropertyAccessor constructorAccessor,
        JsObject prototype, IJsEnvironmentAwareCallable? superConstructor, JsObject? superPrototype,
        JsEnvironment environment, EvaluationContext context)
    {
        foreach (var member in members)
        {
            var value = EvaluateExpression(member.Function, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (value is not IJsCallable callable)
            {
                throw new InvalidOperationException("Class member must be callable.");
            }

            var homeObject = member.IsStatic
                ? constructorAccessor as IJsObjectLike
                : prototype;
            var superTarget = member.IsStatic
                ? superConstructor as IJsPropertyAccessor
                : superPrototype;
            if (value is TypedFunction typedFunction)
            {
                typedFunction.SetSuperBinding(superConstructor, superTarget);
                if (homeObject is not null)
                {
                    typedFunction.SetHomeObject(homeObject);
                }
            }

            if (member.Kind == ClassMemberKind.Method)
            {
                if (member.IsStatic)
                {
                    constructorAccessor.SetProperty(member.Name, value);
                }
                else
                {
                    prototype.SetProperty(member.Name, value);
                }

                continue;
            }

            var accessorTarget = member.IsStatic
                ? constructorAccessor as IJsObjectLike
                : prototype;
            if (accessorTarget is not null)
            {
                accessorTarget.DefineProperty(member.Name,
                    new PropertyDescriptor
                    {
                        Get = member.Kind == ClassMemberKind.Getter ? callable : null,
                        Set = member.Kind == ClassMemberKind.Setter ? callable : null,
                        Enumerable = false,
                        Configurable = true
                    });
            }
            else
            {
                constructorAccessor.SetProperty(member.Name, value);
            }
        }
    }

    private static void InitializeStaticFields(ImmutableArray<ClassField> fields,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment, EvaluationContext context)
    {
        foreach (var field in fields)
        {
            object? value = JsSymbols.Undefined;
            if (field.Initializer is not null)
            {
                value = EvaluateExpression(field.Initializer, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            constructorAccessor.SetProperty(field.Name, value);
        }
    }

    private static object? EvaluateVariableDeclaration(VariableDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        foreach (var declarator in declaration.Declarators)
        {
            EvaluateVariableDeclarator(declaration.Kind, declarator, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return JsSymbols.Undefined;
    }

    private static void EvaluateVariableDeclarator(VariableKind kind, VariableDeclarator declarator,
        JsEnvironment environment, EvaluationContext context)
    {
        var value = declarator.Initializer is null
            ? JsSymbols.Undefined
            : EvaluateExpression(declarator.Initializer, environment, context);

        if (context.ShouldStopEvaluation)
        {
            return;
        }

        var mode = kind switch
        {
            VariableKind.Var => BindingMode.DefineVar,
            VariableKind.Let => BindingMode.DefineLet,
            VariableKind.Const => BindingMode.DefineConst,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        ApplyBindingTarget(declarator.Target, value, environment, context, mode, declarator.Initializer is not null);
    }

    private static object? EvaluateFunctionDeclaration(FunctionDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        var function = CreateFunctionValue(declaration.Function, environment, context);
        environment.Define(declaration.Name, function);
        var skipVarBinding = !environment.IsStrict &&
                             context.BlockedFunctionVarNames is { } blocked &&
                             blocked.Contains(declaration.Name);
        if (!environment.IsStrict && !skipVarBinding)
        {
            var configurable = context.ExecutionKind == ExecutionKind.Eval;
            environment.DefineFunctionScoped(
                declaration.Name,
                function,
                true,
                true,
                configurable);
        }

        return function;
    }

    private static IJsCallable CreateFunctionValue(FunctionExpression functionExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (functionExpression.IsGenerator)
        {
            if (functionExpression.IsAsync)
            {
                return new AsyncGeneratorFactory(functionExpression, environment, context.RealmState);
            }

            return new TypedGeneratorFactory(functionExpression, environment, context.RealmState);
        }

        return new TypedFunction(functionExpression, environment, context.RealmState);
    }

    private static object? EvaluateLabeled(LabeledStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = EvaluateStatement(statement.Statement, environment, context, statement.Label);

            if (context.TryClearBreak(statement.Label))
            {
                return JsSymbols.Undefined;
            }

            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }

    private static object? EvaluateExpression(ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        context.SourceReference = expression.Source;

        return expression switch
        {
            LiteralExpression literal => EvaluateLiteral(literal, context),
            IdentifierExpression identifier => environment.Get(identifier.Name),
            BinaryExpression binary => EvaluateBinary(binary, environment, context),
            UnaryExpression unary => EvaluateUnary(unary, environment, context),
            ConditionalExpression conditional => EvaluateConditional(conditional, environment, context),
            CallExpression call => EvaluateCall(call, environment, context),
            FunctionExpression functionExpression => CreateFunctionValue(functionExpression, environment, context),
            AssignmentExpression assignment => EvaluateAssignment(assignment, environment, context),
            DestructuringAssignmentExpression destructuringAssignment =>
                EvaluateDestructuringAssignment(destructuringAssignment, environment, context),
            PropertyAssignmentExpression propertyAssignment =>
                EvaluatePropertyAssignment(propertyAssignment, environment, context),
            IndexAssignmentExpression indexAssignment =>
                EvaluateIndexAssignment(indexAssignment, environment, context),
            SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
            MemberExpression member => EvaluateMember(member, environment, context),
            NewExpression newExpression => EvaluateNew(newExpression, environment, context),
            ArrayExpression array => EvaluateArray(array, environment, context),
            ObjectExpression obj => EvaluateObject(obj, environment, context),
            ClassExpression classExpression => EvaluateClassExpression(classExpression, environment, context),
            TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
            TaggedTemplateExpression taggedTemplate =>
                EvaluateTaggedTemplate(taggedTemplate, environment, context),
            AwaitExpression awaitExpression => EvaluateAwait(awaitExpression, environment, context),
            YieldExpression yieldExpression => EvaluateYield(yieldExpression, environment, context),
            ThisExpression => environment.Get(JsSymbols.This),
            SuperExpression => throw new InvalidOperationException(
                $"Super is not available in this context.{GetSourceInfo(context, expression.Source)}"),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    private static object? EvaluateAssignment(AssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var targetValue = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return targetValue;
        }

        try
        {
            environment.Assign(expression.Target, targetValue);
            return targetValue;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            object? errorObject = ex.Message;

            // If a ReferenceError constructor is available, use it to
            // create a proper JS error instance so user code can catch
            // and inspect it.
            if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctor) &&
                ctor is IJsCallable callable)
            {
                errorObject = callable.Invoke([ex.Message], JsSymbols.Undefined);
            }

            context.SetThrow(errorObject);
            return errorObject;
        }
    }

    private static object? EvaluateAwait(AwaitExpression expression, JsEnvironment environment,
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

    private static object? EvaluateYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return expression.IsDelegated
            ? EvaluateDelegatedYield(expression, environment, context)
            : EvaluateSimpleYield(expression, environment, context);
    }

    private static object? EvaluateSimpleYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var yieldedValue = expression.Expression is null
            ? JsSymbols.Undefined
            : EvaluateExpression(expression.Expression, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return yieldedValue;
        }

        var yieldTracker = GetYieldTracker(environment);
        if (!yieldTracker.ShouldYield(out var yieldIndex))
        {
            var payload = GetResumePayload(environment, yieldIndex);
            if (!payload.HasValue)
            {
                return JsSymbols.Undefined;
            }

            if (payload.IsThrow)
            {
                context.SetThrow(payload.Value);
                return payload.Value;
            }

            if (payload.IsReturn)
            {
                context.SetReturn(payload.Value);
                return payload.Value;
            }

            return payload.Value;
        }

        context.SetYield(yieldedValue);
        return yieldedValue;
    }

    private static object? EvaluateDelegatedYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Expression is null)
        {
            throw new InvalidOperationException("yield* requires an expression.");
        }

        var stateKey = GetDelegatedStateKey(expression);
        var state = GetDelegatedState(stateKey, environment);

        if (state is null)
        {
            var iterable = EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return iterable;
            }

            state = CreateDelegatedState(iterable);
            StoreDelegatedState(stateKey, environment, state);
        }

        var tracker = GetYieldTracker(environment);
        object? pendingSend = null;
        var hasPendingSend = false;
        var pendingThrow = false;
        var pendingReturn = false;

        while (true)
        {
            var iteratorResult = state.MoveNext(pendingSend,
                hasPendingSend && !pendingThrow && !pendingReturn,
                pendingThrow,
                pendingReturn,
                context,
                out var awaitedPromise);

            if (awaitedPromise && context.IsThrow)
            {
                return JsSymbols.Undefined;
            }

            pendingSend = null;
            hasPendingSend = false;
            pendingThrow = false;
            pendingReturn = false;

            if (iteratorResult.IsDelegatedCompletion)
            {
                if (iteratorResult.PropagateThrow)
                {
                    context.SetThrow(iteratorResult.Value);
                    ClearDelegatedState(stateKey, environment);
                    return iteratorResult.Value;
                }

                ClearDelegatedState(stateKey, environment);
                return iteratorResult.Value;
            }

            var (value, done) = (iteratorResult.Value, iteratorResult.Done);
            if (done)
            {
                ClearDelegatedState(stateKey, environment);
                return value;
            }

            if (!tracker.ShouldYield(out var yieldIndex))
            {
                var payload = GetResumePayload(environment, yieldIndex);
                if (!payload.HasValue)
                {
                    continue;
                }

                if (payload.IsThrow)
                {
                    pendingSend = payload.Value;
                    hasPendingSend = true;
                    pendingThrow = true;
                    continue;
                }

                if (payload.IsReturn)
                {
                    pendingSend = payload.Value;
                    hasPendingSend = true;
                    pendingReturn = true;
                    continue;
                }

                pendingSend = payload.Value;
                hasPendingSend = true;
                continue;
            }

            context.SetYield(value);
            return value;
        }
    }

    private static YieldTracker GetYieldTracker(JsEnvironment environment)
    {
        if (!environment.TryGet(YieldTrackerSymbol, out var tracker) || tracker is not YieldTracker yieldTracker)
        {
            throw new InvalidOperationException("'yield' can only be used inside a generator function.");
        }

        return yieldTracker;
    }

    private static DelegatedYieldState CreateDelegatedState(object? iterable)
    {
        if (TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
        {
            return DelegatedYieldState.FromIterator(iterator);
        }

        var values = EnumerateValues(iterable);
        return DelegatedYieldState.FromEnumerable(values);
    }

    private static Symbol? GetDelegatedStateKey(YieldExpression expression)
    {
        if (expression.Source is null)
        {
            return null;
        }

        var key = $"__yield_delegate_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
        return Symbol.Intern(key);
    }

    private static Symbol? GetAwaitStateKey(AwaitExpression expression)
    {
        if (expression.Source is null)
        {
            return null;
        }

        var key = $"__await_state_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
        return Symbol.Intern(key);
    }

    private static DelegatedYieldState? GetDelegatedState(Symbol? key, JsEnvironment environment)
    {
        if (key is null)
        {
            return null;
        }

        if (environment.TryGet(key, out var existing) && existing is DelegatedYieldState state)
        {
            return state;
        }

        return null;
    }

    private static void StoreDelegatedState(Symbol? key, JsEnvironment environment, DelegatedYieldState state)
    {
        if (key is null)
        {
            return;
        }

        if (environment.TryGet(key, out _))
        {
            environment.Assign(key, state);
        }
        else
        {
            environment.Define(key, state);
        }
    }

    private static void ClearDelegatedState(Symbol? key, JsEnvironment environment)
    {
        if (key is null)
        {
            return;
        }

        if (environment.TryGet(key, out _))
        {
            environment.Assign(key, null);
        }
    }

    private static ResumePayload GetResumePayload(JsEnvironment environment, int yieldIndex)
    {
        if (!environment.TryGet(YieldResumeContextSymbol, out var contextValue) ||
            contextValue is not YieldResumeContext resumeContext)
        {
            return ResumePayload.Empty;
        }

        return resumeContext.TakePayload(yieldIndex);
    }

    private static bool IsGeneratorContext(JsEnvironment environment)
    {
        return environment.TryGet(YieldResumeContextSymbol, out var contextValue) &&
               contextValue is YieldResumeContext;
    }

    private static GeneratorPendingCompletion GetGeneratorPendingCompletion(JsEnvironment environment)
    {
        if (environment.TryGet(GeneratorPendingCompletionSymbol, out var existing) &&
            existing is GeneratorPendingCompletion pending)
        {
            return pending;
        }

        var created = new GeneratorPendingCompletion();
        environment.DefineFunctionScoped(GeneratorPendingCompletionSymbol, created, true);
        return created;
    }

    private static object? EvaluateDestructuringAssignment(DestructuringAssignmentExpression expression,
        JsEnvironment environment, EvaluationContext context)
    {
        var assignedValue = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return assignedValue;
        }

        // Reuse the same binding machinery as variable declarations so nested
        // destructuring assignments behave consistently.
        AssignBindingTarget(expression.Target, assignedValue, environment, context);
        return assignedValue;
    }

    private static object? EvaluatePropertyAssignment(PropertyAssignmentExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is MemberExpression { Target: SuperExpression } superMember)
        {
            if (!context.IsThisInitialized)
            {
                throw CreateSuperReferenceError(environment, context, null);
            }

            var propertyKey = EvaluateExpression(superMember.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            var assignedValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            var binding = ExpectSuperBinding(environment, context);
            binding.SetProperty(propertyName, assignedValue);
            return assignedValue;
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsComputed && IsNullish(target))
        {
            throw new InvalidOperationException("Cannot set property on null or undefined.");
        }

        var property = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, property, value, context);
        return value;
    }

    private static object? EvaluateIndexAssignment(IndexAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is SuperExpression)
        {
            throw new InvalidOperationException(
                $"Assigning through super is not supported.{GetSourceInfo(context, expression.Source)}");
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var index = EvaluateExpression(expression.Index, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        AssignPropertyValue(target, index, value, context);
        return value;
    }

    private static object? EvaluateSequence(SequenceExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        _ = EvaluateExpression(expression.Left, environment, context);
        return context.ShouldStopEvaluation
            ? JsSymbols.Undefined
            : EvaluateExpression(expression.Right, environment, context);
    }

    private static object? EvaluateLiteral(LiteralExpression literal, EvaluationContext context)
    {
        return literal.Value switch
        {
            RegexLiteralValue regex => StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags,
                context.RealmState),
            _ => literal.Value
        };
    }

    private static object? EvaluateMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Fast-path well-known symbol properties so expressions like
        // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
        // values that can be used as keys (e.g. o[Symbol.iterator]).
        if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
            string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
            expression.Property is LiteralExpression { Value: string symbolProp })
        {
            return symbolProp switch
            {
                "iterator" => TypedAstSymbol.For("Symbol.iterator"),
                "asyncIterator" => TypedAstSymbol.For("Symbol.asyncIterator"),
                "toStringTag" => TypedAstSymbol.For("Symbol.toStringTag"),
                _ => EvaluateDefaultMember(expression, environment, context)
            };
        }

        return EvaluateDefaultMember(expression, environment, context);
    }

    private static object? EvaluateDefaultMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is SuperExpression)
        {
            var (memberValue, _) = ResolveSuperMember(expression, environment, context);
            return context.ShouldStopEvaluation ? JsSymbols.Undefined : memberValue;
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsOptional && IsNullish(target))
        {
            return JsSymbols.Undefined;
        }

        var propertyValue = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (TryGetPropertyValue(target, propertyValue, out var arrayValue, context))
        {
            return context.ShouldStopEvaluation ? JsSymbols.Undefined : arrayValue;
        }

        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return TryGetPropertyValue(target, propertyName, out var value, context)
            ? context.ShouldStopEvaluation ? JsSymbols.Undefined : value
            : JsSymbols.Undefined;
    }

    private static object? EvaluateConditional(ConditionalExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateExpression(expression.Test, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return IsTruthy(test)
            ? EvaluateExpression(expression.Consequent, environment, context)
            : EvaluateExpression(expression.Alternate, environment, context);
    }

    private static object? EvaluateUnary(UnaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Operator is "++" or "--")
        {
            var reference = AssignmentReferenceResolver.Resolve(
                expression.Operand,
                environment,
                context,
                EvaluateExpression);
            var currentValue = reference.GetValue();
            var updatedValue = expression.Operator == "++"
                ? IncrementValue(currentValue, context)
                : DecrementValue(currentValue, context);
            reference.SetValue(updatedValue);
            return expression.IsPrefix ? updatedValue : currentValue;
        }

        if (expression.Operator == "delete")
        {
            return EvaluateDelete(expression.Operand, environment, context);
        }

        if (expression.Operator == "typeof")
        {
            if (expression.Operand is IdentifierExpression identifier &&
                !environment.TryGet(identifier.Name, out var value))
            {
                return "undefined";
            }

            var operandValue = EvaluateExpression(expression.Operand, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            return GetTypeofString(operandValue);
        }

        var operand = EvaluateExpression(expression.Operand, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return expression.Operator switch
        {
            "!" => !IsTruthy(operand),
            "+" => operand is JsBigInt
                ? throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context)
                : JsOps.ToNumber(operand, context),
            "-" => operand is JsBigInt bigInt ? -bigInt : -JsOps.ToNumber(operand, context),
            "~" => BitwiseNot(operand, context),
            "void" => JsSymbols.Undefined,
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateBinary(BinaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var left = EvaluateExpression(expression.Left, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        switch (expression.Operator)
        {
            case "&&":
                return IsTruthy(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
            case "||":
                return IsTruthy(left)
                    ? left
                    : EvaluateExpression(expression.Right, environment, context);
            case "??":
                return IsNullish(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
        }

        var right = EvaluateExpression(expression.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        return expression.Operator switch
        {
            "+" => Add(left, right, context),
            "-" => Subtract(left, right, context),
            "*" => Multiply(left, right, context),
            "/" => Divide(left, right, context),
            "%" => Modulo(left, right, context),
            "**" => Power(left, right, context),
            "==" => LooseEquals(left, right, context),
            "!=" => !LooseEquals(left, right, context),
            "===" => StrictEquals(left, right),
            "!==" => !StrictEquals(left, right),
            "<" => JsOps.LessThan(left, right, context),
            "<=" => JsOps.LessThanOrEqual(left, right, context),
            ">" => JsOps.GreaterThan(left, right, context),
            ">=" => JsOps.GreaterThanOrEqual(left, right, context),
            "&" => BitwiseAnd(left, right, context),
            "|" => BitwiseOr(left, right, context),
            "^" => BitwiseXor(left, right, context),
            "<<" => LeftShift(left, right, context),
            ">>" => RightShift(left, right, context),
            ">>>" => UnsignedRightShift(left, right, context),
            "in" => InOperator(left, right, context),
            "instanceof" => InstanceofOperator(left, right),
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateCall(CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var (callee, thisValue, skippedOptional) = EvaluateCallTarget(expression.Callee, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsSymbols.Undefined;
        }

        if (expression.IsOptional && IsNullish(callee))
        {
            return JsSymbols.Undefined;
        }

        if (callee is not IJsCallable callable)
        {
            // Special-case Function.prototype.apply / call patterns such as
            // Object.prototype.hasOwnProperty.apply(target, args).
            if (expression.Callee is MemberExpression member)
            {
                if (thisValue is IJsCallable targetFunction &&
                    member.Property is LiteralExpression { Value: string propertyName })
                {
                    if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                    {
                        return InvokeWithApply(targetFunction, expression.Arguments, environment, context);
                    }

                    if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                    {
                        return InvokeWithCall(targetFunction, expression.Arguments, environment, context);
                    }
                }

                // Fallback for patterns like `obj.formatArgs.call(this, ...)`
                // where `formatArgs` is a callable copied onto `obj` but the
                // `.call` helper is missing or not modeled. In that case we
                // invoke the underlying function directly with the provided
                // `this` value and arguments instead of throwing.
                if (member is
                    {
                        Property: LiteralExpression { Value: "call" }, Target: MemberExpression
                        {
                            Property: LiteralExpression { Value: "formatArgs" }
                        } inner
                    })
                {
                    var target = EvaluateExpression(inner.Target, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    if (TryGetPropertyValue(target, "formatArgs", out var innerValue) &&
                        innerValue is IJsCallable innerFunction)
                    {
                        return InvokeWithCall(innerFunction, expression.Arguments, environment, context);
                    }
                }
            }

            var typeName = callee?.GetType().Name ?? "null";
            var sourceInfo = GetSourceInfo(context, expression.Source);
            var symbolName = callee is Symbol sym ? sym.Name : null;
            var symbolSuffix = symbolName is null ? string.Empty : $" (symbol '{symbolName}')";
            var calleeDescription = DescribeCallee(expression.Callee);
            Console.Error.WriteLine(
                $"[EvaluateCall] Non-callable callee={calleeDescription}, type={typeName}, thisValueType={thisValue?.GetType().Name ?? "null"}{symbolSuffix}{sourceInfo}");
            throw new InvalidOperationException(
                $"Attempted to call a non-callable value '{calleeDescription}' of type '{typeName}'{symbolSuffix}.{sourceInfo}");
        }

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsSymbols.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue))
                {
                    arguments.Add(item);
                }

                continue;
            }

            arguments.Add(EvaluateExpression(argument.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var frozenArguments = FreezeArguments(arguments);

        try
        {
            var result = callable.Invoke(frozenArguments, thisValue);
            if (expression.Callee is SuperExpression)
            {
                context.MarkThisInitialized();
            }

            return result;
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }
        }
    }

    private static ImmutableArray<object?> FreezeArguments(ImmutableArray<object?>.Builder builder)
    {
        return builder.Count == builder.Capacity
            ? builder.MoveToImmutable()
            : builder.ToImmutable();
    }

    private static object? InvokeWithApply(IJsCallable targetFunction,
        ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        object? thisArg = JsSymbols.Undefined;
        if (callArguments.Length > 0)
        {
            thisArg = EvaluateExpression(callArguments[0].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        var argsBuilder = ImmutableArray.CreateBuilder<object?>();
        if (callArguments.Length > 1)
        {
            var argsArray = EvaluateExpression(callArguments[1].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            foreach (var item in EnumerateSpread(argsArray))
            {
                argsBuilder.Add(item);
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static object? InvokeWithCall(IJsCallable targetFunction,
        ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        object? thisArg = JsSymbols.Undefined;
        var argsBuilder = ImmutableArray.CreateBuilder<object?>();

        for (var i = 0; i < callArguments.Length; i++)
        {
            var argValue = EvaluateExpression(callArguments[i].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            if (i == 0)
            {
                thisArg = argValue;
            }
            else
            {
                argsBuilder.Add(argValue);
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static (object? Callee, object? ThisValue, bool SkippedOptional) EvaluateCallTarget(ExpressionNode callee,
        JsEnvironment environment, EvaluationContext context)
    {
        if (callee is SuperExpression superExpression)
        {
            var binding = ExpectSuperBinding(environment, context);
            if (binding.Constructor is null)
            {
                throw new InvalidOperationException(
                    $"Super constructor is not available in this context.{GetSourceInfo(context, superExpression.Source)}");
            }

            return (binding.Constructor, binding.ThisValue, false);
        }

        if (callee is MemberExpression member)
        {
            if (member.Target is SuperExpression)
            {
                var (memberValue, binding) = ResolveSuperMember(member, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (JsSymbols.Undefined, binding.ThisValue, true);
                }

                return (memberValue, binding.ThisValue, false);
            }

            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            if (member.IsOptional && IsNullish(target))
            {
                return (null, null, true);
            }

            var property = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            var propertyName = JsOps.GetRequiredPropertyName(property, context);
            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            if (!TryGetPropertyValue(target, propertyName, out var value, context))
            {
                return context.ShouldStopEvaluation
                    ? (JsSymbols.Undefined, null, true)
                    : (JsSymbols.Undefined, target, false);
            }

            if (context.ShouldStopEvaluation)
            {
                return (JsSymbols.Undefined, null, true);
            }

            return (value, target, false);
        }

        var directCallee = EvaluateExpression(callee, environment, context);
        return (directCallee, null, false);
    }

    private static IEnumerable<object?> EnumerateSpread(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
            default:
                throw new InvalidOperationException("Value is not iterable.");
        }
    }

    private static bool EvaluateDelete(ExpressionNode operand, JsEnvironment environment, EvaluationContext context)
    {
        if (operand is MemberExpression member)
        {
            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            var propertyValue = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return DeletePropertyValue(target, propertyValue, context);
        }

        // Deleting identifiers is a no-op in strict mode; return false to indicate failure.
        return false;
    }

    private static object? EvaluateNew(NewExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var realm = context.RealmState;
        var constructor = EvaluateExpression(expression.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (constructor is not IJsCallable callable)
        {
            throw new InvalidOperationException("Attempted to construct a non-callable value.");
        }

        if (constructor is HostFunction hostFunction &&
            (!hostFunction.IsConstructor || hostFunction.DisallowConstruct))
        {
            var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke([hostFunction.ConstructErrorMessage ?? "is not a constructor"], null)
                : new InvalidOperationException(hostFunction.ConstructErrorMessage ?? "Target is not a constructor.");
            throw new ThrowSignal(error);
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject proto)
        {
            instance.SetPrototype(proto);
        }

        InitializeClassInstance(constructor, instance, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        var args = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            args.Add(EvaluateExpression(argument, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        object? result;
        try
        {
            result = callable.Invoke(args.MoveToImmutable(), instance);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }

        // In JavaScript, constructors can explicitly return an object to override the
        // default instance that `new` creates. Our host objects (Map, Set, custom
        // host functions, etc.) don't necessarily derive from JsObject, but they do
        // expose their members through IJsPropertyAccessor/IJsCallable. Treat any
        // such object-like result as the constructed value; otherwise fall back to
        // the auto-created instance.
        return result switch
        {
            IJsPropertyAccessor => result,
            IJsCallable => result,
            _ => instance
        };
    }

    private static void InitializeClassInstance(object? constructor, JsObject instance, JsEnvironment environment,
        EvaluationContext context)
    {
        if (constructor is TypedFunction typedFunction)
        {
            typedFunction.InitializeInstance(instance, environment, context);
        }
    }

    private static object? EvaluateArray(ArrayExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray();
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValue = EvaluateExpression(element.Expression!, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsSymbols.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue))
                {
                    array.Push(item);
                }

                continue;
            }

            array.Push(element.Expression is null
                ? JsSymbols.Undefined
                : EvaluateExpression(element.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        StandardLibrary.AddArrayMethods(array, context.RealmState);
        return array;
    }

    private static string DescribeCallee(ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression id => id.Name.Name,
            MemberExpression member => $"{DescribeCallee(member.Target)}.{DescribeMemberName(member.Property)}",
            CallExpression call => $"{DescribeCallee(call.Callee)}(...)",
            _ => expression.GetType().Name
        };
    }

    private static string DescribeMemberName(ExpressionNode property)
    {
        return property switch
        {
            LiteralExpression { Value: string s } => s,
            IdentifierExpression id => id.Name.Name,
            _ => property.GetType().Name
        };
    }

    private static object? EvaluateObject(ObjectExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        if (context.RealmState.ObjectPrototype is { } objectProto)
        {
            obj.SetPrototype(objectProto);
        }

        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property:
                {
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    var value = member.Value is null
                        ? JsSymbols.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Method:
                {
                    var method = new TypedFunction(member.Function!, environment, context.RealmState);
                    method.SetHomeObject(obj);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetProperty(name, method);
                    break;
                }
                case ObjectMemberKind.Getter:
                {
                    var getter = new TypedFunction(member.Function!, environment, context.RealmState);
                    getter.SetHomeObject(obj);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetGetter(name, getter);
                    break;
                }
                case ObjectMemberKind.Setter:
                {
                    var setter = new TypedFunction(member.Function!, environment, context.RealmState);
                    setter.SetHomeObject(obj);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    obj.SetSetter(name, setter);
                    break;
                }
                case ObjectMemberKind.Field:
                {
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    var value = member.Value is null
                        ? JsSymbols.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Spread:
                {
                    var spreadValue = EvaluateExpression(member.Value!, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return JsSymbols.Undefined;
                    }

                    if (spreadValue is JsObject spreadObject)
                    {
                        foreach (var key in spreadObject.GetOwnPropertyNames())
                        {
                            var spreadPropertyValue = spreadObject.TryGetProperty(key, out var val)
                                ? val
                                : JsSymbols.Undefined;
                            obj.SetProperty(key, spreadPropertyValue);
                        }

                        break;
                    }

                    if (spreadValue is IDictionary<string, object?> dictionary)
                    {
                        foreach (var kvp in dictionary)
                        {
                            obj.SetProperty(kvp.Key, kvp.Value);
                        }

                        break;
                    }

                    throw new InvalidOperationException("Cannot spread value that is not an object.");
                }
            }
        }

        return obj;
    }

    private static string ResolveObjectMemberName(ObjectMember member, JsEnvironment environment,
        EvaluationContext context)
    {
        object? keyValue;

        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            keyValue = EvaluateExpression(keyExpression, environment, context);
        }
        else
        {
            keyValue = member.Key;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
        return context.ShouldStopEvaluation ? string.Empty : propertyName;
    }

    private static object? EvaluateTemplateLiteral(TemplateLiteralExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new StringBuilder();
        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Append(part.Text);
                continue;
            }

            if (part.Expression is null)
            {
                continue;
            }

            var value = EvaluateExpression(part.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            builder.Append(value.ToJsString());
        }

        return builder.ToString();
    }

    private static object? EvaluateTaggedTemplate(TaggedTemplateExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var (tagValue, thisValue, skippedOptional) = EvaluateCallTarget(expression.Tag, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsSymbols.Undefined;
        }

        if (tagValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Tag in tagged template must be a function.");
        }

        var stringsArrayValue = EvaluateExpression(expression.StringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (stringsArrayValue is not JsArray stringsArray)
        {
            throw new InvalidOperationException("Tagged template strings array is invalid.");
        }

        var rawStringsArrayValue = EvaluateExpression(expression.RawStringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsSymbols.Undefined;
        }

        if (rawStringsArrayValue is not JsArray rawStringsArray)
        {
            throw new InvalidOperationException("Tagged template raw strings array is invalid.");
        }

        var templateObject = CreateTemplateObject(stringsArray, rawStringsArray);

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Expressions.Length + 1);
        arguments.Add(templateObject);

        foreach (var expr in expression.Expressions)
        {
            arguments.Add(EvaluateExpression(expr, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var frozenArguments = FreezeArguments(arguments);

        try
        {
            return callable.Invoke(frozenArguments, thisValue);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }
        }
    }

    private static JsObject CreateTemplateObject(JsArray stringsArray, JsArray rawStringsArray)
    {
        var templateObject = new JsObject();
        for (var i = 0; i < stringsArray.Items.Count; i++)
        {
            templateObject[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
        }

        templateObject["length"] = (double)stringsArray.Items.Count;
        templateObject["raw"] = rawStringsArray;
        return templateObject;
    }

    private static bool IsNullish(object? value)
    {
        return value.IsNullish();
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static object? Add(object? left, object? right, EvaluationContext context)
    {
        var leftPrimitive = JsOps.ToPrimitive(left, "default", context);
        var rightPrimitive = JsOps.ToPrimitive(right, "default", context);

        if (leftPrimitive is JsBigInt leftBigInt && rightPrimitive is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        if (leftPrimitive is string || rightPrimitive is string)
        {
            return JsOps.ToJsString(leftPrimitive) + JsOps.ToJsString(rightPrimitive);
        }

        if (leftPrimitive is JsBigInt || rightPrimitive is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return JsOps.ToNumber(leftPrimitive, context) + JsOps.ToNumber(rightPrimitive, context);
    }

    private static object Subtract(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l - r,
            (l, r) => l - r,
            context);
    }

    private static object Multiply(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l * r,
            (l, r) => l * r,
            context);
    }

    private static object Divide(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l / r,
            (l, r) => l / r,
            context);
    }

    private static object Modulo(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l % r,
            (l, r) => l % r,
            context);
    }

    private static object Power(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => JsBigInt.Pow(l, r),
            (l, r) => Math.Pow(l, r),
            context);
    }

    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp,
        EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return numericOp(JsOps.ToNumber(left, context), JsOps.ToNumber(right, context));
    }

    private static bool LooseEquals(object? left, object? right, EvaluationContext context)
    {
        return JsOps.LooseEquals(left, right, context);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static object BitwiseAnd(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l & r,
            (l, r) => l & r,
            context);
    }

    private static object BitwiseOr(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l | r,
            (l, r) => l | r,
            context);
    }

    private static object BitwiseXor(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r,
            context);
    }

    private static object BitwiseNot(object? operand, EvaluationContext context)
    {
        if (operand is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        return (double)~ToInt32(operand, context);
    }

    private static object LeftShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("BigInts have no unsigned right shift, use >> instead", context);
        }

        var leftUInt = ToUInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftUInt >> rightInt);
    }

    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op,
        EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context);
        return (double)int32Op(leftInt, rightInt);
    }

    private static int ToInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToInt32(JsOps.ToNumber(value, context));
    }

    private static uint ToUInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToUInt32(JsOps.ToNumber(value, context));
    }

    private static object IncrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value + BigInteger.One),
            _ => JsOps.ToNumber(value, context) + 1
        };
    }

    private static object DecrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value - BigInteger.One),
            _ => JsOps.ToNumber(value, context) - 1
        };
    }

    private static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        return JsOps.ToPropertyName(value, context);
    }

    private static bool TryResolveArrayIndex(object? candidate, out int index, EvaluationContext? context = null)
    {
        return JsOps.TryResolveArrayIndex(candidate, out index, context);
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        return JsOps.TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context = null)
    {
        return JsOps.TryGetPropertyValue(target, propertyKey, out value, context);
    }

    private static void AssignPropertyValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context = null)
    {
        JsOps.AssignPropertyValue(target, propertyKey, value, context);
    }

    private static void AssignPropertyValueByName(object? target, string propertyName, object? value)
    {
        JsOps.AssignPropertyValueByName(target, propertyName, value);
    }

    private static bool DeletePropertyValue(object? target, object? propertyKey, EvaluationContext? context = null)
    {
        return JsOps.DeletePropertyValue(target, propertyKey, context);
    }

    private static void HoistVarDeclarations(
        BlockStatement block,
        JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues = true,
        HashSet<Symbol>? lexicalNames = null)
    {
        var effectiveLexicalNames = lexicalNames is null
            ? CollectLexicalNames(block)
            : new HashSet<Symbol>(lexicalNames);
        if (lexicalNames is not null)
        {
            effectiveLexicalNames.UnionWith(CollectLexicalNames(block));
        }

        foreach (var statement in block.Statements)
        {
            HoistFromStatement(statement, environment, context, hoistFunctionValues, effectiveLexicalNames);
        }
    }

    private static void HoistFromStatement(
        StatementNode statement,
        JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues,
        HashSet<Symbol>? lexicalNames)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDeclaration:
                    foreach (var declarator in varDeclaration.Declarators)
                    {
                        HoistFromBindingTarget(declarator.Target, environment);
                    }

                    break;
                case BlockStatement block:
                    HoistVarDeclarations(block, environment, context, false, lexicalNames);
                    break;
                case IfStatement ifStatement:
                    HoistFromStatement(ifStatement.Then, environment, context, false,
                        lexicalNames);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        hoistFunctionValues = false;
                        continue;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    hoistFunctionValues = false;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    hoistFunctionValues = false;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar)
                    {
                        HoistFromStatement(initVar, environment, context, hoistFunctionValues, lexicalNames);
                    }

                    statement = forStatement.Body;
                    hoistFunctionValues = false;
                    continue;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        HoistFromBindingTarget(forEachStatement.Target, environment);
                    }

                    statement = forEachStatement.Body;
                    hoistFunctionValues = false;
                    continue;
                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    continue;
                case TryStatement tryStatement:
                    HoistVarDeclarations(tryStatement.TryBlock, environment, context, false,
                        lexicalNames);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        HoistVarDeclarations(catchClause.Body, environment, context, false,
                            lexicalNames);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        HoistVarDeclarations(finallyBlock, environment, context, false,
                            lexicalNames);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        HoistVarDeclarations(switchCase.Body, environment, context, false,
                            lexicalNames);
                    }

                    break;
                case FunctionDeclaration functionDeclaration:
                {
                    if (lexicalNames is not null && lexicalNames.Contains(functionDeclaration.Name))
                    {
                        break;
                    }

                    if (hoistFunctionValues)
                    {
                        var functionValue = CreateFunctionValue(functionDeclaration.Function, environment, context);
                        environment.DefineFunctionScoped(
                            functionDeclaration.Name,
                            functionValue,
                            hasInitializer: true,
                            isFunctionDeclaration: true,
                            globalFunctionConfigurable: context.ExecutionKind == ExecutionKind.Eval);
                        break;
                    }

                    // Annex B: in non-strict code, function declarations inside blocks are
                    // treated as var-scoped for the enclosing function/global scope. In
                    // strict mode the block-scoped function should not create a var
                    // binding.
                    if (!environment.IsStrict)
                    {
                        environment.DefineFunctionScoped(
                            functionDeclaration.Name,
                            JsSymbols.Undefined,
                            hasInitializer: false,
                            isFunctionDeclaration: true,
                            globalFunctionConfigurable: context.ExecutionKind == ExecutionKind.Eval);
                    }
                    break;
                }
                case ClassDeclaration:
                case ModuleStatement:
                    break;
            }

            break;
        }
    }

    private static HashSet<Symbol> CollectLexicalNames(BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        CollectLexicalNamesFromStatement(block, names);
        return names;
    }

    private static void CollectLexicalNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectLexicalNamesFromStatement(inner, names);
                    }

                    break;
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } letDecl:
                    foreach (var declarator in letDecl.Declarators)
                    {
                        CollectSymbolsFromBinding(declarator.Target, names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
                case FunctionDeclaration:
                    // Function declarations are handled separately; they should not block themselves.
                    break;
                case IfStatement ifStatement:
                    CollectLexicalNamesFromStatement(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const
                        } decl)
                    {
                        foreach (var declarator in decl.Declarators)
                        {
                            CollectSymbolsFromBinding(declarator.Target, names);
                        }
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const)
                    {
                        CollectSymbolsFromBinding(forEachStatement.Target, names);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectLexicalNamesFromStatement(switchCase.Body, names);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectLexicalNamesFromStatement(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        CollectSymbolsFromBinding(catchClause.Binding, names);
                        CollectLexicalNamesFromStatement(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectSymbolsFromBinding(BindingTarget target, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    names.Add(id.Name);
                    return;
                case ArrayBinding array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectSymbolsFromBinding(element.Target, names);
                        }
                    }

                    if (array.RestElement is not null)
                    {
                        target = array.RestElement;
                        continue;
                    }

                    return;
                case ObjectBinding obj:
                    foreach (var property in obj.Properties)
                    {
                        CollectSymbolsFromBinding(property.Target, names);
                    }

                    if (obj.RestElement is not null)
                    {
                        target = obj.RestElement;
                        continue;
                    }

                    return;
                default:
                    return;
            }
        }
    }

    private static void HoistFromBindingTarget(BindingTarget target, JsEnvironment environment)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    environment.DefineFunctionScoped(identifier.Name, JsSymbols.Undefined, false);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            HoistFromBindingTarget(element.Target, environment);
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        HoistFromBindingTarget(property.Target, environment);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static bool InOperator(object? property, object? target, EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(property, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        return TryGetPropertyValue(target, propertyName, out _, context);
    }

    private static bool InstanceofOperator(object? left, object? right)
    {
        if (!TryGetPropertyValue(right, "prototype", out var prototype) || prototype is not JsObject prototypeObject)
        {
            throw new InvalidOperationException("Right-hand side of 'instanceof' is not a constructor.");
        }

        var current = left switch
        {
            JsObject obj => obj.Prototype,
            IJsObjectLike objectLike => objectLike.Prototype,
            _ => null
        };

        while (current is not null)
        {
            if (ReferenceEquals(current, prototypeObject))
            {
                return true;
            }

            current = current.Prototype;
        }

        // Fallback for branded error objects where the prototype chain was
        // constructed correctly but does not match by reference (e.g. across
        // realms or when a test mutates globals). If the thrown object carries
        // a matching name tag, treat it as an instance of the constructor.
        if (TryGetPropertyValue(right, "name", out var ctorName) &&
            ctorName is string nameString &&
            TryGetPropertyValue(left, "name", out var valueName) &&
            valueName is string valueNameString &&
            string.Equals(nameString, valueNameString, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string GetTypeofString(object? value)
    {
        return JsOps.GetTypeofString(value);
    }

    private static void AssignBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.Assign);
    }

    private static void DefineBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context, bool isConst)
    {
        ApplyBindingTarget(target, value, environment, context,
            isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
    }

    private static void DefineOrAssignVar(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.DefineVar);
    }

    private static void ApplyBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode, bool hasInitializer = true)
    {
        switch (target)
        {
            case IdentifierBinding identifier:
                ApplyIdentifierBinding(identifier, value, environment, mode, hasInitializer);
                break;
            case ArrayBinding arrayBinding:
                BindArrayPattern(arrayBinding, value, environment, context, mode);
                break;
            case ObjectBinding objectBinding:
                BindObjectPattern(objectBinding, value, environment, context, mode);
                break;
            default:
                throw new NotSupportedException($"Binding target '{target.GetType().Name}' is not supported.");
        }
    }

    private static void ApplyIdentifierBinding(IdentifierBinding identifier, object? value, JsEnvironment environment,
        BindingMode mode, bool hasInitializer)
    {
        switch (mode)
        {
            case BindingMode.Assign:
                environment.Assign(identifier.Name, value);
                break;
            case BindingMode.DefineLet:
                environment.Define(identifier.Name, value, isLexical: true, blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineConst:
                environment.Define(identifier.Name, value, true, blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineVar:
                environment.DefineFunctionScoped(identifier.Name, value, hasInitializer);
                break;
            case BindingMode.DefineParameter:
                environment.Define(identifier.Name, value, isLexical: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void BindArrayPattern(ArrayBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        if (value is not JsArray array)
        {
            throw new InvalidOperationException(
                $"Cannot destructure non-array value.{GetSourceInfo(context)}");
        }

        var index = 0;
        foreach (var element in binding.Elements)
        {
            if (element.Target is null)
            {
                index++;
                continue;
            }

            var elementValue = index < array.Items.Count ? array.Items[index] : null;
            if (IsNullOrUndefined(elementValue) && element.DefaultValue is not null)
            {
                elementValue = EvaluateExpression(element.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            ApplyBindingTarget(element.Target, elementValue, environment, context, mode);
            index++;
        }

        if (binding.RestElement is not null)
        {
            var restArray = new JsArray();
            for (; index < array.Items.Count; index++)
            {
                restArray.Push(array.Items[index]);
            }

            ApplyBindingTarget(binding.RestElement, restArray, environment, context, mode);
        }
    }

    private static void BindObjectPattern(ObjectBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        if (value is not JsObject obj)
        {
            throw new InvalidOperationException(
                $"Cannot destructure non-object value.{GetSourceInfo(context)}");
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in binding.Properties)
        {
            usedKeys.Add(property.Name);
            var propertyValue = obj.TryGetProperty(property.Name, out var val) ? val : null;

            if (IsNullOrUndefined(propertyValue) && property.DefaultValue is not null)
            {
                propertyValue = EvaluateExpression(property.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            ApplyBindingTarget(property.Target, propertyValue, environment, context, mode);
        }

        if (binding.RestElement is not null)
        {
            var restObject = new JsObject();
            foreach (var kvp in obj)
            {
                if (!usedKeys.Contains(kvp.Key))
                {
                    restObject[kvp.Key] = kvp.Value;
                }
            }

            ApplyBindingTarget(binding.RestElement, restObject, environment, context, mode);
        }
    }

    private static (object? Value, SuperBinding Binding) ResolveSuperMember(MemberExpression expression,
        JsEnvironment environment, EvaluationContext context)
    {
        if (!context.IsThisInitialized)
        {
            throw CreateSuperReferenceError(environment, context, null);
        }

        var binding = ExpectSuperBinding(environment, context);
        var propertyValue = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return (JsSymbols.Undefined, binding);
        }

        var propertyName = ToPropertyName(propertyValue, context)
                           ?? throw new InvalidOperationException(
                               $"Property name cannot be null.{GetSourceInfo(context, expression.Source)}");

        if (context.ShouldStopEvaluation)
        {
            return (JsSymbols.Undefined, binding);
        }

        if (!binding.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidOperationException(
                $"Cannot read property '{propertyName}' from super prototype.{GetSourceInfo(context, expression.Source)}");
        }

        return (value, binding);
    }

    private static SuperBinding ExpectSuperBinding(JsEnvironment environment, EvaluationContext context)
    {
        try
        {
            if (environment.Get(JsSymbols.Super) is SuperBinding binding)
            {
                return binding;
            }
        }
        catch (InvalidOperationException ex)
        {
            var wrapped = CreateSuperReferenceError(environment, context, ex);
            if (wrapped is ThrowSignal throwSignal)
            {
                throw throwSignal;
            }

            throw wrapped;
        }

        var fallback = CreateSuperReferenceError(environment, context, null);
        if (fallback is ThrowSignal signal)
        {
            throw signal;
        }

        throw fallback;
    }

    private static Exception CreateSuperReferenceError(JsEnvironment environment, EvaluationContext context,
        Exception? inner)
    {
        var message = $"Super is not available in this context.{GetSourceInfo(context)}";
        if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctorVal) &&
            ctorVal is IJsCallable ctor)
        {
            var error = ctor.Invoke([message], JsSymbols.Undefined);
            return new ThrowSignal(error);
        }

        return new InvalidOperationException(message, inner);
    }

    private static string GetSourceInfo(EvaluationContext context, SourceReference? fallback = null)
    {
        var source = fallback ?? context.SourceReference;
        if (source is null)
        {
            return " (no source reference)";
        }

        var snippet = source.GetText();
        if (snippet.Length > 50)
        {
            snippet = snippet[..47] + "...";
        }

        return
            $" at {source} (snippet: '{snippet}') Source: '{source.Source}' Start: {source.StartPosition} End: {source.EndPosition}";
    }

    private static bool IsNullOrUndefined(object? value)
    {
        return value.IsNullish();
    }

    private static JsArray CreateArgumentsArray(IReadOnlyList<object?> arguments)
    {
        var array = new JsArray();
        for (var i = 0; i < arguments.Count; i++)
        {
            array.Push(arguments[i]);
        }

        array.DefineProperty("__arguments__",
            new PropertyDescriptor { Value = true, Writable = false, Enumerable = false, Configurable = false });

        return array;
    }

    private static void RestoreSignal(EvaluationContext context, ISignal? signal)
    {
        switch (signal)
        {
            case null:
                return;
            case ReturnSignal returnSignal:
                context.SetReturn(returnSignal.Value);
                break;
            case BreakSignal breakSignal:
                context.SetBreak(breakSignal.Label);
                break;
            case ContinueSignal continueSignal:
                context.SetContinue(continueSignal.Label);
                break;
            case ThrowFlowSignal throwSignal:
                context.SetThrow(throwSignal.Value);
                break;
        }
    }

    private static void BindFunctionParameters(FunctionExpression function, IReadOnlyList<object?> arguments,
        JsEnvironment environment, EvaluationContext context)
    {
        var argumentIndex = 0;

        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsRest)
            {
                if (parameter.Pattern is not null)
                {
                    throw new NotSupportedException("Rest parameters cannot use destructuring patterns.");
                }

                var restArray = new JsArray();
                for (; argumentIndex < arguments.Count; argumentIndex++)
                {
                    restArray.Push(arguments[argumentIndex]);
                }

                if (parameter.Name is null)
                {
                    throw new InvalidOperationException("Rest parameter must have an identifier.");
                }

                environment.Define(parameter.Name, restArray, isLexical: false);
                continue;
            }

            var value = argumentIndex < arguments.Count ? arguments[argumentIndex] : JsSymbols.Undefined;
            argumentIndex++;

            if (IsNullOrUndefined(value) && parameter.DefaultValue is not null)
            {
                value = EvaluateExpression(parameter.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (parameter.Pattern is not null)
            {
                ApplyBindingTarget(parameter.Pattern, value, environment, context, BindingMode.DefineParameter);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                continue;
            }

            if (parameter.Name is null)
            {
                throw new InvalidOperationException("Parameter must have an identifier when no pattern is provided.");
            }

            environment.Define(parameter.Name, value, isLexical: false);
        }
    }

    private static JsObject CreateGeneratorIteratorObject(
        Func<IReadOnlyList<object?>, object?> next,
        Func<IReadOnlyList<object?>, object?> @return,
        Func<IReadOnlyList<object?>, object?> @throw)
    {
        var iterator = new JsObject();
        iterator.SetProperty("next", new HostFunction(next));
        iterator.SetProperty("return", new HostFunction(@return));
        iterator.SetProperty("throw", new HostFunction(@throw));
        return iterator;
    }

    private sealed class GeneratorPendingCompletion
    {
        public bool HasValue { get; set; }
        public bool IsThrow { get; set; }
        public bool IsReturn { get; set; }
        public object? Value { get; set; }
    }

    private sealed class YieldResumeContext
    {
        private readonly Dictionary<int, ResumePayload> _pending = new();

        public void SetValue(int yieldIndex, object? value)
        {
            _pending[yieldIndex] = ResumePayload.FromValue(value);
        }

        public void SetException(int yieldIndex, object? value)
        {
            _pending[yieldIndex] = ResumePayload.FromThrow(value);
        }

        public void SetReturn(int yieldIndex, object? value)
        {
            _pending[yieldIndex] = ResumePayload.FromReturn(value);
        }

        public ResumePayload TakePayload(int yieldIndex)
        {
            if (_pending.TryGetValue(yieldIndex, out var payload))
            {
                _pending.Remove(yieldIndex);
                return payload;
            }

            return ResumePayload.Empty;
        }

        public void Clear()
        {
            _pending.Clear();
        }
    }

    private readonly record struct ResumePayload(bool HasValue, bool IsThrow, bool IsReturn, object? Value)
    {
        public static ResumePayload Empty { get; } = new(false, false, false, JsSymbols.Undefined);

        public static ResumePayload FromValue(object? value)
        {
            return new ResumePayload(true, false, false, value);
        }

        public static ResumePayload FromThrow(object? value)
        {
            return new ResumePayload(true, true, false, value);
        }

        public static ResumePayload FromReturn(object? value)
        {
            return new ResumePayload(true, false, true, value);
        }
    }

    private enum BindingMode
    {
        Assign,
        DefineLet,
        DefineConst,
        DefineVar,
        DefineParameter
    }

    private sealed class DelegatedYieldState
    {
        private readonly IEnumerator<object?>? _enumerator;
        private readonly bool _isGeneratorObject;
        private readonly JsObject? _iterator;

        private DelegatedYieldState(JsObject? iterator, IEnumerator<object?>? enumerator, bool isGeneratorObject)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _isGeneratorObject = isGeneratorObject;
        }

        public static DelegatedYieldState FromIterator(JsObject iterator)
        {
            return new DelegatedYieldState(iterator, null, IsGeneratorObject(iterator));
        }

        public static DelegatedYieldState FromEnumerable(IEnumerable<object?> enumerable)
        {
            return new DelegatedYieldState(null, enumerable.GetEnumerator(), false);
        }

        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow) MoveNext(
            object? sendValue,
            bool hasSendValue,
            bool propagateThrow,
            bool propagateReturn,
            EvaluationContext context,
            out bool awaitedPromise)
        {
            awaitedPromise = false;
            if (_iterator is not null)
            {
                JsObject? nextResult;
                object? candidate = null;
                var methodInvoked = false;
                if (propagateThrow)
                {
                    methodInvoked = TryInvokeIteratorMethod(
                        _iterator,
                        "throw",
                        sendValue ?? JsSymbols.Undefined,
                        context,
                        out candidate);
                }
                else if (propagateReturn)
                {
                    methodInvoked = TryInvokeIteratorMethod(
                        _iterator,
                        "return",
                        sendValue ?? JsSymbols.Undefined,
                        context,
                        out candidate);
                }
                else
                {
                    candidate = InvokeIteratorNext(_iterator, sendValue, hasSendValue);
                }

                if (!methodInvoked && candidate is null)
                {
                    return (JsSymbols.Undefined, true, propagateThrow, propagateThrow);
                }

                if (methodInvoked && candidate is null)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                var nextCandidate = candidate ?? throw new InvalidOperationException("Iterator result missing.");
                object? awaitedCandidate;
                if (nextCandidate is JsObject promiseCandidate && IsPromiseLike(promiseCandidate))
                {
                    awaitedPromise = true;
                    if (!AwaitScheduler.TryAwaitPromiseSync(promiseCandidate, context, out awaitedCandidate))
                    {
                        return (JsSymbols.Undefined, true, true, propagateThrow);
                    }
                }
                else
                {
                    awaitedCandidate = nextCandidate;
                }

                if (awaitedCandidate is not JsObject resolvedObject)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                nextResult = resolvedObject;

                var done = nextResult.TryGetProperty("done", out var doneValue) &&
                           doneValue is bool and true;
                var value = nextResult.TryGetProperty("value", out var yielded)
                    ? yielded
                    : JsSymbols.Undefined;
                var delegatedCompletion = _isGeneratorObject && (propagateThrow || propagateReturn);
                var propagateThrowResult = _isGeneratorObject && propagateThrow && done;
                return (value, done, delegatedCompletion, propagateThrowResult);
            }

            if (_enumerator is null)
            {
                if (propagateThrow)
                {
                    throw new ThrowSignal(sendValue);
                }

                return (JsSymbols.Undefined, true, propagateReturn, false);
            }

            if (propagateThrow)
            {
                throw new ThrowSignal(sendValue);
            }

            if (propagateReturn)
            {
                return (sendValue, true, true, false);
            }

            if (!_enumerator.MoveNext())
            {
                return (JsSymbols.Undefined, true, false, false);
            }

            return (_enumerator.Current, false, false, false);
        }

        private static bool IsGeneratorObject(JsObject iterator)
        {
            return iterator.TryGetProperty(GeneratorBrandPropertyName, out var brand) &&
                   ReferenceEquals(brand, GeneratorBrandMarker);
        }
    }


    private sealed class TypedGeneratorFactory : IJsCallable
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly RealmState _realmState;

        public TypedGeneratorFactory(FunctionExpression function, JsEnvironment closure, RealmState realmState)
        {
            if (!function.IsGenerator)
            {
                throw new ArgumentException("Factory can only wrap generator functions.", nameof(function));
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
        }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var instance = new TypedGeneratorInstance(_function, _closure, arguments, thisValue, this, _realmState);
            return instance.CreateGeneratorObject();
        }

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[GeneratorFunction: {name.Name}]"
                : "[GeneratorFunction]";
        }
    }

    private sealed class AsyncGeneratorFactory : IJsCallable
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly RealmState _realmState;

        public AsyncGeneratorFactory(FunctionExpression function, JsEnvironment closure, RealmState realmState)
        {
            if (!function.IsGenerator || !function.IsAsync)
            {
                throw new ArgumentException("Factory can only wrap async generator functions.", nameof(function));
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
        }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var instance = new AsyncGeneratorInstance(_function, _closure, arguments, thisValue, this, _realmState);
            return instance.CreateAsyncIteratorObject();
        }

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[AsyncGeneratorFunction: {name.Name}]"
                : "[AsyncGeneratorFunction]";
        }
    }

    private sealed class TypedGeneratorInstance
    {
        private readonly IReadOnlyList<object?> _arguments;
        private readonly IJsCallable _callable;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly GeneratorPlan? _plan;
        private readonly RealmState _realmState;
        private readonly YieldResumeContext _resumeContext = new();
        private readonly object? _thisValue;
        private readonly Stack<TryFrame> _tryStack = new();
        private bool _asyncStepMode;
        private EvaluationContext? _context;
        private int _currentInstructionIndex;
        private int _currentYieldIndex;
        private bool _done;
        private JsEnvironment? _executionEnvironment;

        private Symbol? _pendingAwaitKey;
        private object? _pendingPromise;
        private ResumePayloadKind _pendingResumeKind;
        private object? _pendingResumeValue = JsSymbols.Undefined;
        private int _programCounter;
        private GeneratorState _state = GeneratorState.Start;

        public TypedGeneratorInstance(FunctionExpression function, JsEnvironment closure,
            IReadOnlyList<object?> arguments, object? thisValue, IJsCallable callable, RealmState realmState)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _callable = callable;
            _realmState = realmState;

            if (!GeneratorIrBuilder.TryBuild(function, out var plan, out var failureReason))
            {
                var reason = failureReason ?? "Generator contains unsupported construct for IR.";
                throw new NotSupportedException($"Generator IR not implemented for this function: {reason}");
            }

            _plan = plan;
            _programCounter = plan.EntryPoint;
        }

        public JsObject CreateGeneratorObject()
        {
            var iterator = CreateGeneratorIteratorObject(
                args => Next(args.Count > 0 ? args[0] : JsSymbols.Undefined),
                args => Return(args.Count > 0 ? args[0] : null),
                args => Throw(args.Count > 0 ? args[0] : null));
            iterator.SetProperty(IteratorSymbolPropertyName, new HostFunction((_, _) => iterator));
            iterator.SetProperty(GeneratorBrandPropertyName, GeneratorBrandMarker);
            return iterator;
        }

        private object? Next(object? value)
        {
            return ExecutePlan(ResumeMode.Next, value);
        }

        private object? Return(object? value)
        {
            return ExecutePlan(ResumeMode.Return, value);
        }

        private object? Throw(object? error)
        {
            return ExecutePlan(ResumeMode.Throw, error);
        }

        internal AsyncGeneratorStepResult ExecuteAsyncStep(ResumeMode mode, object? resumeValue)
        {
            // Reuse the existing ExecutePlan logic but translate its iterator
            // result / exceptions into a structured step result that async
            // generators can consume without throwing. This entrypoint also
            // marks the executor as async-aware so future steps can surface
            // pending Promises instead of blocking.
            var previousAsyncStepMode = _asyncStepMode;
            _asyncStepMode = true;
            _pendingPromise = null;

            try
            {
                var result = ExecutePlan(mode, resumeValue);

                if (_pendingPromise is JsObject pending)
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsSymbols.Undefined, false,
                        pending);
                }

                if (result is JsObject obj &&
                    obj.TryGetProperty("done", out var doneRaw) &&
                    doneRaw is bool done &&
                    obj.TryGetProperty("value", out var value))
                {
                    return done
                        ? new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, value, true, null)
                        : new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Yield, value, false, null);
                }

                // If the plan completed without producing a well-formed iterator
                // result, treat it as a completed step with undefined.
                return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Completed, JsSymbols.Undefined, true, null);
            }
            catch (PendingAwaitException)
            {
                if (_pendingPromise is JsObject pending)
                {
                    return new AsyncGeneratorStepResult(AsyncGeneratorStepKind.Pending, JsSymbols.Undefined, false,
                        pending);
                }

                throw new InvalidOperationException("Async generator awaited a non-promise value.");
            }
            finally
            {
                _asyncStepMode = previousAsyncStepMode;
                _pendingPromise = null;
            }
        }

        private JsEnvironment CreateExecutionEnvironment()
        {
            var description = _function.Name is { } name
                ? $"function* {name.Name}"
                : "generator function";
            var environment = new JsEnvironment(_closure, true, _function.Body.IsStrict, _function.Source, description);
            environment.Define(JsSymbols.This, _thisValue ?? new JsObject());
            environment.Define(YieldResumeContextSymbol, _resumeContext);
            environment.Define(GeneratorInstanceSymbol, this);

            // Define `arguments` inside generator functions so generator bodies
            // can observe the values they were invoked with.
            environment.Define(JsSymbols.Arguments, CreateArgumentsArray(_arguments));

            if (_function.Name is { } functionName)
            {
                environment.Define(functionName, _callable);
            }

            HoistVarDeclarations(_function.Body, environment, new EvaluationContext(_realmState));

            var bindingContext = new EvaluationContext(_realmState);
            BindFunctionParameters(_function, _arguments, environment, bindingContext);
            if (bindingContext.IsThrow)
            {
                var thrown = bindingContext.FlowValue;
                bindingContext.Clear();
                throw new ThrowSignal(thrown);
            }

            if (bindingContext.IsReturn)
            {
                bindingContext.ClearReturn();
            }

            return environment;
        }

        private static JsObject CreateIteratorResult(object? value, bool done)
        {
            var result = new JsObject();
            result.SetProperty("value", value);
            result.SetProperty("done", done);
            return result;
        }

        private static IteratorDriverState CreateIteratorDriverState(object? iterable, IteratorDriverKind kind)
        {
            if (TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
            {
                return new IteratorDriverState
                {
                    IteratorObject = iterator, Enumerator = null, IsAsyncIterator = kind == IteratorDriverKind.Await
                };
            }

            var enumerable = EnumerateValues(iterable);
            return new IteratorDriverState
            {
                IteratorObject = null,
                Enumerator = enumerable.GetEnumerator(),
                IsAsyncIterator = kind == IteratorDriverKind.Await
            };
        }

        private static void StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? value)
        {
            if (environment.TryGet(symbol, out _))
            {
                environment.Assign(symbol, value);
            }
            else
            {
                environment.Define(symbol, value);
            }
        }

        private static bool TryGetSymbolValue(JsEnvironment environment, Symbol symbol, out object? value)
        {
            if (environment.TryGet(symbol, out var existing))
            {
                value = existing;
                return true;
            }

            value = null;
            return false;
        }

        private object? ExecutePlan(ResumeMode mode, object? resumeValue)
        {
            if (_plan is null)
            {
                throw new InvalidOperationException("No generator plan available.");
            }

            var wasStart = _state == GeneratorState.Start;
            if (_done || _state == GeneratorState.Completed)
            {
                _done = true;
                return FinishExternalCompletion(mode, resumeValue);
            }

            if ((mode == ResumeMode.Throw || mode == ResumeMode.Return) && wasStart)
            {
                _state = GeneratorState.Completed;
                _done = true;
                return FinishExternalCompletion(mode, resumeValue);
            }

            _state = GeneratorState.Executing;
            PreparePendingResumeValue(mode, resumeValue, wasStart);

            var environment = EnsureExecutionEnvironment();
            var context = EnsureEvaluationContext();

            // If we are resuming after a pending await, thread the resolved
            // value into the per-site await state so subsequent evaluations
            // of the AwaitExpression see the fulfilled value instead of the
            // original promise object.
            if (_pendingAwaitKey is { } awaitKey)
            {
                var (kind, value) = ConsumeResumeValue();
                if (kind == ResumePayloadKind.Value)
                {
                    if (environment.TryGet(awaitKey, out var stateObj) && stateObj is AwaitState state)
                    {
                        state.HasResult = true;
                        state.Result = value;
                        environment.Assign(awaitKey, state);
                    }
                    else
                    {
                        var newState = new AwaitState { HasResult = true, Result = value };
                        if (environment.TryGet(awaitKey, out _))
                        {
                            environment.Assign(awaitKey, newState);
                        }
                        else
                        {
                            environment.Define(awaitKey, newState);
                        }
                    }
                }

                _pendingAwaitKey = null;
            }

            try
            {
                while (_programCounter >= 0 && _programCounter < _plan.Instructions.Length)
                {
                    _currentInstructionIndex = _programCounter;
                    var instruction = _plan.Instructions[_programCounter];
                    switch (instruction)
                    {
                        case StatementInstruction statementInstruction:
                            EvaluateStatement(statementInstruction.Statement, environment, context);
                            if (context.IsThrow)
                            {
                                var thrown = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(thrown);
                            }

                            if (context.IsReturn)
                            {
                                var returnSignalValue = context.FlowValue;
                                context.ClearReturn();
                                if (HandleAbruptCompletion(AbruptKind.Return, returnSignalValue, environment))
                                {
                                    continue;
                                }

                                return CompleteReturn(returnSignalValue);
                            }

                            _programCounter = statementInstruction.Next;
                            continue;

                        case YieldInstruction yieldInstruction:
                            object? yieldedValue = JsSymbols.Undefined;
                            if (yieldInstruction.YieldExpression is not null)
                            {
                                yieldedValue = EvaluateExpression(yieldInstruction.YieldExpression, environment,
                                    context);
                                if (context.IsThrow)
                                {
                                    var thrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(thrown);
                                }
                            }

                            _programCounter = yieldInstruction.Next;
                            _state = GeneratorState.Suspended;
                            return CreateIteratorResult(yieldedValue, false);

                        case YieldStarInstruction yieldStarInstruction:
                        {
                            var currentIndex = _programCounter;
                            if (!TryGetSymbolValue(environment, yieldStarInstruction.StateSlotSymbol,
                                    out var stateValue) ||
                                stateValue is not YieldStarState yieldStarState)
                            {
                                yieldStarState = new YieldStarState();
                                StoreSymbolValue(environment, yieldStarInstruction.StateSlotSymbol, yieldStarState);
                            }

                            if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                                _pendingResumeKind is not ResumePayloadKind.Throw and not ResumePayloadKind.Return)
                            {
                                var pendingKind = yieldStarState.PendingAbrupt;
                                var pendingValue = yieldStarState.PendingValue;
                                yieldStarState.PendingAbrupt = AbruptKind.None;
                                yieldStarState.PendingValue = null;
                                yieldStarState.State = null;
                                yieldStarState.AwaitingResume = false;
                                environment.Assign(yieldStarInstruction.StateSlotSymbol, null);

                                if (pendingKind == AbruptKind.Throw)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Throw, pendingValue, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(pendingValue);
                                }

                                if (pendingKind == AbruptKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, pendingValue, environment))
                                    {
                                        continue;
                                    }

                                    return CompleteReturn(pendingValue);
                                }
                            }

                            if (yieldStarState.State is null)
                            {
                                var yieldStarIterable =
                                    EvaluateExpression(yieldStarInstruction.IterableExpression, environment, context);
                                if (context.IsThrow)
                                {
                                    var thrown = context.FlowValue;
                                    context.Clear();
                                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(thrown);
                                }

                                yieldStarState.State = CreateDelegatedState(yieldStarIterable);
                                yieldStarState.AwaitingResume = false;
                            }

                            while (true)
                            {
                                object? sendValue = JsSymbols.Undefined;
                                var hasSendValue = false;
                                var propagateThrow = false;
                                var propagateReturn = false;

                                if (yieldStarState.AwaitingResume)
                                {
                                    var (delegatedResumeKind, delegatedResumePayload) = ConsumeResumeValue();
                                    switch (delegatedResumeKind)
                                    {
                                        case ResumePayloadKind.Throw:
                                            propagateThrow = true;
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                        case ResumePayloadKind.Return:
                                            propagateReturn = true;
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                        default:
                                            hasSendValue = true;
                                            sendValue = delegatedResumePayload;
                                            break;
                                    }
                                }

                                var iteratorResult = yieldStarState.State!.MoveNext(
                                    sendValue,
                                    hasSendValue,
                                    propagateThrow,
                                    propagateReturn,
                                    context,
                                    out _);

                                if (iteratorResult.IsDelegatedCompletion)
                                {
                                    var pendingKind = propagateThrow ? AbruptKind.Throw : AbruptKind.Return;
                                    object? abruptValue;
                                    if (pendingKind == AbruptKind.Throw && context.IsThrow)
                                    {
                                        abruptValue = context.FlowValue;
                                        context.Clear();
                                    }
                                    else
                                    {
                                        abruptValue = pendingKind == AbruptKind.Throw
                                            ? sendValue
                                            : iteratorResult.Value;
                                    }

                                    if (!iteratorResult.Done)
                                    {
                                        yieldStarState.PendingAbrupt = pendingKind;
                                        yieldStarState.PendingValue = sendValue;
                                        yieldStarState.AwaitingResume = true;
                                        _programCounter = currentIndex;
                                        _state = GeneratorState.Suspended;
                                        return CreateIteratorResult(iteratorResult.Value, false);
                                    }

                                    yieldStarState.State = null;
                                    yieldStarState.AwaitingResume = false;
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);

                                    if (pendingKind == AbruptKind.Throw)
                                    {
                                        if (HandleAbruptCompletion(AbruptKind.Throw, abruptValue, environment))
                                        {
                                            break;
                                        }

                                        _tryStack.Clear();
                                        throw new ThrowSignal(abruptValue);
                                    }

                                    if (HandleAbruptCompletion(AbruptKind.Return, abruptValue, environment))
                                    {
                                        break;
                                    }

                                    return CompleteReturn(abruptValue);
                                }

                                if (iteratorResult.Done && !propagateThrow && !propagateReturn)
                                {
                                    yieldStarState.State = null;
                                    yieldStarState.AwaitingResume = false;
                                    environment.Assign(yieldStarInstruction.StateSlotSymbol, null);
                                    if (yieldStarInstruction.ResultSlotSymbol is { } resultSlot)
                                    {
                                        StoreSymbolValue(environment, resultSlot, iteratorResult.Value);
                                    }

                                    _programCounter = yieldStarInstruction.Next;
                                    break;
                                }

                                yieldStarState.AwaitingResume = true;
                                _programCounter = currentIndex;
                                _state = GeneratorState.Suspended;
                                var resultDone = propagateReturn ? iteratorResult.Done : false;
                                return CreateIteratorResult(iteratorResult.Value, resultDone);
                            }

                            continue;
                        }

                        case StoreResumeValueInstruction storeResumeValueInstruction:
                            var (resumeKind, resumePayload) = ConsumeResumeValue();
                            if (resumeKind == ResumePayloadKind.Throw)
                            {
                                context.SetThrow(resumePayload);
                            }
                            else if (resumeKind == ResumePayloadKind.Return)
                            {
                                context.SetReturn(resumePayload);
                            }
                            else if (storeResumeValueInstruction.TargetSymbol is { } resumeSymbol)
                            {
                                if (environment.TryGet(resumeSymbol, out _))
                                {
                                    environment.Assign(resumeSymbol, resumePayload);
                                }
                                else
                                {
                                    environment.Define(resumeSymbol, resumePayload);
                                }
                            }

                            if (context.IsThrow)
                            {
                                var thrownPayload = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownPayload, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(thrownPayload);
                            }

                            if (context.IsReturn)
                            {
                                var resumeReturnValue = context.FlowValue;
                                context.ClearReturn();
                                if (HandleAbruptCompletion(AbruptKind.Return, resumeReturnValue, environment))
                                {
                                    continue;
                                }

                                return CompleteReturn(resumeReturnValue);
                            }

                            _programCounter = storeResumeValueInstruction.Next;
                            continue;

                        case EnterTryInstruction enterTryInstruction:
                            PushTryFrame(enterTryInstruction, environment);
                            _programCounter = enterTryInstruction.Next;
                            continue;

                        case LeaveTryInstruction leaveTryInstruction:
                            CompleteTryNormally(leaveTryInstruction.Next);
                            continue;

                        case EndFinallyInstruction endFinallyInstruction:
                            if (_tryStack.Count == 0)
                            {
                                _programCounter = endFinallyInstruction.Next;
                                continue;
                            }

                            var completedFrame = _tryStack.Pop();
                            var pending = completedFrame.PendingCompletion;
                            // Console.WriteLine($"[IR] EndFinally: pending={pending.Kind}, value={pending.Value}, resume={pending.ResumeTarget}, stack={_tryStack.Count}");
                            if (pending.Kind == AbruptKind.None)
                            {
                                var target = pending.ResumeTarget >= 0
                                    ? pending.ResumeTarget
                                    : endFinallyInstruction.Next;
                                _programCounter = target;
                                continue;
                            }

                            if (pending.Kind == AbruptKind.Return)
                            {
                                if (HandleAbruptCompletion(AbruptKind.Return, pending.Value, environment))
                                {
                                    continue;
                                }

                                return CompleteReturn(pending.Value);
                            }

                            if (pending.Kind == AbruptKind.Break || pending.Kind == AbruptKind.Continue)
                            {
                                if (HandleAbruptCompletion(pending.Kind, pending.Value, environment))
                                {
                                    continue;
                                }

                                _programCounter = pending.Value is int idx ? idx : endFinallyInstruction.Next;
                                continue;
                            }

                            if (HandleAbruptCompletion(AbruptKind.Throw, pending.Value, environment))
                            {
                                continue;
                            }

                            _tryStack.Clear();
                            throw new ThrowSignal(pending.Value);

                        case IteratorInitInstruction iteratorInitInstruction:
                            var iterableValue = EvaluateExpression(iteratorInitInstruction.IterableExpression,
                                environment, context);
                            if (context.IsThrow)
                            {
                                var initThrown = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, initThrown, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(initThrown);
                            }

                            var iteratorState = CreateIteratorDriverState(iterableValue, iteratorInitInstruction.Kind);
                            StoreSymbolValue(environment, iteratorInitInstruction.IteratorSlot, iteratorState);
                            _programCounter = iteratorInitInstruction.Next;
                            continue;

                        case IteratorMoveNextInstruction iteratorMoveNextInstruction:
                            var iteratorIndex = _programCounter;
                            if (!TryGetSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                    out var iteratorStateValue) ||
                                iteratorStateValue is not IteratorDriverState driverState)
                            {
                                _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                continue;
                            }

                            if (!driverState.IsAsyncIterator)
                            {
                                object? currentValue;
                                if (driverState.IteratorObject is JsObject iteratorObj)
                                {
                                    var nextResult = InvokeIteratorNext(iteratorObj);
                                    if (nextResult is not JsObject resultObj)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    var done = resultObj.TryGetProperty("done", out var doneValue) &&
                                               doneValue is bool and true;
                                    if (done)
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = resultObj.TryGetProperty("value", out var yielded)
                                        ? yielded
                                        : JsSymbols.Undefined;
                                }
                                else if (driverState.Enumerator is IEnumerator<object?> enumerator)
                                {
                                    if (!enumerator.MoveNext())
                                    {
                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    currentValue = enumerator.Current;
                                }
                                else
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, currentValue);
                                _programCounter = iteratorMoveNextInstruction.Next;
                                continue;
                            }

                            object? awaitedValue = null;
                            object? awaitedNextResult = null;

                            // If we're resuming after a pending await from this
                            // iterator site, consume the resume payload and treat
                            // it as the awaited result instead of calling into the
                            // iterator again.
                            if (driverState.AwaitingNextResult || driverState.AwaitingValue)
                            {
                                var awaitingValue = driverState.AwaitingValue;
                                driverState.AwaitingNextResult = false;
                                driverState.AwaitingValue = false;
                                var (forAwaitResumeKind, forAwaitResumePayload) = ConsumeResumeValue();
                                StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot, driverState);

                                if (forAwaitResumeKind == ResumePayloadKind.Throw)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload, environment))
                                    {
                                        continue;
                                    }

                                    _tryStack.Clear();
                                    throw new ThrowSignal(forAwaitResumePayload);
                                }

                                if (forAwaitResumeKind == ResumePayloadKind.Return)
                                {
                                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload, environment))
                                    {
                                        continue;
                                    }

                                    return CompleteReturn(forAwaitResumePayload);
                                }

                                if (awaitingValue)
                                {
                                    awaitedValue = forAwaitResumePayload;
                                    goto StoreIteratorValue;
                                }

                                awaitedNextResult = forAwaitResumePayload;
                            }

                            if (driverState.IteratorObject is JsObject awaitIteratorObj)
                            {
                                if (awaitedNextResult is null)
                                {
                                    var nextResult = InvokeIteratorNext(awaitIteratorObj);
                                    if (!TryAwaitPromiseOrSchedule(nextResult, context, out var awaitedNext))
                                    {
                                        if (_asyncStepMode && _pendingPromise is JsObject)
                                        {
                                            driverState.AwaitingNextResult = true;
                                            StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                                driverState);
                                            _state = GeneratorState.Suspended;
                                            _programCounter = iteratorIndex;
                                            return CreateIteratorResult(JsSymbols.Undefined, false);
                                        }

                                        if (context.IsThrow)
                                        {
                                            var thrownAwait = context.FlowValue;
                                            context.Clear();
                                            if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwait, environment))
                                            {
                                                continue;
                                            }

                                            _tryStack.Clear();
                                            throw new ThrowSignal(thrownAwait);
                                        }

                                        _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                        continue;
                                    }

                                    awaitedNextResult = awaitedNext;
                                }

                                if (awaitedNextResult is not JsObject awaitResultObj)
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                                awaitDoneValue is bool and true;
                                if (doneAwait)
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                                    ? yieldedAwait
                                    : JsSymbols.Undefined;
                                if (!TryAwaitPromiseOrSchedule(rawValue, context, out var fullyAwaitedValue))
                                {
                                    if (_asyncStepMode && _pendingPromise is JsObject)
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return CreateIteratorResult(JsSymbols.Undefined, false);
                                    }

                                    if (context.IsThrow)
                                    {
                                        var thrownAwaitValue = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwaitValue, environment))
                                        {
                                            continue;
                                        }

                                        _tryStack.Clear();
                                        throw new ThrowSignal(thrownAwaitValue);
                                    }

                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                awaitedValue = fullyAwaitedValue;
                            }
                            else if (driverState.Enumerator is IEnumerator<object?> awaitEnumerator)
                            {
                                if (!awaitEnumerator.MoveNext())
                                {
                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                var enumerated = awaitEnumerator.Current;
                                if (!TryAwaitPromiseOrSchedule(enumerated, context, out var awaitedEnumerated))
                                {
                                    if (_asyncStepMode && _pendingPromise is JsObject)
                                    {
                                        driverState.AwaitingValue = true;
                                        StoreSymbolValue(environment, iteratorMoveNextInstruction.IteratorSlot,
                                            driverState);
                                        _state = GeneratorState.Suspended;
                                        _programCounter = iteratorIndex;
                                        return CreateIteratorResult(JsSymbols.Undefined, false);
                                    }

                                    if (context.IsThrow)
                                    {
                                        var thrownAwaitEnum = context.FlowValue;
                                        context.Clear();
                                        if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwaitEnum, environment))
                                        {
                                            continue;
                                        }

                                        _tryStack.Clear();
                                        throw new ThrowSignal(thrownAwaitEnum);
                                    }

                                    _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                    continue;
                                }

                                awaitedValue = awaitedEnumerated;
                            }
                            else
                            {
                                _programCounter = iteratorMoveNextInstruction.BreakIndex;
                                continue;
                            }

                            StoreIteratorValue:
                            StoreSymbolValue(environment, iteratorMoveNextInstruction.ValueSlot, awaitedValue);
                            _programCounter = iteratorMoveNextInstruction.Next;
                            continue;

                        case JumpInstruction jumpInstruction:
                            _programCounter = jumpInstruction.TargetIndex;
                            continue;

                        case BranchInstruction branchInstruction:
                            var testValue = EvaluateExpression(branchInstruction.Condition, environment, context);
                            if (context.IsThrow)
                            {
                                var thrownBranch = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownBranch, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(thrownBranch);
                            }

                            _programCounter = IsTruthy(testValue)
                                ? branchInstruction.ConsequentIndex
                                : branchInstruction.AlternateIndex;
                            continue;

                        case BreakInstruction breakInstruction:
                            if (HandleAbruptCompletion(AbruptKind.Break, breakInstruction.TargetIndex, environment))
                            {
                                continue;
                            }

                            _programCounter = breakInstruction.TargetIndex;
                            continue;

                        case ContinueInstruction continueInstruction:
                            if (HandleAbruptCompletion(AbruptKind.Continue, continueInstruction.TargetIndex,
                                    environment))
                            {
                                continue;
                            }

                            _programCounter = continueInstruction.TargetIndex;
                            continue;

                        case ReturnInstruction returnInstruction:
                            var returnValue = returnInstruction.ReturnExpression is null
                                ? JsSymbols.Undefined
                                : EvaluateExpression(returnInstruction.ReturnExpression, environment, context);
                            if (context.IsThrow)
                            {
                                var pendingThrow = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, pendingThrow, environment))
                                {
                                    continue;
                                }

                                _tryStack.Clear();
                                throw new ThrowSignal(pendingThrow);
                            }

                            if (context.IsReturn)
                            {
                                var pendingReturn = context.FlowValue;
                                context.ClearReturn();
                                returnValue = pendingReturn;
                            }

                            if (HandleAbruptCompletion(AbruptKind.Return, returnValue, environment))
                            {
                                continue;
                            }

                            _programCounter = -1;
                            _state = GeneratorState.Completed;
                            _done = true;
                            _tryStack.Clear();
                            return CreateIteratorResult(returnValue, true);

                        default:
                            throw new InvalidOperationException(
                                $"Unsupported generator instruction {instruction.GetType().Name}");
                    }
                }
            }
            catch (PendingAwaitException)
            {
                // A pending await surfaced from within the generator body.
                // Async-aware callers translate this into a Pending step so
                // the generator can resume once the promise settles.
                if (_asyncStepMode)
                {
                    throw;
                }

                return CreateIteratorResult(JsSymbols.Undefined, false);
            }

            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return CreateIteratorResult(JsSymbols.Undefined, true);
        }

        private JsEnvironment EnsureExecutionEnvironment()
        {
            return _executionEnvironment ??= CreateExecutionEnvironment();
        }

        private EvaluationContext EnsureEvaluationContext()
        {
            if (_context is null)
            {
                _context = new EvaluationContext(_realmState);
            }
            else
            {
                _context.Clear();
            }

            return _context;
        }

        private object? ResumeGenerator(ResumeMode mode, object? value)
        {
            var completed = _done || _state == GeneratorState.Completed;
            if (completed)
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return FinishExternalCompletion(mode, value);
            }

            var wasStart = _state == GeneratorState.Start;
            if ((mode == ResumeMode.Throw || mode == ResumeMode.Return) && wasStart)
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return FinishExternalCompletion(mode, value);
            }

            try
            {
                _state = GeneratorState.Executing;

                _executionEnvironment ??= CreateExecutionEnvironment();

                if (!wasStart && _currentYieldIndex > 0)
                {
                    switch (mode)
                    {
                        case ResumeMode.Throw:
                            _resumeContext.SetException(_currentYieldIndex - 1, value);
                            break;
                        case ResumeMode.Return:
                            _resumeContext.SetReturn(_currentYieldIndex - 1, value);
                            break;
                        default:
                            _resumeContext.SetValue(_currentYieldIndex - 1, value);
                            break;
                    }
                }

                var context = new EvaluationContext(_realmState);
                _executionEnvironment.Define(YieldTrackerSymbol, new YieldTracker(_currentYieldIndex));

                var result = EvaluateBlock(_function.Body, _executionEnvironment, context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    _state = GeneratorState.Completed;
                    _done = true;
                    _resumeContext.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (context.IsYield)
                {
                    var yielded = context.FlowValue;
                    context.Clear();
                    _state = GeneratorState.Suspended;
                    _currentYieldIndex++;
                    return CreateIteratorResult(yielded, false);
                }

                if (context.IsReturn)
                {
                    var returnValue = context.FlowValue;
                    context.ClearReturn();
                    _state = GeneratorState.Completed;
                    _done = true;
                    _resumeContext.Clear();
                    return CreateIteratorResult(returnValue, true);
                }

                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                return CreateIteratorResult(result, true);
            }
            catch
            {
                _state = GeneratorState.Completed;
                _done = true;
                _resumeContext.Clear();
                throw;
            }
        }

        private static object? FinishExternalCompletion(ResumeMode mode, object? value)
        {
            return mode switch
            {
                ResumeMode.Throw => throw new ThrowSignal(value),
                _ => CreateIteratorResult(value, true)
            };
        }

        internal object? EvaluateAwaitInGenerator(AwaitExpression expression, JsEnvironment environment,
            EvaluationContext context)
        {
            // When not executing under async-aware stepping, fall back to the
            // legacy blocking helper so synchronous generators remain usable.
            if (!_asyncStepMode)
            {
                var awaitedValueSync = EvaluateExpression(expression.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return awaitedValueSync;
                }

                if (!TryAwaitPromise(awaitedValueSync, context, out var resolvedSync))
                {
                    return resolvedSync;
                }

                return resolvedSync;
            }

            // Async-aware mode: use per-site await state so we don't re-run
            // side-effecting expressions after the promise has resolved.
            var awaitKey = GetAwaitStateKey(expression);
            AwaitState? existingState = null;
            if (awaitKey is not null &&
                environment.TryGet(awaitKey, out var stateObj) &&
                stateObj is AwaitState { HasResult: true } state)
            {
                // Await has already completed; reuse the resolved value once
                // for this resume, then clear the flag so future iterations
                // (e.g. in loops) see a fresh await.
                var result = state.Result;
                environment.Assign(awaitKey, new AwaitState());
                _pendingAwaitKey = null;
                return result;
            }

            var awaitedValue = EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return awaitedValue;
            }

            if (awaitKey is not null)
            {
                existingState = new AwaitState();

                if (environment.TryGet(awaitKey, out _))
                {
                    environment.Assign(awaitKey, existingState);
                }
                else
                {
                    environment.Define(awaitKey, existingState);
                }
            }

            // Async-aware mode: surface promise-like values as pending steps
            // so AsyncGeneratorInstance can resume via the event queue.
            if (!TryAwaitPromiseOrSchedule(awaitedValue, context, out var resolved))
            {
                if (_pendingPromise is JsObject && awaitKey is not null)
                {
                    // Remember which await site is pending so we can stash the
                    // resolved value on resume.
                    _pendingAwaitKey = awaitKey;
                    _state = GeneratorState.Suspended;
                    _programCounter = _currentInstructionIndex;
                    throw new PendingAwaitException();
                }

                // If TryAwaitPromiseOrSchedule reported an error via the context,
                // let the caller observe the pending throw/return.
                return resolved;
            }

            return resolved;
        }

        private bool TryAwaitPromiseOrSchedule(object? candidate, EvaluationContext context, out object? resolvedValue)
        {
            var pendingPromise = _pendingPromise;
            var result = AwaitScheduler.TryAwaitPromiseOrSchedule(candidate, _asyncStepMode, ref pendingPromise,
                context, out resolvedValue);
            _pendingPromise = pendingPromise;
            return result;
        }

        private void PreparePendingResumeValue(ResumeMode mode, object? resumeValue, bool wasStart)
        {
            if (wasStart)
            {
                _pendingResumeKind = ResumePayloadKind.None;
                _pendingResumeValue = JsSymbols.Undefined;
                return;
            }

            switch (mode)
            {
                case ResumeMode.Throw:
                    _pendingResumeKind = ResumePayloadKind.Throw;
                    _pendingResumeValue = resumeValue;
                    break;
                case ResumeMode.Return:
                    _pendingResumeKind = ResumePayloadKind.Return;
                    _pendingResumeValue = resumeValue;
                    break;
                default:
                    _pendingResumeKind = ResumePayloadKind.Value;
                    _pendingResumeValue = resumeValue;
                    break;
            }
        }

        private (ResumePayloadKind Kind, object? Value) ConsumeResumeValue()
        {
            var kind = _pendingResumeKind;
            var value = _pendingResumeValue;
            _pendingResumeKind = ResumePayloadKind.None;
            _pendingResumeValue = JsSymbols.Undefined;

            if (kind == ResumePayloadKind.None)
            {
                return (ResumePayloadKind.Value, JsSymbols.Undefined);
            }

            return (kind, value);
        }

        private void PushTryFrame(EnterTryInstruction instruction, JsEnvironment environment)
        {
            var frame = new TryFrame(instruction.HandlerIndex, instruction.CatchSlotSymbol, instruction.FinallyIndex);
            if (instruction.CatchSlotSymbol is { } slot && !environment.TryGet(slot, out _))
            {
                environment.Define(slot, JsSymbols.Undefined);
            }

            _tryStack.Push(frame);
        }

        private void CompleteTryNormally(int resumeTarget)
        {
            if (_tryStack.Count == 0)
            {
                _programCounter = resumeTarget;
                return;
            }

            var frame = _tryStack.Peek();
            if (frame is { FinallyIndex: >= 0, FinallyScheduled: false })
            {
                frame.FinallyScheduled = true;
                frame.PendingCompletion = PendingCompletion.FromNormal(resumeTarget);
                _programCounter = frame.FinallyIndex;
                return;
            }

            _tryStack.Pop();
            _programCounter = resumeTarget;
        }

        private bool HandleAbruptCompletion(AbruptKind kind, object? value, JsEnvironment environment)
        {
            // Console.WriteLine($"[IR] HandleAbruptCompletion kind={kind}, value={value}, stack={_tryStack.Count}");
            while (_tryStack.Count > 0)
            {
                var frame = _tryStack.Peek();
                if (kind == AbruptKind.Throw && frame is { HandlerIndex: >= 0, CatchUsed: false })
                {
                    frame.CatchUsed = true;
                    if (frame.CatchSlotSymbol is { } slot)
                    {
                        if (environment.TryGet(slot, out _))
                        {
                            environment.Assign(slot, value);
                        }
                        else
                        {
                            environment.Define(slot, value);
                        }
                    }

                    _programCounter = frame.HandlerIndex;
                    return true;
                }

                if (frame.FinallyIndex >= 0)
                {
                    if (!frame.FinallyScheduled)
                    {
                        frame.FinallyScheduled = true;
                        frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                        _programCounter = frame.FinallyIndex;
                        return true;
                    }

                    frame.PendingCompletion = PendingCompletion.FromAbrupt(kind, value);
                    return true;
                }

                _tryStack.Pop();
            }

            return false;
        }

        private object? CompleteReturn(object? value)
        {
            _programCounter = -1;
            _state = GeneratorState.Completed;
            _done = true;
            _tryStack.Clear();
            return CreateIteratorResult(value, true);
        }

        private sealed class PendingAwaitException : Exception
        {
        }


        private sealed class AwaitState
        {
            public bool HasResult { get; set; }
            public object? Result { get; set; }
        }

        // Lightweight step result used by async-generator wrappers so they can
        // drive the same IR plan without duplicating the interpreter. This
        // supports yield/completion/throw, and has room for a future "Pending"
        // state that surfaces promise-like values without blocking.
        internal readonly record struct AsyncGeneratorStepResult(
            AsyncGeneratorStepKind Kind,
            object? Value,
            bool Done,
            object? PendingPromise);

        internal enum AsyncGeneratorStepKind
        {
            Yield,
            Completed,
            Throw,
            Pending
        }

        internal enum ResumeMode
        {
            Next,
            Throw,
            Return
        }

        private enum GeneratorState
        {
            Start,
            Suspended,
            Executing,
            Completed
        }

        private enum ResumePayloadKind
        {
            None,
            Value,
            Throw,
            Return
        }

        private enum AbruptKind
        {
            None,
            Return,
            Throw,
            Break,
            Continue
        }

        private sealed class TryFrame(int handlerIndex, Symbol? catchSlotSymbol, int finallyIndex)
        {
            public int HandlerIndex { get; } = handlerIndex;
            public Symbol? CatchSlotSymbol { get; } = catchSlotSymbol;
            public int FinallyIndex { get; } = finallyIndex;
            public bool CatchUsed { get; set; }
            public bool FinallyScheduled { get; set; }
            public PendingCompletion PendingCompletion { get; set; } = PendingCompletion.None;
        }

        private readonly record struct PendingCompletion(AbruptKind Kind, object? Value, int ResumeTarget)
        {
            public static PendingCompletion None { get; } = new(AbruptKind.None, null, -1);

            public static PendingCompletion FromNormal(int resumeTarget)
            {
                return new PendingCompletion(AbruptKind.None, null, resumeTarget);
            }

            public static PendingCompletion FromAbrupt(AbruptKind kind, object? value)
            {
                return new PendingCompletion(kind, value, -1);
            }
        }

        private sealed class YieldStarState
        {
            public DelegatedYieldState? State { get; set; }
            public bool AwaitingResume { get; set; }
            public AbruptKind PendingAbrupt { get; set; }
            public object? PendingValue { get; set; }
        }
    }

    private sealed class AsyncGeneratorInstance(
        FunctionExpression function,
        JsEnvironment closure,
        IReadOnlyList<object?> arguments,
        object? thisValue,
        IJsCallable callable,
        RealmState realmState)
    {
        private readonly IReadOnlyList<object?> _arguments = arguments;
        private readonly IJsCallable _callable = callable;
        private readonly FunctionExpression _function = function;

        private readonly TypedGeneratorInstance _inner = new(function, closure, arguments, thisValue, callable,
            realmState);

        private readonly RealmState _realmState = realmState;
        private readonly object? _thisValue = thisValue;

        // WAITING ON FULL ASYNC GENERATOR IR SUPPORT:
        // For now we reuse the sync generator IR plan and runtime to execute
        // the body. Async semantics are modeled by driving the shared plan
        // through a small step API and wrapping each step in a Promise. Once
        // we have a dedicated async-generator IR executor, this wiring
        // should be revisited so await/yield drive a single non-blocking
        // state machine.

        public JsObject CreateAsyncIteratorObject()
        {
            var asyncIterator = CreateGeneratorIteratorObject(
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Next,
                    args.Count > 0 ? args[0] : JsSymbols.Undefined),
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Return,
                    args.Count > 0 ? args[0] : null),
                args => CreateStepPromise(TypedGeneratorInstance.ResumeMode.Throw,
                    args.Count > 0 ? args[0] : null));

            // asyncIterator[Symbol.asyncIterator] returns itself.
            var asyncSymbol = TypedAstSymbol.For("Symbol.asyncIterator");
            var asyncKey = $"@@symbol:{asyncSymbol.GetHashCode()}";
            asyncIterator.SetProperty(asyncKey, new HostFunction((thisValue, _) => thisValue));

            return asyncIterator;
        }

        private object? CreateStepPromise(TypedGeneratorInstance.ResumeMode mode, object? argument)
        {
            // Look up the global Promise constructor from the closure environment.
            if (!closure.TryGet(Symbol.Intern("Promise"), out var promiseCtorObj) ||
                promiseCtorObj is not IJsCallable promiseCtor)
            {
                throw new InvalidOperationException("Promise constructor is not available in the current environment.");
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (execArgs.Count < 2 ||
                    execArgs[0] is not IJsCallable resolve ||
                    execArgs[1] is not IJsCallable reject)
                {
                    return null;
                }

                // Drive the underlying generator plan by a single step and
                // resolve/reject the Promise based on the step outcome.
                var step = _inner.ExecuteAsyncStep(mode, argument);
                switch (step.Kind)
                {
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                    {
                        var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                        resolve.Invoke([iteratorResult], null);
                        break;
                    }
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                        reject.Invoke([step.Value], null);
                        break;
                    case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                        HandlePendingStep(step, resolve, reject);
                        break;
                }

                return null;
            });

            var promiseObj = promiseCtor.Invoke([executor], null);
            return promiseObj;
        }

        private static JsObject CreateAsyncIteratorResult(object? value, bool done)
        {
            var result = new JsObject();
            result.SetProperty("value", value);
            result.SetProperty("done", done);
            return result;
        }

        private void ResolveFromStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            switch (step.Kind)
            {
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Yield:
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Completed:
                {
                    var iteratorResult = CreateAsyncIteratorResult(step.Value, step.Done);
                    resolve.Invoke([iteratorResult], null);
                    break;
                }
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Throw:
                    reject.Invoke([step.Value], null);
                    break;
                case TypedGeneratorInstance.AsyncGeneratorStepKind.Pending:
                    HandlePendingStep(step, resolve, reject);
                    break;
            }
        }

        private void HandlePendingStep(
            TypedGeneratorInstance.AsyncGeneratorStepResult step,
            IJsCallable resolve,
            IJsCallable reject)
        {
            if (step.PendingPromise is not JsObject pendingPromise ||
                !pendingPromise.TryGetProperty("then", out var thenValue) ||
                thenValue is not IJsCallable thenCallable)
            {
                reject.Invoke(["Awaited value is not a promise"], null);
                return;
            }

            var onFulfilled = new HostFunction(args =>
            {
                var value = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Next, value);
                ResolveFromStep(resumed, resolve, reject);
                return null;
            });

            var onRejected = new HostFunction(args =>
            {
                var reason = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                var resumed = _inner.ExecuteAsyncStep(TypedGeneratorInstance.ResumeMode.Throw, reason);
                ResolveFromStep(resumed, resolve, reject);
                return null;
            });

            thenCallable.Invoke([onFulfilled, onRejected], pendingPromise);
        }
    }

    private sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor, IJsObjectLike
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private IJsObjectLike? _homeObject;
        private ImmutableArray<ClassField> _instanceFields = ImmutableArray<ClassField>.Empty;
        private bool _isClassConstructor;
        private bool _isDerivedClassConstructor;
        private IJsEnvironmentAwareCallable? _superConstructor;
        private IJsPropertyAccessor? _superPrototype;

        public TypedFunction(FunctionExpression function, JsEnvironment closure, RealmState realmState)
        {
            if (function.IsAsync || function.IsGenerator)
            {
                throw new NotSupportedException(
                    "Async and generator functions are not supported by the typed evaluator yet.");
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            var paramCount = function.Parameters.Length;
            if (_realmState.FunctionPrototype is not null)
            {
                _properties.SetPrototype(_realmState.FunctionPrototype);
            }

            // Functions expose a prototype object so instances created via `new` can inherit from it.
            var functionPrototype = new JsObject();
            functionPrototype.SetPrototype(_realmState.ObjectPrototype);
            functionPrototype.DefineProperty("constructor",
                new PropertyDescriptor { Value = this, Writable = true, Enumerable = false, Configurable = true });
            _properties.SetProperty("prototype", functionPrototype);
            _properties.DefineProperty("length",
                new PropertyDescriptor
                {
                    Value = (double)paramCount, Writable = false, Enumerable = false, Configurable = false
                });
        }

        public JsEnvironment? CallingJsEnvironment { get; set; }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var context = new EvaluationContext(_realmState);
            var description = _function.Name is { } name ? $"function {name.Name}" : "anonymous function";
            var environment = new JsEnvironment(_closure, true, _function.Body.IsStrict, _function.Source, description);

            // Bind `this`.
            var boundThis = thisValue;
            if (!_function.Body.IsStrict && (thisValue is null || ReferenceEquals(thisValue, JsSymbols.Undefined)))
            {
                boundThis = CallingJsEnvironment?.Get(JsSymbols.This) ?? JsSymbols.Undefined;
            }

            if (_isDerivedClassConstructor && _superConstructor is not null)
            {
                context.MarkThisUninitialized();
            }
            else
            {
                context.MarkThisInitialized();
            }

            environment.Define(JsSymbols.This, boundThis ?? new JsObject());

            var prototypeForSuper = _superPrototype;
            if (prototypeForSuper is null && _homeObject is not null)
            {
                prototypeForSuper = _homeObject.Prototype;
            }

            if (prototypeForSuper is null && thisValue is JsObject thisObj)
            {
                prototypeForSuper = thisObj.Prototype;
            }

            if (_superConstructor is not null || prototypeForSuper is not null)
            {
                var binding = new SuperBinding(_superConstructor, prototypeForSuper, boundThis);
                environment.Define(JsSymbols.Super, binding);
            }

            // Define `arguments` so non-arrow function bodies can observe the
            // arguments they were called with. Arrow functions are also
            // represented as FunctionExpression for now, so this behaves like a
            // per-call arguments array rather than a lexical binding.
            environment.Define(JsSymbols.Arguments, CreateArgumentsArray(arguments));

            // Named function expressions should see their name inside the body.
            if (_function.Name is { } functionName)
            {
                environment.Define(functionName, this);
            }

            HoistVarDeclarations(_function.Body, environment, context);

            BindFunctionParameters(_function, arguments, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsSymbols.Undefined;
            }

            var result = EvaluateBlock(_function.Body, environment, context);

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                throw new ThrowSignal(thrown);
            }

            if (!context.IsReturn)
            {
                return JsSymbols.Undefined;
            }

            var value = context.FlowValue;
            context.ClearReturn();
            return value;
        }

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;

        public IEnumerable<string> Keys => _properties.Keys;

        public void DefineProperty(string name, PropertyDescriptor descriptor)
        {
            _properties.DefineProperty(name, descriptor);
        }

        public void SetPrototype(object? candidate)
        {
            _properties.SetPrototype(candidate);
        }

        public void Seal()
        {
            _properties.Seal();
        }

        public bool TryGetProperty(string name, out object? value)
        {
            if (_properties.TryGetProperty(name, out value))
            {
                return true;
            }

            // Provide minimal Function.prototype-style helpers for typed
            // functions so patterns like fn.call/apply/bind work for code
            // emitted by tools like Babel/regenerator.
            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                        var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                        var argList = new List<object?>();
                        if (args.Count > 1 && args[1] is JsArray jsArray)
                        {
                            foreach (var item in jsArray.Items)
                            {
                                argList.Add(item);
                            }
                        }

                        return callable.Invoke(argList.ToArray(), thisArg);
                    });
                    return true;

                case "bind":
                    value = new HostFunction((_, args) =>
                    {
                        var boundThis = args.Count > 0 ? args[0] : JsSymbols.Undefined;
                        var boundArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];

                        return new HostFunction((_, innerArgs) =>
                        {
                            var finalArgs = new object?[boundArgs.Length + innerArgs.Count];
                            boundArgs.CopyTo(finalArgs, 0);
                            for (var i = 0; i < innerArgs.Count; i++)
                            {
                                finalArgs[boundArgs.Length + i] = innerArgs[i];
                            }

                            return callable.Invoke(finalArgs, boundThis);
                        });
                    });
                    return true;
            }

            value = null;
            return false;
        }

        public void SetProperty(string name, object? value)
        {
            _properties.SetProperty(name, value);
        }

        PropertyDescriptor? IJsPropertyAccessor.GetOwnPropertyDescriptor(string name)
        {
            return _properties.GetOwnPropertyDescriptor(name);
        }

        IEnumerable<string> IJsPropertyAccessor.GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
        {
            return _properties.GetOwnPropertyDescriptor(name);
        }

        public IEnumerable<string> GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public void SetSuperBinding(IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype)
        {
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
        }

        public void SetIsClassConstructor(bool isDerived)
        {
            _isClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
        }

        public void SetInstanceFields(ImmutableArray<ClassField> fields)
        {
            _instanceFields = fields;
        }

        public void InitializeInstance(JsObject instance, JsEnvironment environment, EvaluationContext context)
        {
            if (_superConstructor is TypedFunction typedFunction)
            {
                typedFunction.InitializeInstance(instance, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (_instanceFields.IsDefaultOrEmpty || _instanceFields.Length == 0)
            {
                return;
            }

            foreach (var field in _instanceFields)
            {
                object? value = JsSymbols.Undefined;
                if (field.Initializer is not null)
                {
                    var initEnv = new JsEnvironment(environment);
                    initEnv.Define(JsSymbols.This, instance);
                    value = EvaluateExpression(field.Initializer, initEnv, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                instance.SetProperty(field.Name, value);
            }
        }
    }
}
