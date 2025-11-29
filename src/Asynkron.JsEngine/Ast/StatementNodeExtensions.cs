namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(StatementNode statement)
    {
        private object? EvaluateStatement(
            JsEnvironment environment,
            EvaluationContext context,
            Symbol? activeLabel = null)
        {
            context.SourceReference = statement.Source;
            context.ThrowIfCancellationRequested();
            using var statementActivity =
                StartEvaluatorActivity($"Statement:{statement.GetType().Name}", context, statement.Source);

            return statement switch
            {
                BlockStatement block => EvaluateBlock(block, environment, context),
                ExpressionStatement expressionStatement => EvaluateExpression(expressionStatement.Expression,
                    environment,
                    context),
                ReturnStatement returnStatement => EvaluateReturn(returnStatement, environment, context),
                ThrowStatement throwStatement => EvaluateThrow(throwStatement, environment, context),
                VariableDeclaration declaration => EvaluateVariableDeclaration(declaration, environment, context),
                FunctionDeclaration functionDeclaration => EvaluateFunctionDeclaration(functionDeclaration, environment,
                    context),
                IfStatement ifStatement => EvaluateIf(ifStatement, environment, context),
                WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context, activeLabel),
                DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context,
                    activeLabel),
                ForStatement forStatement => EvaluateFor(forStatement, environment, context, activeLabel),
                ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context,
                    activeLabel),
                BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
                ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
                LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
                TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
                SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context, activeLabel),
                ClassDeclaration classDeclaration => EvaluateClass(classDeclaration, environment, context),
                WithStatement withStatement => EvaluateWith(withStatement, environment, context),
                EmptyStatement => EmptyCompletion,
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
            };
        }

        private bool IsStrictBlock()
        {
            return statement is BlockStatement { IsStrict: true };
        }

        private void HoistFromStatement(JsEnvironment environment,
            EvaluationContext context,
            bool hoistFunctionValues,
            HashSet<Symbol> lexicalNames,
            HashSet<Symbol> catchParameterNames,
            HashSet<Symbol> simpleCatchParameterNames,
            HoistPass pass,
            bool inBlockScope)
        {
            while (true)
            {
                switch (statement)
                {
                    case VariableDeclaration { Kind: VariableKind.Var } varDeclaration when pass == HoistPass.Vars:
                        foreach (var declarator in varDeclaration.Declarators)
                        {
                            HoistFromBindingTarget(declarator.Target, environment, context, lexicalNames);
                        }

                        break;
                    case BlockStatement block:
                        HoistVarDeclarationsPass(
                            block,
                            environment,
                            context,
                            hoistFunctionValues,
                            MergeLexicalNames(block, lexicalNames),
                            MergeCatchNames(block, catchParameterNames),
                            MergeSimpleCatchNames(block, simpleCatchParameterNames),
                            pass,
                            true);
                        break;
                    case IfStatement ifStatement:
                        HoistFromStatement(ifStatement.Then, environment, context, false,
                            lexicalNames, catchParameterNames, simpleCatchParameterNames, pass, true);
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
                        continue;
                    case DoWhileStatement doWhileStatement:
                        statement = doWhileStatement.Body;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    case WithStatement withStatement:
                        statement = withStatement.Body;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar &&
                            pass == HoistPass.Vars)
                        {
                            HoistFromStatement(initVar, environment, context, hoistFunctionValues, lexicalNames,
                                catchParameterNames, simpleCatchParameterNames, pass,
                                inBlockScope);
                        }

                        statement = forStatement.Body;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    case ForEachStatement forEachStatement:
                        if (pass == HoistPass.Vars && forEachStatement.DeclarationKind == VariableKind.Var)
                        {
                            HoistFromBindingTarget(forEachStatement.Target, environment, context, lexicalNames);
                        }

                        statement = forEachStatement.Body;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    case LabeledStatement labeled:
                        statement = labeled.Statement;
                        continue;
                    case TryStatement tryStatement:
                        HoistVarDeclarationsPass(tryStatement.TryBlock, environment, context, false,
                            MergeLexicalNames(tryStatement.TryBlock, lexicalNames),
                            MergeCatchNames(tryStatement.TryBlock, catchParameterNames),
                            MergeSimpleCatchNames(tryStatement.TryBlock, simpleCatchParameterNames),
                            pass,
                            true);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            HoistVarDeclarationsPass(catchClause.Body, environment, context, false,
                                MergeLexicalNames(catchClause.Body, lexicalNames),
                                MergeCatchNames(catchClause.Body, catchParameterNames),
                                MergeSimpleCatchNames(catchClause.Body, simpleCatchParameterNames),
                                pass,
                                true);
                        }

                        if (tryStatement.Finally is { } finallyBlock)
                        {
                            HoistVarDeclarationsPass(finallyBlock, environment, context, false,
                                MergeLexicalNames(finallyBlock, lexicalNames),
                                MergeCatchNames(finallyBlock, catchParameterNames),
                                MergeSimpleCatchNames(finallyBlock, simpleCatchParameterNames),
                                pass,
                                true);
                        }

                        break;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            HoistVarDeclarationsPass(switchCase.Body, environment, context, false,
                                MergeLexicalNames(switchCase.Body, lexicalNames),
                                MergeCatchNames(switchCase.Body, catchParameterNames),
                                MergeSimpleCatchNames(switchCase.Body, simpleCatchParameterNames),
                                pass,
                                true);
                        }

                        break;
                    case FunctionDeclaration functionDeclaration:
                    {
                        if (pass != HoistPass.Functions)
                        {
                            break;
                        }

                        if (context.BlockedFunctionVarNames is { } blockedHoists &&
                            blockedHoists.Contains(functionDeclaration.Name))
                        {
                            break;
                        }

                        if (context.CurrentScope.IsStrict && lexicalNames.Contains(functionDeclaration.Name))
                        {
                            break;
                        }

                        var hasNonCatchLexical = lexicalNames.Contains(functionDeclaration.Name) &&
                                                 !simpleCatchParameterNames.Contains(functionDeclaration.Name);
                        var functionScope = environment.GetFunctionScope();
                        var isAnnexBBlockFunction =
                            inBlockScope &&
                            context.CurrentScope is { IsStrict: false, AllowAnnexB: true };

                        if (isAnnexBBlockFunction)
                        {
                            if (hasNonCatchLexical ||
                                functionScope.HasBodyLexicalName(functionDeclaration.Name))
                            {
                                break;
                            }

                            var forceConfigurableGlobal =
                                functionScope.IsGlobalFunctionScope;
                            functionScope.DefineFunctionScoped(
                                functionDeclaration.Name,
                                Symbol.Undefined,
                                false,
                                context: context,
                                blocksFunctionScopeOverride: true,
                                globalVarConfigurable: forceConfigurableGlobal ? true : null);

                            break;
                        }

                        if (inBlockScope)
                        {
                            break;
                        }

                        if (hoistFunctionValues)
                        {
                            var functionValue = CreateFunctionValue(functionDeclaration.Function, environment, context);
                            environment.DefineFunctionScoped(
                                functionDeclaration.Name,
                                functionValue,
                                true,
                                true,
                                context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                                context);
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

        private void CollectLexicalNamesFromStatement(HashSet<Symbol> names)
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
                    case WithStatement withStatement:
                        statement = withStatement.Body;
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

        private void CollectCatchNamesFromStatement(HashSet<Symbol> names)
        {
            while (true)
            {
                switch (statement)
                {
                    case BlockStatement block:
                        foreach (var inner in block.Statements)
                        {
                            CollectCatchNamesFromStatement(inner, names);
                        }

                        break;
                    case IfStatement ifStatement:
                        CollectCatchNamesFromStatement(ifStatement.Then, names);
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
                        if (forStatement.Body is not null)
                        {
                            statement = forStatement.Body;
                            continue;
                        }

                        break;
                    case ForEachStatement forEachStatement:
                        statement = forEachStatement.Body;
                        continue;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            CollectCatchNamesFromStatement(switchCase.Body, names);
                        }

                        break;
                    case TryStatement tryStatement:
                        CollectCatchNamesFromStatement(tryStatement.TryBlock, names);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            CollectSymbolsFromBinding(catchClause.Binding, names);
                            CollectCatchNamesFromStatement(catchClause.Body, names);
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

        private void CollectSimpleCatchNamesFromStatement(HashSet<Symbol> names)
        {
            while (true)
            {
                switch (statement)
                {
                    case BlockStatement block:
                        foreach (var inner in block.Statements)
                        {
                            CollectSimpleCatchNamesFromStatement(inner, names);
                        }

                        break;
                    case IfStatement ifStatement:
                        CollectSimpleCatchNamesFromStatement(ifStatement.Then, names);
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
                        if (forStatement.Body is not null)
                        {
                            statement = forStatement.Body;
                            continue;
                        }

                        break;
                    case ForEachStatement forEachStatement:
                        statement = forEachStatement.Body;
                        continue;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            CollectSimpleCatchNamesFromStatement(switchCase.Body, names);
                        }

                        break;
                    case TryStatement tryStatement:
                        CollectSimpleCatchNamesFromStatement(tryStatement.TryBlock, names);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            if (catchClause.Binding is IdentifierBinding identifierBinding)
                            {
                                names.Add(identifierBinding.Name);
                            }

                            CollectSimpleCatchNamesFromStatement(catchClause.Body, names);
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
}
