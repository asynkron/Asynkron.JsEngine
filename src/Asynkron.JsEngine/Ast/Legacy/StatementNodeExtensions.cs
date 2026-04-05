#region

using System.Runtime.CompilerServices;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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
                            var functionValue = functionDeclaration.Function.CreateFunctionValue(environment, context,
                                skipInternalNameBinding: true);
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
}
