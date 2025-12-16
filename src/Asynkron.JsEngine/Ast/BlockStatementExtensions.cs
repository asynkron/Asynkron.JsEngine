using System.Diagnostics;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BlockStatement block)
    {
        /// <summary>
        /// Evaluates a block statement and returns the completion value as JsValue.
        /// Returns JsValue.Undefined for empty blocks to match browser behavior.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBlockJsValue(
            JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateBlockCore(block, environment, context);
        }

        /// <summary>
        /// Core block evaluation that returns JsValue directly.
        /// Returns JsValue.Undefined for empty blocks to match browser behavior.
        /// </summary>
        private JsValue EvaluateBlockCore(
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (context.AllowIdentifierCache)
            {
                context.AllowIdentifierCache = AllowsIdentifierCaching(block);
            }
            var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

            // Start with Undefined (not Unit) to match browser behavior for empty blocks
            var resultJs = JsValue.Undefined;

            // Fast path: if the block has no lexical/function decls, execute directly in the incoming environment
            if (!hoistPlan.NeedsEnvironment)
            {
                foreach (var statement in block.Statements)
                {
                    context.ThrowIfCancellationRequested();

                    var completionJs = EvaluateStatementJsValue(statement, environment, context);
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

            var scope = new JsEnvironment(environment, false, block.IsStrict);

            var mode = scope.IsStrict || block.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var scopeHandle = context.PushScope(ScopeKind.Block, mode);
            using var blockActivity = Activity.Current?.StartEvaluatorActivity("Scope:Block", context, block.Source);
            blockActivity?.SetTag("js.block.strict", block.IsStrict);
            blockActivity?.SetTag("js.block.statementCount", block.Statements.Length);

            // Per ES spec, lexical declarations (let/const/class) must be hoisted to create
            // bindings in the TDZ (Temporal Dead Zone) BEFORE function hoisting.
            // This ensures closures that reference lexical variables will find TDZ bindings
            // and throw ReferenceError if accessed before initialization.
            foreach (var stmt in block.Statements)
            {
                if (stmt is VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } lexDecl)
                {
                    var isConst = lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    foreach (var declarator in lexDecl.Declarators)
                    {
                        HoistLexicalBindingTargetForTdz(block, declarator.Target, scope, isConst);
                    }
                }
            }

            // Block-scoped function declarations (strict mode behavior - no AnnexB hoisting)
            InstantiateLexicalBlockFunctions(block, scope, context);

            foreach (var statement in block.Statements)
            {
                context.ThrowIfCancellationRequested();

                var completionJs = EvaluateStatementJsValue(statement, scope, context);
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

        private void InstantiateLexicalBlockFunctions(
            JsEnvironment blockEnvironment,
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
                var functionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context,
                    skipInternalNameBinding: true);
                blockEnvironment.DefineJsValue(
                    functionDeclaration.Name,
                    JsValue.FromObject(functionValue),
                    isConst: true,
                    isLexical: true,
                    blocksFunctionScopeOverride: true);
            }
        }

        private void HoistLexicalBindingTargetForTdz(BindingTarget target, JsEnvironment blockEnvironment, bool isConst)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!blockEnvironment.HasBinding(id.Name))
                    {
                        blockEnvironment.DefineJsValue(id.Name, JsValue.FromObject(JsEnvironment.Uninitialized), isLexical: true,
                            blocksFunctionScopeOverride: true, isConst: isConst);
                    }
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            HoistLexicalBindingTargetForTdz(block, elementTarget, blockEnvironment, isConst);
                        }
                    }
                    if (arrayBinding.RestElement is { } restTarget)
                    {
                        HoistLexicalBindingTargetForTdz(block, restTarget, blockEnvironment, isConst);
                    }
                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        HoistLexicalBindingTargetForTdz(block, prop.Target, blockEnvironment, isConst);
                    }
                    if (objectBinding.RestElement is { } restObjTarget)
                    {
                        HoistLexicalBindingTargetForTdz(block, restObjTarget, blockEnvironment, isConst);
                    }
                    break;
            }
        }

        private void HoistVarDeclarations(JsEnvironment environment,
            EvaluationContext context,
            bool hoistFunctionValues = true,
            HashSet<Symbol>? lexicalNames = null,
            HashSet<Symbol>? catchParameterNames = null,
            HashSet<Symbol>? simpleCatchParameterNames = null,
            bool inBlockScope = false)
        {
            var effectiveLexicalNames = lexicalNames is null
                ? CollectLexicalNames(block)
                : [..lexicalNames];
            if (lexicalNames is not null)
            {
                effectiveLexicalNames.UnionWith(CollectLexicalNames(block));
            }

            var effectiveCatchNames = catchParameterNames is null
                ? CollectCatchParameterNames(block)
                : [..catchParameterNames];
            if (catchParameterNames is not null)
            {
                effectiveCatchNames.UnionWith(CollectCatchParameterNames(block));
            }

            var effectiveSimpleCatchNames = simpleCatchParameterNames is null
                ? CollectSimpleCatchParameterNames(block)
                : [..simpleCatchParameterNames];
            if (simpleCatchParameterNames is not null)
            {
                effectiveSimpleCatchNames.UnionWith(CollectSimpleCatchParameterNames(block));
            }

            HoistVarDeclarationsPass(
                block,
                environment,
                context,
                hoistFunctionValues,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Functions,
                inBlockScope);
            HoistVarDeclarationsPass(
                block,
                environment,
                context,
                false,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Vars,
                inBlockScope);
        }

        private void HoistVarDeclarationsPass(JsEnvironment environment,
            EvaluationContext context,
            bool hoistFunctionValues,
            HashSet<Symbol> lexicalNames,
            HashSet<Symbol> catchParameterNames,
            HashSet<Symbol> simpleCatchParameterNames,
            HoistPass pass,
            bool inBlockScope)
        {
            foreach (var statement in block.Statements)
            {
                HoistFromStatement(statement, environment, context, hoistFunctionValues, lexicalNames,
                    catchParameterNames,
                    simpleCatchParameterNames,
                    pass,
                    inBlockScope);
            }
        }

        private HashSet<Symbol> MergeLexicalNames(HashSet<Symbol> lexicalNames)
        {
            var merged = new HashSet<Symbol>(lexicalNames);
            merged.UnionWith(CollectLexicalNames(block));
            return merged;
        }

        private HashSet<Symbol> MergeCatchNames(HashSet<Symbol> catchParameterNames)
        {
            var merged = new HashSet<Symbol>(catchParameterNames);
            merged.UnionWith(CollectCatchParameterNames(block));
            return merged;
        }

        private HashSet<Symbol> MergeSimpleCatchNames(HashSet<Symbol> simpleCatchParameterNames)
        {
            var merged = new HashSet<Symbol>(simpleCatchParameterNames);
            merged.UnionWith(CollectSimpleCatchParameterNames(block));
            return merged;
        }

        private HashSet<Symbol> CollectLexicalNames()
        {
            var names = new HashSet<Symbol>();
            CollectLexicalNamesFromStatement(block, names);
            return names;
        }

        private HashSet<Symbol> CollectFunctionNames()
        {
            var names = new HashSet<Symbol>();
            foreach (var statement in block.Statements)
            {
                if (statement is FunctionDeclaration functionDeclaration)
                {
                    names.Add(functionDeclaration.Name);
                }
            }

            return names;
        }

        private HashSet<Symbol> CollectCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectCatchNamesFromStatement(block, names);
            return names;
        }

        private HashSet<Symbol> CollectSimpleCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectSimpleCatchNamesFromStatement(block, names);
            return names;
        }

        private bool HasHoistableDeclarations()
        {
            var stack = new Stack<StatementNode>();
            stack.Push(block);

            while (stack.Count > 0)
            {
                var statement = stack.Pop();
                switch (statement)
                {
                    case VariableDeclaration { Kind: VariableKind.Var }:
                    case FunctionDeclaration:
                        return true;
                    case BlockStatement b:
                        foreach (var inner in b.Statements)
                        {
                            stack.Push(inner);
                        }

                        break;
                    case IfStatement ifStatement:
                        stack.Push(ifStatement.Then);
                        if (ifStatement.Else is { } elseBranch)
                        {
                            stack.Push(elseBranch);
                        }

                        break;
                    case WhileStatement whileStatement:
                        stack.Push(whileStatement.Body);
                        break;
                    case DoWhileStatement doWhileStatement:
                        stack.Push(doWhileStatement.Body);
                        break;
                    case WithStatement withStatement:
                        stack.Push(withStatement.Body);
                        break;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var })
                        {
                            return true;
                        }

                        if (forStatement.Body is not null)
                        {
                            stack.Push(forStatement.Body);
                        }

                        break;
                    case ForEachStatement forEachStatement:
                        if (forEachStatement.DeclarationKind == VariableKind.Var)
                        {
                            return true;
                        }

                        stack.Push(forEachStatement.Body);
                        break;
                    case LabeledStatement labeled:
                        stack.Push(labeled.Statement);
                        break;
                    case TryStatement tryStatement:
                        stack.Push(tryStatement.TryBlock);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            stack.Push(catchClause.Body);
                        }

                        if (tryStatement.Finally is { } finallyBlock)
                        {
                            stack.Push(finallyBlock);
                        }

                        break;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            stack.Push(switchCase.Body);
                        }

                        break;
                }
            }

            return false;
        }
    }
}
