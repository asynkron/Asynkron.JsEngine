using System.Diagnostics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(StatementNode statement)
    {
        /// <summary>
        /// Evaluates a statement and returns the completion value as JsValue.
        /// Tiny hot path for inlining - only handles the most common cases.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateStatementJsValue(
            JsEnvironment environment,
            EvaluationContext context,
            Symbol? activeLabel = null)
        {
            context.SourceReference = statement.Source;
            context.ThrowIfCancellationRequested();

            // Hot path - explicit type checks for best inlining
            if (statement is ExpressionStatement expressionStatement)
                return EvaluateExpression(expressionStatement.Expression, environment, context);
            if (statement is BlockStatement block)
                return EvaluateBlockJsValue(block, environment, context);
            if (statement is IfStatement ifStatement)
                return EvaluateIfJsValue(ifStatement, environment, context);
            if (statement is ReturnStatement returnStatement)
                return EvaluateReturnJsValue(returnStatement, environment, context);
            if (statement is ForStatement forStatement)
                return EvaluateForJsValue(forStatement, environment, context, activeLabel);
            if (statement is EmptyStatement)
                return JsValue.Unit;

            return statement.EvaluateStatementJsValueSlow(environment, context, activeLabel);
        }

        /// <summary>
        /// Slow path for less common statement types - marked NoInlining to keep hot path small.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateStatementJsValueSlow(
            JsEnvironment environment,
            EvaluationContext context,
            Symbol? activeLabel)
        {
            // Medium frequency statements
            switch (statement)
            {
                case WhileStatement whileStatement:
                    return EvaluateWhileJsValue(whileStatement, environment, context, activeLabel);
                case DoWhileStatement doWhileStatement:
                    return EvaluateDoWhileJsValue(doWhileStatement, environment, context, activeLabel);
                case SwitchStatement switchStatement:
                    return EvaluateSwitchJsValue(switchStatement, environment, context, activeLabel);
                case TryStatement tryStatement:
                    return EvaluateTryJsValue(tryStatement, environment, context);
                case LabeledStatement labeledStatement:
                    return EvaluateLabeledJsValue(labeledStatement, environment, context);
            }

            // Low frequency statements with activity tracking

            return statement switch
            {
                ThrowStatement throwStatement => EvaluateThrowJsValue(throwStatement, environment, context),
                VariableDeclaration declaration => EvaluateVariableDeclarationJsValue(declaration, environment, context),
                FunctionDeclaration functionDeclaration => EvaluateFunctionDeclarationJsValue(functionDeclaration, environment,
                    context),
                ForEachStatement forEachStatement => EvaluateForEachJsValue(forEachStatement, environment, context,
                    activeLabel),
                BreakStatement breakStatement => EvaluateBreakJsValue(breakStatement, context),
                ContinueStatement continueStatement => EvaluateContinueJsValue(continueStatement, context),
                ClassDeclaration classDeclaration => EvaluateClassJsValue(classDeclaration, environment, context),
                WithStatement withStatement => EvaluateWithJsValue(withStatement, environment, context),
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{statement.GetType().Name}'."),
            };
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
                    case ExportDeclarationStatement exportDeclaration:
                        statement = exportDeclaration.Declaration;
                        continue;
                    case ExportDefaultStatement { Value: ExportDefaultDeclaration { Declaration: { } decl } }:
                        statement = decl;
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

                        if (context.CurrentScope.IsStrict && lexicalNames.Contains(functionDeclaration.Name))
                        {
                            break;
                        }

                        // Block-scoped function declarations are lexically scoped (no hoisting to function scope)
                        if (inBlockScope)
                        {
                            break;
                        }

                        if (hoistFunctionValues)
                        {
                            // Pass skipInternalNameBinding: true so the TypedFunction doesn't create
                            // an internal const binding for the function name. For function declarations,
                            // the name binding lives in the outer (function/global) scope and is mutable.
                            var functionValue = CreateFunctionValue(functionDeclaration.Function, environment, context,
                                skipInternalNameBinding: true);
                            environment.DefineFunctionScoped(
                                functionDeclaration.Name,
                                functionValue,
                                true,
                                true,
                                context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                                context,
                                allowExistingGlobalFunctionRedeclaration: false,
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
                    case VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } letDecl:
                        foreach (var declarator in letDecl.Declarators)
                        {
                            CollectSymbolsFromBinding(declarator.Target, names);
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
                                Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
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
                        if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const
                            or VariableKind.Using or VariableKind.AwaitUsing)
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
                            if (catchClause.Binding is not null)
                            {
                                CollectSymbolsFromBinding(catchClause.Binding, names);
                            }
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
                            if (catchClause.Binding is not null)
                            {
                                CollectSymbolsFromBinding(catchClause.Binding, names);
                            }
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
