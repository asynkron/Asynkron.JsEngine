#region

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

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
            {
                var result = expressionStatement.Expression.EvaluateExpression(environment, context);
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
        private JsValue EvaluateStatementJsValueSlow(
            JsEnvironment environment,
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
                FunctionDeclaration => EvaluateFunctionDeclarationJsValue(),
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
                            true);
                        break;
                    case IfStatement ifStatement:
                        ifStatement.Then.HoistFromStatement(environment, context, false,
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
                            initVar.HoistFromStatement(environment, context, hoistFunctionValues, lexicalNames,
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
                            forEachStatement.Target.HoistFromBindingTarget(environment, context, lexicalNames);
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
                        tryStatement.TryBlock.HoistVarDeclarationsPass(environment, context, false,
                            tryStatement.TryBlock.MergeLexicalNames(lexicalNames),
                            tryStatement.TryBlock.MergeCatchNames(catchParameterNames),
                            tryStatement.TryBlock.MergeSimpleCatchNames(simpleCatchParameterNames),
                            pass,
                            true);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            catchClause.Body.HoistVarDeclarationsPass(environment, context, false,
                                catchClause.Body.MergeLexicalNames(lexicalNames),
                                catchClause.Body.MergeCatchNames(catchParameterNames),
                                catchClause.Body.MergeSimpleCatchNames(simpleCatchParameterNames),
                                pass,
                                true);
                        }

                        if (tryStatement.Finally is { } finallyBlock)
                        {
                            finallyBlock.HoistVarDeclarationsPass(environment, context, false,
                                finallyBlock.MergeLexicalNames(lexicalNames),
                                finallyBlock.MergeCatchNames(catchParameterNames),
                                finallyBlock.MergeSimpleCatchNames(simpleCatchParameterNames),
                                pass,
                                true);
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

                        if (hoistFunctionValues)
                        {
                            // Pass skipInternalNameBinding: true so the SyncFunctionInvoker doesn't create
                            // an internal const binding for the function name. For function declarations,
                            // the name binding lives in the outer (function/global) scope and is mutable.
                            var functionValue = functionDeclaration.Function.CreateFunctionValue(environment, context,
                                skipInternalNameBinding: true);
                            var fnValueJs = JsValue.FromObjectUnsafe(functionValue);
                            if (Environment.GetEnvironmentVariable("DEBUG_SLOT") == "1")
                            {
                                File.AppendAllText("/tmp/slotdebug.txt",
                                    $"HoistFunction enter scope={environment.ScopeId} name={functionDeclaration.Name.Name}{Environment.NewLine}");
                            }
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
                                if (Environment.GetEnvironmentVariable("DEBUG_SLOT") == "1")
                                {
                                    File.AppendAllText("/tmp/slotdebug.txt",
                                        $"HoistFunction slot scope={environment.ScopeId} name={functionDeclaration.Name.Name} slot={slotIndex}{Environment.NewLine}");
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
                            if (catchClause.Binding is not null)
                            {
                                catchClause.Binding.CollectSymbolsFromBinding(names);
                            }

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

        private void CollectCatchNamesFromStatement(HashSet<Symbol> names)
        {
            while (true)
            {
                switch (statement)
                {
                    case BlockStatement block:
                        foreach (var inner in block.Statements)
                        {
                            inner.CollectCatchNamesFromStatement(names);
                        }

                        break;
                    case IfStatement ifStatement:
                        ifStatement.Then.CollectCatchNamesFromStatement(names);
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
                            switchCase.Body.CollectCatchNamesFromStatement(names);
                        }

                        break;
                    case TryStatement tryStatement:
                        tryStatement.TryBlock.CollectCatchNamesFromStatement(names);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            if (catchClause.Binding is not null)
                            {
                                catchClause.Binding.CollectSymbolsFromBinding(names);
                            }

                            catchClause.Body.CollectCatchNamesFromStatement(names);
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
                            inner.CollectSimpleCatchNamesFromStatement(names);
                        }

                        break;
                    case IfStatement ifStatement:
                        ifStatement.Then.CollectSimpleCatchNamesFromStatement(names);
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
                            switchCase.Body.CollectSimpleCatchNamesFromStatement(names);
                        }

                        break;
                    case TryStatement tryStatement:
                        tryStatement.TryBlock.CollectSimpleCatchNamesFromStatement(names);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            if (catchClause.Binding is IdentifierBinding identifierBinding)
                            {
                                names.Add(identifierBinding.Name);
                            }

                            catchClause.Body.CollectSimpleCatchNamesFromStatement(names);
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
