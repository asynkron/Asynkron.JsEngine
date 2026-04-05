#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private const string DisableForOfLoopEnvPoolVar = "JSENGINE_DISABLE_FOROF_LOOP_ENV_POOL";

    /// <summary>
    /// Applies a binary operator to two JsValue operands.
    /// For logical operators (&&, ||, ??), short-circuit evaluation must be done by the caller;
    /// this method just returns the right operand for those cases.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue ApplyBinaryOperator(BinaryOperator op, JsValue left, JsValue right,
        EvaluationContext context)
    {
        return op switch
        {
            BinaryOperator.Add => AddValue(left, right, context),
            BinaryOperator.Subtract => SubtractValue(left, right, context),
            BinaryOperator.Multiply => MultiplyValue(left, right, context),
            BinaryOperator.Divide => DivideValue(left, right, context),
            BinaryOperator.Modulo => ModuloValue(left, right, context),
            BinaryOperator.Power => PowerValue(left, right, context),
            BinaryOperator.LessThan => LessThanValue(left, right, context),
            BinaryOperator.LessThanOrEqual => LessThanOrEqualValue(left, right, context),
            BinaryOperator.GreaterThan => GreaterThanValue(left, right, context),
            BinaryOperator.GreaterThanOrEqual => GreaterThanOrEqualValue(left, right, context),
            BinaryOperator.Equal => LooseEqualsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.NotEqual => LooseEqualsValue(left, right, context) ? JsValue.False : JsValue.True,
            BinaryOperator.StrictEqual => StrictEqualsValue(left, right) ? JsValue.True : JsValue.False,
            BinaryOperator.StrictNotEqual => StrictEqualsValue(left, right) ? JsValue.False : JsValue.True,
            BinaryOperator.BitwiseAnd => BitwiseAndValue(left, right, context),
            BinaryOperator.BitwiseOr => BitwiseOrValue(left, right, context),
            BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
            BinaryOperator.LeftShift => LeftShiftValue(left, right, context),
            BinaryOperator.RightShift => RightShiftValue(left, right, context),
            BinaryOperator.UnsignedRightShift => UnsignedRightShiftValue(left, right, context),
            BinaryOperator.In => InOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.InstanceOf => InstanceofOperatorJsValue(left, right, context) ? JsValue.True : JsValue.False,
            BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.NullishCoalescing => right,
            _ => throw new NotSupportedException($"Operator '{op}' is not supported.")
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBreakJsValue(this BreakStatement statement, EvaluationContext context)
    {
        context.SetBreak(statement.Label);
        return JsValue.Unit;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateContinueJsValue(this ContinueStatement statement, EvaluationContext context)
    {
        context.SetContinue(statement.Label);
        return JsValue.Unit;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateClassJsValue(this ClassDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        var constructorValue = declaration.Definition.CreateClassValue(environment, context, declaration.Name);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Unit;
        }

        environment.DefineJsValue(declaration.Name, constructorValue, isConst: false, isLexicalBinding: true,
            blocksFunctionScopeOverride: true, isImmutableBinding: false);
        return JsValue.Unit;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateWhileJsValue(this WhileStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        return plan.EvaluateLoopPlanJsValue(environment, context, loopLabel);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDoWhileJsValue(this DoWhileStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        return plan.EvaluateLoopPlanJsValue(environment, context, loopLabel);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIfJsValue(this IfStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var test = EvaluateCachedExpressionProgram(
            statement.Condition,
            environment,
            context,
            "Dynamic if condition");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var branch = test.IsTruthy ? statement.Then : statement.Else;
        if (branch is null)
        {
            return JsValue.Undefined;
        }

        JsValue result;
        if (branch is BlockStatement block)
        {
            result = block.EvaluateBlockJsValue(environment, context);
        }
        else if (branch is FunctionDeclaration funcDecl)
        {
            var blockEnv = JsEnvironment.CreateInstance(
                environment,
                false,
                false,
                description: "if-body annex-b block");
            result = EvaluateFunctionDeclarationJsValue(funcDecl, blockEnv, context);
        }
        else
        {
            result = branch.EvaluateStatementJsValue(environment, context);
        }

        return result.IsUnit ? JsValue.Undefined : result;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateVariableDeclarationJsValue(this VariableDeclaration declaration,
        JsEnvironment environment,
        EvaluationContext context)
    {
        foreach (var declarator in declaration.Declarators)
        {
            declaration.Kind.EvaluateVariableDeclarator(declarator, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return JsValue.Unit;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateForJsValue(this ForStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<LoopPlan>)statement).GetOrCreateCache();
        var loopEnvironment =
            JsEnvironment.CreateInstance(environment, creatingSource: statement.Source, description: "for-loop");
        return plan.EvaluateLoopPlanJsValue(loopEnvironment, context, loopLabel);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateForEachJsValue(this ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            return statement.EvaluateForAwaitOfJsValue(environment, context, loopLabel);
        }

        var logger = context.RealmState.Logger;
        var cachedPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

        var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        using var pooledTdzEnv = hasLexicalDeclaration
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
            : default;
        var iterableEnvironment = hasLexicalDeclaration ? pooledTdzEnv.Value! : environment;

        if (hasLexicalDeclaration)
        {
            InitializeIterationEnvironmentLayout(cachedPlan, iterableEnvironment);

            var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing;
            statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
        }

        var previousAllowIdentifierCache = context.AllowIdentifierCache;
        if (hasLexicalDeclaration)
        {
            context.AllowIdentifierCache = false;
        }

        JsValue iterableJsValue;
        try
        {
            iterableJsValue = EvaluateCachedExpressionProgram(
                statement.Iterable,
                iterableEnvironment,
                context,
                "Dynamic foreach iterable");
        }
        finally
        {
            context.AllowIdentifierCache = previousAllowIdentifierCache;
        }

        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (statement.Kind == ForEachKind.Of)
        {
            if (iterableJsValue.IsNull || iterableJsValue.IsUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Cannot iterate over null or undefined", context,
                    context.RealmState);
            }
        }

        if (statement.Kind == ForEachKind.In)
        {
            var kind = iterableJsValue.Kind;
            if (kind != JsValueKind.Object &&
                kind != JsValueKind.String &&
                kind != JsValueKind.Null &&
                kind != JsValueKind.Undefined)
            {
                throw new ThrowSignal("Cannot iterate properties of non-object value.");
            }
        }

        var useTypedArrayLoopEnv = statement.Kind == ForEachKind.Of && iterableJsValue.TryGetObject<TypedArrayBase>(out _);
        var useLoopPool = hasLexicalDeclaration && !useTypedArrayLoopEnv && !IsEnvEnabled(DisableForOfLoopEnvPoolVar);
        using var pooledLoopEnv = useLoopPool
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-loop", logger: logger)
            : default;
        var loopEnvironment = hasLexicalDeclaration switch
        {
            true => useLoopPool switch
            {
                true => pooledLoopEnv,
                _ => useTypedArrayLoopEnv
                    ? environment
                    : JsEnvironment.CreateInstance(environment, false, false, statement.Source, "for-each-loop")
            },
            _ => environment
        };

        if (statement.Kind == ForEachKind.Of)
        {
            return ExecuteIteratorWithFastPath(statement, iterableJsValue, loopEnvironment, environment, context, loopLabel);
        }

        var values = statement.Kind switch
        {
            ForEachKind.In => EnumeratePropertyKeys(iterableJsValue),
            ForEachKind.Of or ForEachKind.AwaitOf => throw new ArgumentOutOfRangeException(),
            _ => throw new ArgumentOutOfRangeException()
        };

        var useIterationSlotsForIn = cachedPlan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                     statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                         or VariableKind.Using
                                         or VariableKind.AwaitUsing;

        var lastValueJs = JsValue.Undefined;
        foreach (var value in values)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            JsEnvironment iterationEnvironment;
            if (statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                or VariableKind.Using or VariableKind.AwaitUsing)
            {
                iterationEnvironment = JsEnvironment.CreateInstance(loopEnvironment, creatingSource: statement.Source,
                    description: "for-each-iteration");
                if (useIterationSlotsForIn)
                {
                    InitializeIterationEnvironmentLayout(cachedPlan, iterationEnvironment);
                }
            }
            else
            {
                iterationEnvironment = loopEnvironment;
            }

            statement.Target.AssignLoopBinding(value, iterationEnvironment, environment, context,
                statement.DeclarationKind);

            if (context.ShouldStopEvaluation)
            {
                break;
            }

            cachedPlan.SyncIterationSlots(iterationEnvironment, context);

            var bodyResult = statement.Body.EvaluateStatementJsValue(iterationEnvironment, context);
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

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateForAwaitOfJsValue(this ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var logger = context.RealmState.Logger;
        var hasLexicalDeclaration = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
            or VariableKind.Using or VariableKind.AwaitUsing;

        using var pooledTdzEnv = hasLexicalDeclaration
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-each-head-tdz", logger: logger)
            : default;
        var iterableEnvironment = hasLexicalDeclaration ? pooledTdzEnv.Value! : environment;

        if (hasLexicalDeclaration)
        {
            var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
            InitializeIterationEnvironmentLayout(plan, iterableEnvironment);

            var isConstDeclaration = statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing;
            statement.Target.CreateUninitializedLexicalBindings(iterableEnvironment, isConstDeclaration);
        }

        var iterableJs = EvaluateCachedExpressionProgram(
            statement.Iterable,
            iterableEnvironment,
            context,
            "Dynamic for-await-of iterable");

        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (iterableJs.IsNull || iterableJs.IsUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot iterate over null or undefined", context,
                context.RealmState);
        }

        var useLoopPool = hasLexicalDeclaration && !IsEnvEnabled(DisableForOfLoopEnvPoolVar);
        using var pooledLoopEnv = useLoopPool
            ? JsEnvironmentPool.RentScoped(environment, false, false, statement.Source, "for-await-of loop", logger: logger)
            : default;
        var loopEnvironment = hasLexicalDeclaration
            ? useLoopPool
                ? pooledLoopEnv.Value!
                : JsEnvironment.CreateInstance(environment, false, false, statement.Source, "for-await-of loop")
            : environment;

        return ExecuteIteratorWithFastPath(statement, iterableJs, loopEnvironment, environment, context, loopLabel);
    }

    private static JsValue ExecuteIteratorWithFastPath(
        ForEachStatement statement,
        JsValue iterableValue,
        JsEnvironment loopEnvironment,
        JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var plan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();
        var useIterationSlots = plan is { IterationSlotCount: >= 0, IterationScopeId: >= 0 } &&
                                statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                                    or VariableKind.Using
                                    or VariableKind.AwaitUsing;
        if (iterableValue.TryGetObject<TypedArrayBase>(out _))
        {
            useIterationSlots = false;
        }

        var fastEnumerator = TryGetFastEnumeratorForIteration(iterableValue);
        if (fastEnumerator is not null)
        {
            try
            {
                return plan.ExecuteIteratorDriverJsValue(null,
                    fastEnumerator,
                    loopEnvironment,
                    environment,
                    context,
                    loopLabel,
                    useIterationSlots);
            }
            finally
            {
                fastEnumerator.Dispose();
            }
        }

        return ExecuteIteratorSlowPath(iterableValue, plan, loopEnvironment, environment, context, loopLabel, useIterationSlots);
    }

    private static JsValue ExecuteIteratorSlowPath(
        JsValue iterableValue,
        IteratorDriverPlan plan,
        JsEnvironment loopEnvironment,
        JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel,
        bool useIterationSlots)
    {
        var iteratorTarget = NormalizeIterableTarget(iterableValue, context);
        if (TryGetIteratorFromProtocols(iteratorTarget, context, out var iterator) && iterator is not null)
        {
            return plan.ExecuteIteratorDriverJsValue(iterator,
                null,
                loopEnvironment,
                environment,
                context,
                loopLabel,
                useIterationSlots);
        }

        throw StandardLibrary.ThrowTypeError("Value is not iterable", context, context.RealmState);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateLabeledJsValue(this LabeledStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = statement.Statement.EvaluateStatementJsValue(environment, context, statement.Label);
            if (context.TryClearBreak(statement.Label) && result.IsUnit)
            {
                return JsValue.Undefined;
            }

            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateReturnJsValue(this ReturnStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        var jsValue = statement.Expression is null
            ? JsValue.Undefined
            : statement.Expression.EvaluateDynamicExpressionOperand(
                environment,
                context,
                "Dynamic return expression");

        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        context.SetReturn(jsValue);
        return jsValue;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateThrowJsValue(this ThrowStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        var jsValue = EvaluateCachedExpressionProgram(
            statement.Expression,
            environment,
            context,
            "Dynamic throw expression");
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        context.SetThrow(jsValue);
        return jsValue;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTryJsValue(this TryStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        context.RealmState.Logger?.LogInformation(
            "EvaluateTry enter (catch={HasCatch}, finally={HasFinally}) throwFlag={ThrowFlag}",
            statement.Catch is not null,
            statement.Finally is not null,
            context.IsThrow);

        JsValue result;
        try
        {
            result = statement.TryBlock.EvaluateBlockJsValue(environment, context);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            result = signal.ThrownValue;
        }

        if (context.IsThrow && statement.Catch is not null)
        {
            context.RealmState.Logger?.LogInformation(
                "EvaluateTry handling catch; throw type={ThrowType}",
                !context.FlowValue.IsUndefined ? context.FlowValue.GetType().Name : "undefined");
            var thrownValue = context.FlowValue;
            context.Clear();
            var catchEnv = JsEnvironment.CreateInstance(
                environment,
                creatingSource: statement.Catch.Body.Source,
                description: "catch");

            if (statement.Catch.Binding is not null)
            {
                if (statement.Catch.Binding is IdentifierBinding identifierBinding)
                {
                    catchEnv.SetSimpleCatchParameters(
                        new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { identifierBinding.Name });
                    catchEnv.DefineJsValue(
                        identifierBinding.Name,
                        thrownValue,
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                }
                else
                {
                    statement.Catch.Binding.DefineBindingTarget(thrownValue, catchEnv, context, false);
                }
            }

            result = statement.Catch.Body.EvaluateBlockJsValue(catchEnv, context);
        }

        if (statement.Finally is null)
        {
            context.RealmState.Logger?.LogInformation(
                "EvaluateTry exit (no finally) throwFlag={ThrowFlag}",
                context.IsThrow);
            return result.IsUnit ? JsValue.Undefined : result;
        }

        var savedState = context.SaveCompletionState();
        GeneratorPendingCompletion? pending = null;
        var isGenerator = environment.IsGeneratorContext();
        if (isGenerator && savedState.HasCompletion)
        {
            pending = environment.GetGeneratorPendingCompletion();
            if (savedState.IsReturn)
            {
                pending.HasValue = true;
                pending.IsThrow = false;
                pending.IsReturn = true;
                pending.Value = savedState.ReturnValue;
            }
            else if (savedState.Signal is ThrowFlowCompletionSignal throwSignal)
            {
                pending.HasValue = true;
                pending.IsThrow = true;
                pending.IsReturn = false;
                pending.Value = throwSignal.JsValue;
            }
        }

        context.Clear();
        var finallyResult = statement.Finally.EvaluateBlockJsValue(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return finallyResult.IsUnit ? JsValue.Undefined : finallyResult;
        }

        if (isGenerator && pending?.HasValue == true)
        {
            var pendingValueJs = pending.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(pending.Value);

            if (pending.IsThrow)
            {
                context.SetThrow(pendingValueJs);
            }
            else if (pending.IsReturn)
            {
                context.SetReturn(pendingValueJs);
            }

            pending.HasValue = false;
            pending.IsThrow = false;
            pending.IsReturn = false;
            pending.Value = null;
        }
        else
        {
            context.RestoreCompletionState(savedState);
        }

        context.RealmState.Logger?.LogInformation(
            "EvaluateTry exit (with finally) throwFlag={ThrowFlag}",
            context.IsThrow);
        return result.IsUnit ? JsValue.Undefined : result;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateSwitchJsValue(this SwitchStatement statement,
        JsEnvironment environment,
        EvaluationContext context,
        Symbol? targetLabel)
    {
        var discriminantJs = EvaluateCachedExpressionProgram(
            statement.Discriminant,
            environment,
            context,
            "Dynamic switch discriminant");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var instantiationPlan = ((IAstCacheable<SwitchInstantiationPlan>)statement).GetOrCreateCache();
        var switchEnv = JsEnvironment.CreateInstance(environment, false, instantiationPlan.IsStrict);
        var scopeMode = instantiationPlan.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
        using var scopeHandle = context.PushScope(ScopeKind.Block, scopeMode);

        statement.InstantiateSwitchLexicalDeclarations(instantiationPlan, switchEnv, context);

        var completionValue = JsValue.Undefined;
        int? matchedCaseIndex = null;
        int? defaultCaseIndex = null;

        for (var i = 0; i < statement.Cases.Length; i++)
        {
            var switchCase = statement.Cases[i];

            if (switchCase.Test is null)
            {
                defaultCaseIndex = i;
                continue;
            }

            var testJs = EvaluateCachedExpressionProgram(
                switchCase.Test,
                switchEnv,
                context,
                "Dynamic switch case test");
            if (context.ShouldStopEvaluation)
            {
                return completionValue;
            }

            if (StrictEqualsValue(discriminantJs, testJs))
            {
                matchedCaseIndex = i;
                break;
            }
        }

        var startIndex = matchedCaseIndex ?? defaultCaseIndex;
        if (startIndex is null)
        {
            return JsValue.Undefined;
        }

        for (var i = startIndex.Value; i < statement.Cases.Length; i++)
        {
            var switchCase = statement.Cases[i];
            var (caseCompletionJs, hasCaseJs) =
                statement.EvaluateCaseClauseBodyJsValue(switchCase.Body, switchEnv, context);

            if (hasCaseJs)
            {
                completionValue = caseCompletionJs;
            }

            if (context.TryClearBreak(targetLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow || context.IsContinue)
            {
                break;
            }
        }

        return completionValue;
    }

    private static void InstantiateSwitchLexicalDeclarations(this SwitchStatement _,
        SwitchInstantiationPlan plan,
        JsEnvironment switchEnv,
        EvaluationContext context)
    {
        foreach (var binding in plan.LexicalBindings)
        {
            binding.Target.CreateUninitializedLexicalBindings(switchEnv, binding.IsConst);
        }

        foreach (var funcBinding in plan.FunctionBindings)
        {
            if (!funcBinding.InitializeNow)
            {
                switchEnv.DefineJsValue(
                    funcBinding.Name,
                    JsValue.Uninitialized,
                    true,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                continue;
            }

            var functionDescriptor = FunctionDeclarationDescriptor.Create(funcBinding.Name, funcBinding.Function);
            var functionValue = funcBinding.Function.CreateFunctionValue(
                switchEnv,
                context,
                skipInternalNameBinding: true,
                planSeed: functionDescriptor.PlanSeed);
            switchEnv.DefineJsValue(
                funcBinding.Name,
                JsValue.FromObjectUnsafe(functionValue),
                true,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }

        foreach (var className in plan.ClassBindings)
        {
            switchEnv.DefineJsValue(
                className,
                JsValue.Undefined,
                true,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: false);
        }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static (JsValue result, bool hasResult) EvaluateCaseClauseBodyJsValue(this SwitchStatement _,
        BlockStatement body,
        JsEnvironment switchEnv,
        EvaluationContext context)
    {
        var hasResult = false;
        var result = JsValue.Undefined;

        foreach (var stmt in body.Statements)
        {
            context.ThrowIfCancellationRequested();

            var completion = stmt.EvaluateStatementJsValue(switchEnv, context);
            var shouldStop = context.ShouldStopEvaluation;
            var shouldCapture =
                !completion.IsUnit &&
                (!shouldStop ||
                 context.IsReturn ||
                 context.IsThrow ||
                 context.IsYield ||
                 context.IsBreak ||
                 context.IsContinue);

            if (shouldCapture)
            {
                result = completion;
                hasResult = true;
            }

            if (shouldStop)
            {
                break;
            }
        }

        return (result, hasResult);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateFunctionDeclarationJsValue(
        FunctionDeclaration funcDecl,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var varEnvironment = environment.GetVarEnvironment();
        var isAtVarEnvironment = ReferenceEquals(varEnvironment, environment) ||
                                 environment.IsEvalDeclarationEnvironment;

        var isAnnexBHoistedFunction = false;
        if (isAtVarEnvironment && !context.CurrentScope.IsStrict)
        {
            if (varEnvironment.HasFunctionScopedBinding(funcDecl.Name))
            {
                var existingValue = varEnvironment.GetBindingValueDirect(funcDecl.Name);
                if (existingValue.IsUndefined)
                {
                    isAnnexBHoistedFunction = true;
                }
            }
        }

        if (isAtVarEnvironment && !isAnnexBHoistedFunction)
        {
            return JsValue.Unit;
        }

        var functionDescriptor = FunctionDeclarationDescriptor.Create(funcDecl);
        var functionValue = funcDecl.Function.CreateFunctionValue(
            environment,
            context,
            skipInternalNameBinding: true,
            planSeed: functionDescriptor.PlanSeed);
        var fnValueJs = JsValue.FromObjectUnsafe(functionValue);

        if (isAnnexBHoistedFunction)
        {
            if (environment.IsAnnexBBlocked(funcDecl.Name))
            {
                return JsValue.Unit;
            }

            varEnvironment.AssignJsValue(funcDecl.Name, fnValueJs);

            if (varEnvironment.IsGlobalFunctionScope)
            {
                var globalThis = varEnvironment.GetRootGlobalObject();
                globalThis?.SetProperty(funcDecl.Name.Name, fnValueJs);
            }
        }
        else
        {
            var isBlocked = !context.CurrentScope.IsStrict &&
                            (environment.IsAnnexBBlocked(funcDecl.Name) ||
                             HasEnclosingLexicalBinding(environment.Enclosing, funcDecl.Name));

            if (!isBlocked || !environment.IsBodyEnvironment)
            {
                environment.DefineJsValue(
                    funcDecl.Name,
                    fnValueJs,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            if (!isBlocked && varEnvironment.HasFunctionScopedBinding(funcDecl.Name))
            {
                varEnvironment.AssignJsValue(funcDecl.Name, fnValueJs);

                if (varEnvironment.IsGlobalFunctionScope)
                {
                    var globalThis = varEnvironment.GetRootGlobalObject();
                    globalThis?.SetProperty(funcDecl.Name.Name, fnValueJs);
                }
            }
        }

        return JsValue.Unit;

        static bool HasEnclosingLexicalBinding(JsEnvironment? start, Symbol name)
        {
            var current = start;
            while (current is not null)
            {
                if (current.IsFunctionScope)
                {
                    break;
                }

                if (current.IsSimpleCatchParameter(name))
                {
                    current = current.Enclosing;
                    continue;
                }

                ref var slot = ref current.TryGetSlotRef(name);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref slot) &&
                    slot.IsLexical &&
                    slot.BlocksFunctionScopeOverride)
                {
                    return true;
                }

                current = current.Enclosing;
            }

            return false;
        }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateWithJsValue(this WithStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var objValueJs = EvaluateCachedExpressionProgram(
            statement.Object,
            environment,
            context,
            "Dynamic with object");
        if (context.ShouldStopEvaluation)
        {
            return objValueJs;
        }

        if (!TryConvertToWithBindingObject(objValueJs, context, out var withObject))
        {
            return JsValue.Undefined;
        }

        var withEnv = JsEnvironment.CreateInstance(
            environment,
            false,
            context.CurrentScope.IsStrict,
            statement.Source,
            "with",
            withObject);
        var previousAllowIdentifierCache = context.AllowIdentifierCache;
        context.AllowIdentifierCache = false;

        JsValue completion;
        try
        {
            completion = statement.Body.EvaluateStatementJsValue(withEnv, context);
        }
        finally
        {
            context.AllowIdentifierCache = previousAllowIdentifierCache;
        }

        return completion.IsUnit ? JsValue.Undefined : completion;
    }

    /// <summary>
    /// Evaluates a statement and returns the completion value as JsValue.
    /// Tiny hot path for inlining - only handles the most common cases.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateStatementJsValue(this StatementNode statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? activeLabel = null)
    {
        context.RecordAstEvaluation(statement);
        context.SourceReference = statement.Source;
        context.ThrowIfCancellationRequested();

        // Hot path - explicit type checks for best inlining
        if (statement is ExpressionStatement expressionStatement)
        {
            var result = EvaluateCachedExpressionProgram(
                expressionStatement.Expression,
                environment,
                context,
                "Dynamic expression statement");
            // SuppressCompletionValue is used for synthetic assignments (e.g., switch lowering's __done flag)
            // Return Unit to indicate no change to completion value
            return expressionStatement.SuppressCompletionValue ? JsValue.Unit : result;
        }

        if (statement is BlockStatement block)
        {
            return block.EvaluateBlockJsValue(environment, context);
        }

        if (statement is IfStatement ifStatement)
        {
            return ifStatement.EvaluateIfJsValue(environment, context);
        }

        if (statement is ReturnStatement returnStatement)
        {
            return returnStatement.EvaluateReturnJsValue(environment, context);
        }

        if (statement is ForStatement forStatement)
        {
            return forStatement.EvaluateForJsValue(environment, context, activeLabel);
        }

        if (statement is EmptyStatement)
        {
            return JsValue.Unit;
        }

        return statement.EvaluateStatementJsValueSlow(environment, context, activeLabel);
    }

    /// <summary>
    /// Slow path for less common statement types - marked NoInlining to keep hot path small.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateStatementJsValueSlow(this StatementNode statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? activeLabel)
    {
        // Medium frequency statements
        switch (statement)
        {
            case WhileStatement whileStatement:
                return whileStatement.EvaluateWhileJsValue(environment, context, activeLabel);
            case DoWhileStatement doWhileStatement:
                return doWhileStatement.EvaluateDoWhileJsValue(environment, context, activeLabel);
            case SwitchStatement switchStatement:
                return switchStatement.EvaluateSwitchJsValue(environment, context, activeLabel);
            case TryStatement tryStatement:
                return tryStatement.EvaluateTryJsValue(environment, context);
            case LabeledStatement labeledStatement:
                return labeledStatement.EvaluateLabeledJsValue(environment, context);
        }

        // Low-frequency statements with activity tracking

        return statement switch
        {
            ThrowStatement throwStatement => throwStatement.EvaluateThrowJsValue(environment, context),
            VariableDeclaration declaration => declaration.EvaluateVariableDeclarationJsValue(environment, context),
            FunctionDeclaration funcDecl => EvaluateFunctionDeclarationJsValue(funcDecl, environment, context),
            ForEachStatement forEachStatement => forEachStatement.EvaluateForEachJsValue(environment, context,
                activeLabel),
            BreakStatement breakStatement => breakStatement.EvaluateBreakJsValue(context),
            ContinueStatement continueStatement => continueStatement.EvaluateContinueJsValue(context),
            ClassDeclaration classDeclaration => classDeclaration.EvaluateClassJsValue(environment, context),
            WithStatement withStatement => withStatement.EvaluateWithJsValue(environment, context),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
        };
    }

    private static void HoistFromStatement(this StatementNode statement, JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues,
        HashSet<Symbol> lexicalNames,
        HashSet<Symbol> catchParameterNames,
        HashSet<Symbol> simpleCatchParameterNames,
        HoistPass pass,
        bool inBlockScope,
        bool reverseFunctionHoist,
        HashSet<Symbol>? functionHoistDedupe)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDeclaration when pass == HoistPass.Vars:
                    foreach (var declarator in varDeclaration.Declarators)
                    {
                        declarator.Target.HoistFromBindingTarget(environment, context, lexicalNames);
                    }

                    break;
                case BlockStatement block:
                    block.HoistVarDeclarationsPass(environment,
                        context,
                        hoistFunctionValues, block.MergeLexicalNames(lexicalNames),
                        block.MergeCatchNames(catchParameterNames),
                        block.MergeSimpleCatchNames(simpleCatchParameterNames),
                        pass,
                        true,
                        reverseFunctionHoist,
                        functionHoistDedupe);
                    break;
                case IfStatement ifStatement:
                    ifStatement.Then.HoistFromStatement(environment, context, false,
                        lexicalNames, catchParameterNames, simpleCatchParameterNames, pass, true,
                        reverseFunctionHoist,
                        functionHoistDedupe);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    reverseFunctionHoist = false;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    reverseFunctionHoist = false;
                    continue;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    reverseFunctionHoist = false;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar &&
                        pass == HoistPass.Vars)
                    {
                        initVar.HoistFromStatement(environment, context, hoistFunctionValues, lexicalNames,
                            catchParameterNames, simpleCatchParameterNames, pass,
                            inBlockScope,
                            reverseFunctionHoist,
                            functionHoistDedupe);
                    }

                    statement = forStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    reverseFunctionHoist = false;
                    continue;
                case ForEachStatement forEachStatement:
                    if (pass == HoistPass.Vars && forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        forEachStatement.Target.HoistFromBindingTarget(environment, context, lexicalNames);
                    }

                    statement = forEachStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    reverseFunctionHoist = false;
                    continue;
                case ExportDeclarationStatement exportDeclaration:
                    statement = exportDeclaration.Declaration;
                    reverseFunctionHoist = false;
                    continue;
                case ExportDefaultStatement { Value: ExportDefaultDeclaration { Declaration: { } decl } }:
                    statement = decl;
                    reverseFunctionHoist = false;
                    continue;
                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    reverseFunctionHoist = false;
                    continue;
                case TryStatement tryStatement:
                    tryStatement.TryBlock.HoistVarDeclarationsPass(environment, context, hoistFunctionValues,
                        tryStatement.TryBlock.MergeLexicalNames(lexicalNames),
                        tryStatement.TryBlock.MergeCatchNames(catchParameterNames),
                        tryStatement.TryBlock.MergeSimpleCatchNames(simpleCatchParameterNames),
                        pass,
                        true,
                        reverseFunctionHoist,
                        functionHoistDedupe);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        // Track *active* catch parameter names for Annex B.3.5/B.3.3.3 checks while
                        // hoisting inside the catch body.
                        var catchParameterNamesForBody = catchParameterNames;
                        var simpleCatchParameterNamesForBody = simpleCatchParameterNames;
                        if (catchClause.Binding is { } catchBinding)
                        {
                            catchParameterNamesForBody =
                                new HashSet<Symbol>(catchParameterNames, catchParameterNames.Comparer);
                            catchBinding.CollectSymbolsFromBinding(catchParameterNamesForBody);

                            if (catchBinding is IdentifierBinding identifierBinding)
                            {
                                simpleCatchParameterNamesForBody =
                                    new HashSet<Symbol>(simpleCatchParameterNames, simpleCatchParameterNames.Comparer);
                                simpleCatchParameterNamesForBody.Add(identifierBinding.Name);
                            }
                        }

                        catchClause.Body.HoistVarDeclarationsPass(environment, context, hoistFunctionValues,
                            catchClause.Body.MergeLexicalNames(lexicalNames),
                            catchClause.Body.MergeCatchNames(catchParameterNamesForBody),
                            catchClause.Body.MergeSimpleCatchNames(simpleCatchParameterNamesForBody),
                            pass,
                            true,
                            reverseFunctionHoist,
                            functionHoistDedupe);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        finallyBlock.HoistVarDeclarationsPass(environment, context, hoistFunctionValues,
                            finallyBlock.MergeLexicalNames(lexicalNames),
                            finallyBlock.MergeCatchNames(catchParameterNames),
                            finallyBlock.MergeSimpleCatchNames(simpleCatchParameterNames),
                            pass,
                            true,
                            reverseFunctionHoist,
                            functionHoistDedupe);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        switchCase.Body.HoistVarDeclarationsPass(environment, context, false,
                            switchCase.Body.MergeLexicalNames(lexicalNames),
                            switchCase.Body.MergeCatchNames(catchParameterNames),
                            switchCase.Body.MergeSimpleCatchNames(simpleCatchParameterNames),
                            pass,
                            true,
                            reverseFunctionHoist,
                            functionHoistDedupe);
                    }

                    break;
                case FunctionDeclaration functionDeclaration:
                    {
                        if (pass != HoistPass.Functions)
                        {
                            break;
                        }

                        // In strict mode, block-level function declarations are block-scoped only
                        // (no Annex B var-style hoisting). Skip hoisting entirely - the block's
                        // FunctionDeclarationInstruction will create the binding at runtime.
                        if (inBlockScope && context.CurrentScope.IsStrict)
                        {
                            break;
                        }

                        // Per ES spec, Annex B.3.3 only applies to plain function declarations.
                        // async function, function*, and async function* in blocks are always
                        // lexically scoped — they do NOT get var-style hoisting even in sloppy mode.
                        if (inBlockScope && (functionDeclaration.Function.IsAsync ||
                                             functionDeclaration.Function.WasAsync ||
                                             functionDeclaration.Function.IsGenerator))
                        {
                            break;
                        }

                        if (functionHoistDedupe?.Add(functionDeclaration.Name) == false)
                        {
                            break;
                        }

                        // Per Annex B.3.3, block-scoped functions in non-strict mode should:
                        // 1. Create a var binding with undefined during hoisting
                        // 2. Assign the function value when the block executes
                        // Only hoist with the function value for top-level declarations or strict mode.
                        var shouldHoistFunctionValue = hoistFunctionValues &&
                                                       (!inBlockScope || context.CurrentScope.IsStrict);

                        if (shouldHoistFunctionValue)
                        {
                            // Pass skipInternalNameBinding: true so the SyncFunctionInvoker doesn't create
                            // an internal const binding for the function name. For function declarations,
                            // the name binding lives in the outer (function/global) scope and is mutable.
                            var functionDescriptor = FunctionDeclarationDescriptor.Create(functionDeclaration);
                            var functionValue = functionDeclaration.Function.CreateFunctionValue(environment, context,
                                skipInternalNameBinding: true,
                                planSeed: functionDescriptor.PlanSeed);
                            var fnValueJs = JsValue.FromObjectUnsafe(functionValue);

                            var slotIndex = -1;
                            if (environment.TryGetSlotIndex(functionDeclaration.Name, out var directSlotIndex))
                            {
                                slotIndex = directSlotIndex;
                            }
                            else if (environment._slots is not null)
                            {
                                for (var i = 0; i < environment._slotCount; i++)
                                {
                                    var slotName = environment._slots[i].Name;
                                    if (slotName is not null && slotName.Name == functionDeclaration.Name.Name)
                                    {
                                        slotIndex = i;
                                        break;
                                    }
                                }
                            }

                            if (slotIndex >= 0)
                            {
                                // Slot-backed scope (IR path): populate slot directly for fast lookup
                                environment.SetSlotDirect(slotIndex, fnValueJs);
                                if (context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false })
                                {
                                    ref var slot = ref environment.GetSlotByIndex(slotIndex);
                                    slot.Flags |= SlotFlags.CanDelete;
                                }
                            }
                            else
                            {
                                environment.DefineFunctionScoped(
                                    functionDeclaration.Name,
                                    fnValueJs,
                                    true,
                                    true,
                                    context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                                    context,
                                    canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });
                            }
                        }
                        else if (inBlockScope && !context.CurrentScope.IsStrict)
                        {
                            // Per Annex B.3.3.1/B.3.3.2/B.3.3.3: "If replacing the FunctionDeclaration f
                            // with a VariableStatement that has F as a BindingIdentifier would not produce
                            // any Early Errors for body/script, then..." -- if the function name conflicts
                            // with a lexical binding (let/const) in an outer scope, skip the var hoisting.
                            if (lexicalNames.Contains(functionDeclaration.Name))
                            {
                                break;
                            }

                            // Annex B.3.5: If the catch parameter is not a simple identifier (e.g. destructuring),
                            // any var binding with the same name in the catch body would be an early error.
                            // Per B.3.3.1/B.3.3.2/B.3.3.3, skip AnnexB var-style function hoisting in that case.
                            if (catchParameterNames.Contains(functionDeclaration.Name) &&
                                !simpleCatchParameterNames.Contains(functionDeclaration.Name))
                            {
                                break;
                            }

                            // Per Annex B.3.3.2, in non-strict mode, function declarations in if/while/etc
                            // branches should create a var binding initialized to undefined. The function
                            // value will be assigned when the FunctionDeclaration is evaluated at runtime.
                            // DefineFunctionScoped handles existing bindings correctly (returns early).
                            environment.DefineFunctionScoped(
                                functionDeclaration.Name,
                                JsValue.Undefined,
                                hasInitializer: false,
                                isFunctionDeclaration: true,
                                globalFunctionConfigurable: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                                context: context,
                                canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });
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

    internal static void CollectLexicalNamesFromStatement(this StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        inner.CollectLexicalNamesFromStatement(names);
                    }

                    break;
                case VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } letDecl:
                    foreach (var declarator in letDecl.Declarators)
                    {
                        declarator.Target.CollectSymbolsFromBinding(names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
                case FunctionDeclaration:
                    // Function declarations are handled separately for hoisting;
                    // they are not treated as lexical bindings here.
                    break;
                case IfStatement ifStatement:
                    ifStatement.Then.CollectLexicalNamesFromStatement(names);
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
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using
                            or VariableKind.AwaitUsing
                        } decl)
                    {
                        foreach (var declarator in decl.Declarators)
                        {
                            declarator.Target.CollectSymbolsFromBinding(names);
                        }
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const
                        or VariableKind.Using or VariableKind.AwaitUsing)
                    {
                        forEachStatement.Target.CollectSymbolsFromBinding(names);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        switchCase.Body.CollectLexicalNamesFromStatement(names);
                    }

                    break;
                case TryStatement tryStatement:
                    tryStatement.TryBlock.CollectLexicalNamesFromStatement(names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        // Per ES spec 13.15.7, catch parameters create their own lexical environment
                        // and should NOT be collected as lexical names of the outer (try statement's) scope.
                        catchClause.Body.CollectLexicalNamesFromStatement(names);
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

    private static void CollectCatchNamesFromStatement(this StatementNode statement, HashSet<Symbol> names, bool simpleOnly = false)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        inner.CollectCatchNamesFromStatement(names, simpleOnly);
                    }

                    break;
                case IfStatement ifStatement:
                    ifStatement.Then.CollectCatchNamesFromStatement(names, simpleOnly);
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
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case ForStatement forStatement:
                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        switchCase.Body.CollectCatchNamesFromStatement(names, simpleOnly);
                    }

                    break;
                case TryStatement tryStatement:
                    tryStatement.TryBlock.CollectCatchNamesFromStatement(names, simpleOnly);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (simpleOnly)
                        {
                            if (catchClause.Binding is IdentifierBinding identifierBinding)
                            {
                                names.Add(identifierBinding.Name);
                            }
                        }
                        else
                        {
                            catchClause.Binding?.CollectSymbolsFromBinding(names);
                        }

                        catchClause.Body.CollectCatchNamesFromStatement(names, simpleOnly);
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

    /// <summary>
    /// Evaluates a block statement and returns the completion value as JsValue.
    /// Returns JsValue.Undefined for empty blocks to match browser behavior.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBlockJsValue(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        return block.EvaluateBlockCore(environment, context);
    }

    /// <summary>
    /// Core block evaluation that returns JsValue directly.
    /// Returns JsValue.Undefined for empty blocks to match browser behavior.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBlockCore(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        if (context.AllowIdentifierCache)
        {
            context.AllowIdentifierCache = AllowsIdentifierCaching(block);
        }

        var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

        // Fast path: if the block has no lexical/function decls, execute directly in the incoming environment
        if (!hoistPlan.NeedsEnvironment)
        {
            return block.EvaluateBlockFast(environment, context);
        }

        // Slow path: needs new environment for lexical bindings
        return block.EvaluateBlockSlow(environment, context);
    }

    /// <summary>
    /// Fast path for blocks that don't need a new environment.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBlockFast(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        return EvaluateStatementList(block.Statements, environment, context);
    }

    /// <summary>
    /// Slow path for blocks that need a new environment for lexical bindings.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBlockSlow(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        var scope = JsEnvironmentPool.Rent(environment, false, block.IsStrict, logger: context.RealmState.Logger);
        try
        {
            return block.EvaluateBlockSlowCore(scope, context);
        }
        finally
        {
            JsEnvironmentPool.Return(scope, context.RealmState.Logger);
        }
    }

    /// <summary>
    /// Core slow path logic, separated to allow proper try/finally for pooling.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateBlockSlowCore(this BlockStatement block, JsEnvironment scope,
        EvaluationContext context)
    {
        // Ensure we mark lexical bindings as TDZ before executing the block body.
        // SlotAssignmentRewriter can pre-seed slots (SlotMap) which means HasBinding
        // returns true and the hoist loop below would otherwise skip defining the
        // bindings as uninitialized. Explicitly flagging the lexical slots here
        // preserves the TDZ semantics even when slots already exist.
        var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

        // Use unified Initialize method that properly sets slot names from the map
        // This ensures name-based lookups (TryLocateBinding) can find block-scoped variables
        scope.Initialize(block.ScopeId, block.SlotMap);
        if (hoistPlan.TopLevelLexicalNames.Count > 0)
        {
            scope.MarkSlotsLexicalUninitialized(hoistPlan.TopLevelLexicalNames);
        }

        var mode = scope.IsStrict || block.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
        using var scopeHandle = context.PushScope(ScopeKind.Block, mode);

        // Per ES spec, lexical declarations (let/const/class) must be hoisted to create
        // bindings in the TDZ (Temporal Dead Zone) BEFORE function hoisting.
        // This ensures closures that reference lexical variables will find TDZ bindings
        // and throw ReferenceError if accessed before initialization.
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } lexDecl:
                    var isConst =
                        lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    foreach (var declarator in lexDecl.Declarators)
                    {
                        block.HoistLexicalBindingTargetForTdz(declarator.Target, scope, isConst);
                    }

                    break;
                case ClassDeclaration classDecl:
                    if (!scope.HasBinding(classDecl.Name))
                    {
                        // Class declarations create mutable bindings (like let), not const
                        scope.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isConst: false,
                            isLexicalBinding: true, blocksFunctionScopeOverride: true);
                    }

                    break;
            }
        }

        // Block-scoped function declarations (strict mode behavior - no AnnexB hoisting)
        block.InstantiateLexicalBlockFunctions(scope, context);

        return EvaluateStatementList(block.Statements, scope, context);
    }

    private static void InstantiateLexicalBlockFunctions(this BlockStatement block, JsEnvironment blockEnvironment,
        EvaluationContext context)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is not FunctionDeclaration functionDeclaration)
            {
                continue;
            }

            // Pass skipInternalNameBinding: true so the function doesn't create an internal
            // const binding for its name (the binding is handled by blockEnvironment.Define below).
            var functionDescriptor = FunctionDeclarationDescriptor.Create(functionDeclaration);
            var functionValue = functionDeclaration.Function.CreateFunctionValue(blockEnvironment, context,
                skipInternalNameBinding: true,
                planSeed: functionDescriptor.PlanSeed);
            blockEnvironment.DefineJsValue(
                functionDeclaration.Name,
                JsValue.FromObjectUnsafe(functionValue),
                true,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }
    }

    private static void HoistLexicalBindingTargetForTdz(this BlockStatement block, BindingTarget target,
        JsEnvironment blockEnvironment, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!blockEnvironment.HasBinding(id.Name))
                    {
                        blockEnvironment.DefineJsValue(id.Name, JsValue.Uninitialized, isConst: isConst,
                            isLexicalBinding: true, blocksFunctionScopeOverride: true);
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            block.HoistLexicalBindingTargetForTdz(elementTarget, blockEnvironment, isConst);
                        }
                    }

                    if (arrayBinding.RestElement is { } restTarget)
                    {
                        target = restTarget;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        block.HoistLexicalBindingTargetForTdz(prop.Target, blockEnvironment, isConst);
                    }

                    if (objectBinding.RestElement is { } restObjTarget)
                    {
                        target = restObjTarget;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void HoistVarDeclarations(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues = true,
        HashSet<Symbol>? lexicalNames = null,
        HashSet<Symbol>? catchParameterNames = null,
        HashSet<Symbol>? simpleCatchParameterNames = null,
        bool inBlockScope = false,
        bool reverseFunctionHoist = false,
        HashSet<Symbol>? functionHoistDedupe = null)
    {
        var effectiveLexicalNames = lexicalNames is null
            ? block.CollectLexicalNames()
            : [.. lexicalNames];
        if (lexicalNames is not null)
        {
            effectiveLexicalNames.UnionWith(block.CollectLexicalNames());
        }

        // Catch parameter names are tracked as *active* names from enclosing catch clauses.
        // They are added when recursing into a catch body (see HoistFromStatement TryStatement case).
        var effectiveCatchNames = catchParameterNames ??
                                  new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        var effectiveSimpleCatchNames = simpleCatchParameterNames ??
                                        new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

        block.HoistVarDeclarationsPass(environment,
            context,
            hoistFunctionValues,
            effectiveLexicalNames,
            effectiveCatchNames,
            effectiveSimpleCatchNames,
            HoistPass.Functions,
            inBlockScope,
            reverseFunctionHoist,
            functionHoistDedupe);
        block.HoistVarDeclarationsPass(environment,
            context,
            false,
            effectiveLexicalNames,
            effectiveCatchNames,
            effectiveSimpleCatchNames,
            HoistPass.Vars,
            inBlockScope,
            reverseFunctionHoist: false,
            functionHoistDedupe: null);
    }

    private static void HoistVarDeclarationsPass(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues,
        HashSet<Symbol> lexicalNames,
        HashSet<Symbol> catchParameterNames,
        HashSet<Symbol> simpleCatchParameterNames,
        HoistPass pass,
        bool inBlockScope,
        bool reverseFunctionHoist,
        HashSet<Symbol>? functionHoistDedupe)
    {
        if (reverseFunctionHoist && pass == HoistPass.Functions)
        {
            for (var i = block.Statements.Length - 1; i >= 0; i--)
            {
                var statement = block.Statements[i];
                statement.HoistFromStatement(environment, context, hoistFunctionValues, lexicalNames,
                    catchParameterNames,
                    simpleCatchParameterNames,
                    pass,
                    inBlockScope,
                    reverseFunctionHoist,
                    functionHoistDedupe);
            }
            return;
        }

        foreach (var statement in block.Statements)
        {
            statement.HoistFromStatement(environment, context, hoistFunctionValues, lexicalNames,
                catchParameterNames,
                simpleCatchParameterNames,
                pass,
                inBlockScope,
                reverseFunctionHoist,
                functionHoistDedupe);
        }
    }

    private static HashSet<Symbol> MergeLexicalNames(this BlockStatement block, HashSet<Symbol> lexicalNames)
    {
        var merged = new HashSet<Symbol>(lexicalNames);
        merged.UnionWith(block.CollectLexicalNames());
        return merged;
    }

    private static HashSet<Symbol> MergeCatchNames(this BlockStatement block, HashSet<Symbol> catchParameterNames)
    {
        // Catch parameter names are tracked as active names from enclosing catch clauses.
        // Entering a normal block does not add new catch parameter names.
        _ = block;
        return catchParameterNames;
    }

    private static HashSet<Symbol> MergeSimpleCatchNames(this BlockStatement block,
        HashSet<Symbol> simpleCatchParameterNames)
    {
        // Simple catch parameter names are tracked as active names from enclosing catch clauses.
        // Entering a normal block does not add new catch parameter names.
        _ = block;
        return simpleCatchParameterNames;
    }

    private static HashSet<Symbol> CollectLexicalNames(this BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        block.CollectLexicalNamesFromStatement(names);
        return names;
    }

    private static HashSet<Symbol> CollectCatchParameterNames(this BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        block.CollectCatchNamesFromStatement(names);
        return names;
    }

    private static HashSet<Symbol> CollectSimpleCatchParameterNames(this BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        block.CollectCatchNamesFromStatement(names, simpleOnly: true);
        return names;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateStatementList(
        IReadOnlyList<StatementNode> statements,
        JsEnvironment env,
        EvaluationContext context)
    {
        var resultJs = JsValue.Unit;
        foreach (var statement in statements)
        {
            context.ThrowIfCancellationRequested();

            var completionJs = statement.EvaluateStatementJsValue(env, context);
            var shouldStop = context.ShouldStopEvaluation;
            var shouldCapture =
                !completionJs.IsUnit &&
                (!shouldStop ||
                 context.IsReturn ||
                 context.IsThrow ||
                 context.IsYield ||
                 context.IsBreak ||
                 context.IsContinue);

            if (shouldCapture)
            {
                resultJs = completionJs;
            }

            if (shouldStop)
            {
                break;
            }
        }

        return resultJs;
    }

    private static HashSet<Symbol> CollectTopLevelLexicalNames(ImmutableArray<StatementNode> statements)
    {
        var names = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } decl:
                    foreach (var declarator in decl.Declarators)
                    {
                        declarator.Target.CollectSymbolsFromBinding(names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// Collects all var-declared names from the program body, including function declarations.
    /// This is used for GlobalDeclarationInstantiation to check for conflicts before creating bindings.
    /// </summary>
    private static HashSet<Symbol> CollectAllVarNames(
        ImmutableArray<StatementNode> statements,
        bool isStrict)
    {
        var names = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        CollectVarNamesFromStatements(statements, names, isStrict, false);
        return names;
    }

    private static void CollectVarNamesFromStatements(
        ImmutableArray<StatementNode> statements,
        HashSet<Symbol> names,
        bool isStrict,
        bool inBlockScope)
    {
        foreach (var statement in statements)
        {
            CollectVarNamesFromStatement(statement, names, isStrict, inBlockScope);
        }
    }

    private static void CollectVarNamesFromStatement(
        StatementNode statement,
        HashSet<Symbol> names,
        bool isStrict,
        bool inBlockScope)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDeclaration:
                    foreach (var declarator in varDeclaration.Declarators)
                    {
                        declarator.Target.CollectSymbolsFromBinding(names);
                    }

                    break;
                case FunctionDeclaration functionDeclaration:
                    // Function declarations at top-level are always hoisted as var names
                    // Block-scoped function declarations are lexically scoped (no AnnexB hoisting)
                    if (!inBlockScope)
                    {
                        names.Add(functionDeclaration.Name);
                    }

                    break;
                case BlockStatement block:
                    CollectVarNamesFromStatements(block.Statements, names, isStrict, true);
                    break;
                case IfStatement ifStatement:
                    CollectVarNamesFromStatement(ifStatement.Then, names, isStrict, true);
                    if (ifStatement.Else is not null)
                    {
                        statement = ifStatement.Else;
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
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar)
                    {
                        foreach (var declarator in initVar.Declarators)
                        {
                            declarator.Target.CollectSymbolsFromBinding(names);
                        }
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement { DeclarationKind: VariableKind.Var } forEachStatement:
                    forEachStatement.Target.CollectSymbolsFromBinding(names);
                    statement = forEachStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case TryStatement tryStatement:
                    CollectVarNamesFromStatements(tryStatement.TryBlock.Statements, names, isStrict, true);
                    if (tryStatement.Catch is not null)
                    {
                        CollectVarNamesFromStatements(tryStatement.Catch.Body.Statements, names, isStrict, true);
                    }

                    if (tryStatement.Finally is not null)
                    {
                        CollectVarNamesFromStatements(tryStatement.Finally.Statements, names, isStrict, true);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectVarNamesFromStatements(switchCase.Body.Statements, names, isStrict, true);
                    }

                    break;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
            }

            break;
        }
    }

    private static void HoistLexicalBindingTargetForGlobalTdz(BindingTarget target, JsEnvironment environment,
        bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    environment.DefineJsValue(id.Name, JsValue.Uninitialized, isConst: isConst,
                        isLexicalBinding: true, blocksFunctionScopeOverride: true);

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            HoistLexicalBindingTargetForGlobalTdz(elementTarget, environment, isConst);
                        }
                    }

                    if (arrayBinding.RestElement is { } restTarget)
                    {
                        target = restTarget;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        HoistLexicalBindingTargetForGlobalTdz(prop.Target, environment, isConst);
                    }

                    if (objectBinding.RestElement is { } restObjTarget)
                    {
                        target = restObjTarget;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    public static object? EvaluateProgram(this ProgramNode program, JsEnvironment environment,
        RealmState realmState,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool createStrictEnvironment = true,
        Symbol? functionNameHint = null,
        ImmutableArray<PrivateNameScope>? inheritedPrivateNameScopes = null,
        bool drainAwaitMicrotasks = true)
    {
        var result = program.EvaluateProgramJsValue(environment, realmState, cancellationToken,
            executionKind, createStrictEnvironment, functionNameHint, inheritedPrivateNameScopes,
            drainAwaitMicrotasks);
        if (result.IsUnit)
        {
            return Symbol.Undefined;
        }

        return result.Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => result.NumberValue != 0,
            JsValueKind.Number => result.NumberValue,
            _ => result.ObjectValue
        };
    }

    public static JsValue EvaluateProgramJsValue(this ProgramNode program, JsEnvironment environment,
        RealmState realmState,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script,
        bool createStrictEnvironment = true,
        Symbol? functionNameHint = null,
        ImmutableArray<PrivateNameScope>? inheritedPrivateNameScopes = null,
        bool drainAwaitMicrotasks = true)
    {
        // Track the current execution realm for cross-realm operations.
        // When setting properties on cross-realm objects (e.g., array.length = X on a
        // cross-realm Array), errors should be thrown from the CURRENT realm, not the
        // object's realm. This enables proper instanceof checks in assert.throws().
        var previousRealm = RealmState.Current;
        RealmState.Current = realmState;
        try
        {
            return EvaluateProgramJsValueCore(program, environment, realmState, cancellationToken,
                executionKind, createStrictEnvironment, functionNameHint, inheritedPrivateNameScopes,
                drainAwaitMicrotasks);
        }
        finally
        {
            RealmState.Current = previousRealm;
        }
    }

    private static JsValue EvaluateProgramJsValueCore(ProgramNode program, JsEnvironment environment,
        RealmState realmState,
        CancellationToken cancellationToken,
        ExecutionKind executionKind,
        bool createStrictEnvironment,
        Symbol? functionNameHint,
        ImmutableArray<PrivateNameScope>? inheritedPrivateNameScopes,
        bool drainAwaitMicrotasks)
    {
        var context = realmState.CreateContext(
            ScopeKind.Program,
            program.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
            cancellationToken,
            executionKind);
        // For eval, always disable identifier caching/slot analysis because eval runs in the caller's
        // lexical environment which may contain 'with' statements or allow deletable bindings.
        // The static analysis of the eval code alone doesn't tell us about the outer context.
        var allowScriptSlotAnalysis = context.RealmState.Options.AllowScriptSlotAnalysis &&
                                      executionKind != ExecutionKind.Eval;
        var allowsIdentifierCaching = AllowsIdentifierCaching(program);
        context.AllowIdentifierCache = allowScriptSlotAnalysis &&
                                       allowsIdentifierCaching;
        context.DrainAwaitMicrotasks = drainAwaitMicrotasks;
        if (inheritedPrivateNameScopes is { IsDefault: false, Length: > 0 } scopes)
        {
            context.EnterPrivateNameScopes(scopes);
            context.RealmState.Logger?.LogInformation(
                "Program inherited {PrivateScopeCount} private scopes",
                scopes.Length);
        }

        context.SourceReference = program.Source;
        context.IsStrictSource = program.IsStrict;
        using var nameHintHandle = functionNameHint is not null
            ? context.EnterFunctionNameHint(functionNameHint)
            : null;
        var executionEnvironment = program.IsStrict && createStrictEnvironment
            ? JsEnvironment.CreateInstance(environment, true, true,
                treatAsGlobalFunctionScope: environment.IsGlobalFunctionScope)
            : environment;
        if (program.IsStrict && !executionEnvironment.IsStrict)
        {
            executionEnvironment = JsEnvironment.CreateInstance(executionEnvironment, true, true,
                treatAsGlobalFunctionScope: executionEnvironment.IsGlobalFunctionScope);
        }

        // Set ScopeId for the script environment to enable slot-based variable lookup from nested functions
        // Note: We don't initialize slots because script-level var/let/const use dictionary-based storage
        if (program.ScopeId >= 0)
        {
            executionEnvironment.ScopeId = program.ScopeId;
        }

        if (executionKind == ExecutionKind.Script)
        {
            executionEnvironment.RealmState?.Engine?.SetGlobalExecutionScope(
                executionEnvironment.GetFunctionScope());
        }

        var programMode = program.IsStrict || executionEnvironment.IsStrict
            ? ScopeMode.Strict
            : ScopeMode.Sloppy;
        using var programScope = context.PushScope(ScopeKind.Program, programMode);

        var programBlock = new BlockStatement(program.Source, program.Body, program.IsStrict);
        var lexicalNames = programBlock.CollectLexicalNames();
        var topLevelLexicalNames = CollectTopLevelLexicalNames(program.Body);
        // For bodyLexicalNames used in global/var conflict checks, we only use TOP-LEVEL names.
        // Per ES spec GlobalDeclarationInstantiation, var declarations only conflict with
        // top-level lexical declarations, not with block-scoped let/const in nested blocks.
        var bodyLexicalNames = topLevelLexicalNames.Count == 0
            ? topLevelLexicalNames
            : new HashSet<Symbol>(topLevelLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
        var functionScope = executionEnvironment.GetFunctionScope();
        var reverseFunctionHoist = functionScope.IsGlobalFunctionScope &&
                                   executionKind == ExecutionKind.Script;
        var functionHoistDedupe = reverseFunctionHoist
            ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
            : null;
        // Get the engine's true GlobalEnvironment for storing/checking lexical names.
        // GlobalExecutionScope gets overwritten by each script, but GlobalEnvironment
        // persists and should be the canonical location for global lexical declarations.
        var trueGlobalEnvironment = context.RealmState.Engine?.GlobalEnvironment;
        var globalScopeToCheck = trueGlobalEnvironment ?? functionScope;

        // IMPORTANT: All conflict checks must happen BEFORE we merge any names.
        // Otherwise we'd detect conflicts with names we just added ourselves.
        // NOTE: These checks only apply to GlobalDeclarationInstantiation (scripts),
        // NOT to EvalDeclarationInstantiation. Per ES spec 18.2.1.1 PerformEval step 9,
        // direct eval creates a new declarative environment for lexical declarations,
        // so lexical names in eval don't conflict with outer lexical names.
        if (functionScope.IsGlobalFunctionScope && executionKind != ExecutionKind.Eval)
        {
            // Check if any new lexical names conflict with restricted globals
            foreach (var blockedName in bodyLexicalNames)
            {
                if (functionScope.HasRestrictedGlobalProperty(blockedName))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot redeclare var-scoped binding '{blockedName.Name}' with lexical declaration",
                        context,
                        context.RealmState);
                }
            }

            // Per ES spec GlobalDeclarationInstantiation step 5:
            // For each name in lexNames (new script's let/const/class declarations):
            //   5.a. If envRec.HasVarDeclaration(name) is true, throw SyntaxError
            //   5.b. If envRec.HasLexicalDeclaration(name) is true, throw SyntaxError
            //
            // This checks new lexical names against EXISTING lexical declarations from
            // previous scripts, preventing `let x` in a new script when `let x` already exists.
            foreach (var lexicalName in topLevelLexicalNames)
            {
                // Step 5.a: Check against existing var declarations
                if (functionScope.HasVarDeclaration(lexicalName))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Identifier '{lexicalName.Name}' has already been declared",
                        context,
                        context.RealmState);
                }

                // Step 5.b: Check against existing lexical declarations
                if (globalScopeToCheck.HasGlobalLexicalDeclaration(lexicalName))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Identifier '{lexicalName.Name}' has already been declared",
                        context,
                        context.RealmState);
                }
            }

            // Per ES spec GlobalDeclarationInstantiation step 6:
            // Check ALL var names for conflicts with existing lexical declarations BEFORE
            // creating any bindings. This ensures that a script like 'var x; var existingLet;'
            // doesn't create 'x' when it should throw SyntaxError for 'existingLet'.
            var allVarNames = CollectAllVarNames(program.Body, program.IsStrict);
            foreach (var varName in allVarNames)
            {
                if (globalScopeToCheck.HasGlobalLexicalDeclaration(varName))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Identifier '{varName.Name}' has already been declared",
                        context,
                        context.RealmState);
                }
            }
        }

        // Now that all conflict checks passed, merge/set the lexical names.
        // For non-global scripts (eval, strict wrappers), we can SET since they're isolated.
        // For the true GlobalEnvironment (non-strict global scripts), we must MERGE to preserve
        // lexical names from previous evalScript calls.
        if (executionKind != ExecutionKind.Eval &&
            trueGlobalEnvironment is not null &&
            ReferenceEquals(executionEnvironment, trueGlobalEnvironment))
        {
            // executionEnvironment IS the GlobalEnvironment - must merge to preserve cross-script names
            trueGlobalEnvironment.MergeBodyLexicalNames(bodyLexicalNames);
        }
        else
        {
            // Isolated scope (eval, strict wrapper, etc.) - safe to set
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // For strict wrappers, also merge top-level names to the true GlobalEnvironment
            // so cross-script checks work correctly
            if (executionKind != ExecutionKind.Eval && trueGlobalEnvironment is not null)
            {
                trueGlobalEnvironment.MergeBodyLexicalNames(topLevelLexicalNames);
            }
        }

        // Per ES spec, lexical declarations (let/const/class) must be hoisted to create
        // bindings in the TDZ (Temporal Dead Zone) BEFORE function hoisting.
        // This ensures closures that reference lexical variables will find TDZ bindings
        // and throw ReferenceError if accessed before initialization.
        //
        // If we plan to execute this program via the IR path, we must initialize the slot layout
        // BEFORE hoisting so that user bindings get created after internal IR slots. Otherwise,
        // internal 0-based IR slot writes can overwrite hoisted user bindings.
        var requiresDynamicScopeExecutor = DynamicScopeDetector.ContainsWithStatement(programBlock) ||
                                           executionEnvironment.HasWithObjectInChain();
        var canUseNoSlotIr = executionKind == ExecutionKind.Eval || !allowsIdentifierCaching;
        var canUseIrPlan = !requiresDynamicScopeExecutor &&
                           (context.AllowIdentifierCache || canUseNoSlotIr);
        ScriptPlanCache? scriptPlanCache = null;
        ExecutionPlan? scriptPlan = null;
        var enableScriptSlots = allowScriptSlotAnalysis && context.AllowIdentifierCache;
        if (canUseIrPlan)
        {
            scriptPlanCache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
            if (scriptPlanCache.Succeeded)
            {
                scriptPlan = scriptPlanCache.Plan!;

                // Only initialize slot layout up-front when this execution environment hasn't already
                // allocated slots (e.g. strict-wrapper script environments). For true GlobalEnvironment
                // runs, existing slots (Symbol.This, prior script bindings) may already exist and would
                // require an index offset to remain correct.
                if (!executionEnvironment.HasSlots)
                {
                    var rootSlotMap = scriptPlan.SafeRootSlotMap;
                    var mapMax = 0;
                    foreach (var idx in rootSlotMap.Values)
                    {
                        if (idx >= mapMax)
                        {
                            mapMax = idx + 1;
                        }
                    }

                    var requiredSlots = Math.Max(Math.Max(scriptPlan.RootSlotCount, scriptPlan.SlotSymbols.Length),
                        mapMax);
                    if (requiredSlots == 0)
                    {
                        requiredSlots = scriptPlan.SlotCount;
                    }

                    if (requiredSlots > 0)
                    {
                        var scopeLexicals = scriptPlan.SafeScopeLexicalBindings;
                        var rootLexicals = scriptPlan.SafeRootLexicalBindings;
                        if (rootLexicals.Count == 0 && scopeLexicals.TryGetValue(0, out var fromScope0))
                        {
                            rootLexicals = fromScope0;
                        }

                        executionEnvironment.ResetSlotLayoutForPlan(
                            requiredSlots,
                            rootSlotMap,
                            rootLexicals,
                            scriptPlan.SlotSymbols,
                            scriptPlan.LayoutId,
                            scriptPlan.RootScopeId);
                    }
                }
            }
        }

        foreach (var stmt in program.Body)
        {
            switch (stmt)
            {
                case VariableDeclaration
                {
                    Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                } lexDecl:
                    var isConst =
                        lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    foreach (var declarator in lexDecl.Declarators)
                    {
                        HoistLexicalBindingTargetForGlobalTdz(declarator.Target, executionEnvironment, isConst);
                    }

                    break;
                case ClassDeclaration classDecl:
                    // Class declarations are also lexically scoped and need TDZ
                    // Class declarations create mutable bindings (like let), not const
                    if (!executionEnvironment.HasBinding(classDecl.Name))
                    {
                        executionEnvironment.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isConst: false,
                            isLexicalBinding: true, blocksFunctionScopeOverride: true);
                    }

                    break;
            }
        }

        programBlock.HoistVarDeclarations(executionEnvironment,
            context,
            lexicalNames: lexicalNames,
            reverseFunctionHoist: reverseFunctionHoist,
            functionHoistDedupe: functionHoistDedupe);

        // Direct eval and active with-scope stay on the explicit dynamic-scope executor.
        if (requiresDynamicScopeExecutor)
        {
            context.RealmState.Logger?.LogInformation(
                "Executing script via dynamic-scope executor (captured with path)");
            var dynamicResult = EvaluateStatementList(program.Body, executionEnvironment, context);
            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return dynamicResult;
        }

        if (!canUseIrPlan)
        {
            const string failureReason = "script slot analysis disabled for non-dynamic script execution";
            context.RealmState.Logger?.LogInformation(
                "Rejecting non-dynamic script because IR script plan is unavailable: {FailureReason}",
                failureReason);
            throw new NotSupportedException($"IR plan generation failed for script: {failureReason}");
        }

        if (scriptPlanCache is not { Succeeded: true } || scriptPlan is null)
        {
            var failureReason = scriptPlanCache?.FailureReason ?? "IR script plan is unavailable";
            context.RealmState.Logger?.LogInformation(
                "Rejecting non-dynamic script because IR script plan is unavailable: {FailureReason}",
                failureReason);
            throw new NotSupportedException($"IR plan generation failed for script: {failureReason}");
        }

        context.RealmState.Logger?.LogInformation(
            "Executing script via IR path ({InstructionCount} instructions)",
            scriptPlan.Instructions.Length);

        // Script-level var declarations are marked with IsScriptLevel=true in the IR
        // so they correctly update the global object.
        return ExecutionPlanRunner.RunScript(
            scriptPlan,
            executionEnvironment,
            context);
    }
}
