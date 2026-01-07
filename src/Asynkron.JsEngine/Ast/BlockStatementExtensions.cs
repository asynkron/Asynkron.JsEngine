#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Evaluates a block statement and returns the completion value as JsValue.
    /// Returns JsValue.Undefined for empty blocks to match browser behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue EvaluateBlockJsValue(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        return block.EvaluateBlockCore(environment, context);
    }

    /// <summary>
    /// Core block evaluation that returns JsValue directly.
    /// Returns JsValue.Undefined for empty blocks to match browser behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue EvaluateBlockFast(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        return EvaluateStatementList(block.Statements, environment, context);
    }

    /// <summary>
    /// Slow path for blocks that need a new environment for lexical bindings.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue EvaluateBlockSlow(this BlockStatement block, JsEnvironment environment,
        EvaluationContext context)
    {
        using var scope = JsEnvironmentPool.Rent(environment, false, block.IsStrict, logger: context.RealmState.Logger);
        return block.EvaluateBlockSlowCore(scope, context);
    }

    /// <summary>
    /// Core slow path logic, separated to allow proper try/finally for pooling.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
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
                        scope.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isLexicalBinding: true,
                            blocksFunctionScopeOverride: true, isConst: false);
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
            var functionValue = functionDeclaration.Function.CreateFunctionValue(blockEnvironment, context,
                skipInternalNameBinding: true);
            blockEnvironment.DefineJsValue(
                functionDeclaration.Name,
                JsValue.FromObjectUnsafe(functionValue),
                true,
                isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
        }
    }

    private static void HoistLexicalBindingTargetForTdz(this BlockStatement block, BindingTarget target, JsEnvironment blockEnvironment, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!blockEnvironment.HasBinding(id.Name))
                    {
                        blockEnvironment.DefineJsValue(id.Name, JsValue.Uninitialized, isLexicalBinding: true, blocksFunctionScopeOverride: true, isConst: isConst);
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

        var effectiveCatchNames = catchParameterNames is null
            ? block.CollectCatchParameterNames()
            : [.. catchParameterNames];
        if (catchParameterNames is not null)
        {
            effectiveCatchNames.UnionWith(block.CollectCatchParameterNames());
        }

        var effectiveSimpleCatchNames = simpleCatchParameterNames is null
            ? block.CollectSimpleCatchParameterNames()
            : [.. simpleCatchParameterNames];
        if (simpleCatchParameterNames is not null)
        {
            effectiveSimpleCatchNames.UnionWith(block.CollectSimpleCatchParameterNames());
        }

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
        var merged = new HashSet<Symbol>(catchParameterNames);
        merged.UnionWith(block.CollectCatchParameterNames());
        return merged;
    }

    private static HashSet<Symbol> MergeSimpleCatchNames(this BlockStatement block, HashSet<Symbol> simpleCatchParameterNames)
    {
        var merged = new HashSet<Symbol>(simpleCatchParameterNames);
        merged.UnionWith(block.CollectSimpleCatchParameterNames());
        return merged;
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
}
