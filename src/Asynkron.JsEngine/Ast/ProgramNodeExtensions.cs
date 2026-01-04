#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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
                        CollectBindingNames(declarator.Target, names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
            }
        }

        return names;
    }

    private static void CollectBindingNames(BindingTarget target, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    names.Add(identifier.Name);
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectBindingNames(element.Target, names);
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
                        CollectBindingNames(property.Target, names);
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
                        CollectBindingNames(declarator.Target, names);
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
                            CollectBindingNames(declarator.Target, names);
                        }
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement { DeclarationKind: VariableKind.Var } forEachStatement:
                    CollectBindingNames(forEachStatement.Target, names);
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
                    if (!environment.HasBinding(id.Name))
                    {
                        environment.DefineJsValue(id.Name, JsValue.Uninitialized, isLexicalBinding: true,
                            blocksFunctionScopeOverride: true, isConst: isConst);
                    }

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

    extension(ProgramNode program)
    {
        public object? EvaluateProgram(
            JsEnvironment environment,
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

        public JsValue EvaluateProgramJsValue(
            JsEnvironment environment,
            RealmState realmState,
            CancellationToken cancellationToken = default,
            ExecutionKind executionKind = ExecutionKind.Script,
            bool createStrictEnvironment = true,
            Symbol? functionNameHint = null,
            ImmutableArray<PrivateNameScope>? inheritedPrivateNameScopes = null,
            bool drainAwaitMicrotasks = true)
        {
            var context = realmState.CreateContext(
                ScopeKind.Program,
                program.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
                cancellationToken,
                executionKind);
            // For eval, always disable identifier caching because eval runs in the caller's
            // lexical environment which may contain 'with' statements. The static analysis
            // of the eval code alone doesn't tell us about the outer context.
            var allowScriptSlotAnalysis = context.RealmState.Options.AllowScriptSlotAnalysis;
            context.AllowIdentifierCache = allowScriptSlotAnalysis &&
                                           executionKind != ExecutionKind.Eval &&
                                           AllowsIdentifierCaching(program);
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
                ? new JsEnvironment(environment, true, true,
                    treatAsGlobalFunctionScope: environment.IsGlobalFunctionScope)
                : environment;
            if (program.IsStrict && !executionEnvironment.IsStrict)
            {
                executionEnvironment = new JsEnvironment(executionEnvironment, true, true,
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
            var catchParameterNames = programBlock.CollectCatchParameterNames();
            var simpleCatchParameterNames = programBlock.CollectSimpleCatchParameterNames();
            // For bodyLexicalNames used in global/var conflict checks, we only use TOP-LEVEL names.
            // Per ES spec GlobalDeclarationInstantiation, var declarations only conflict with
            // top-level lexical declarations, not with block-scoped let/const in nested blocks.
            var bodyLexicalNames = topLevelLexicalNames.Count == 0
                ? topLevelLexicalNames
                : new HashSet<Symbol>(topLevelLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            var functionScope = executionEnvironment.GetFunctionScope();
            var reverseFunctionHoist = functionScope.IsGlobalFunctionScope &&
                                       executionKind == ExecutionKind.Script;
            HashSet<Symbol>? functionHoistDedupe = reverseFunctionHoist
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
            var hasDynamicScope = !AllowsIdentifierCaching(program);
            ScriptPlanCache? scriptPlanCache = null;
            ExecutionPlan? scriptPlan = null;
            var enableScriptSlots = allowScriptSlotAnalysis && !hasDynamicScope;
            if (enableScriptSlots)
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
                            executionEnvironment.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isLexicalBinding: true,
                                blocksFunctionScopeOverride: true, isConst: false);
                        }

                        break;
                }
            }

            programBlock.HoistVarDeclarations(executionEnvironment,
                context,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames,
                reverseFunctionHoist: reverseFunctionHoist,
                functionHoistDedupe: functionHoistDedupe);

            // Dynamic scope constructs (with/eval) require dictionary-based lookups; skip IR/slot stamping.
            if (hasDynamicScope)
            {
                context.RealmState.Logger?.LogInformation("Skipping IR path due to dynamic scope (with/eval).");
                var dynamicResult = programBlock.EvaluateStatementJsValue(executionEnvironment, context);
                if (context.IsThrow)
                {
                    throw new ThrowSignal(context.FlowValue);
                }

                return dynamicResult;
            }

            // Try IR execution path first (unified execution model)
            // Fall back to AST walking if IR building fails or encounters unsupported constructs
            if (enableScriptSlots && scriptPlanCache is { Succeeded: true } && scriptPlan is not null)
            {
                context.RealmState.Logger?.LogInformation(
                    "Executing script via IR path ({InstructionCount} instructions)",
                    scriptPlan.Instructions.Length);

                try
                {
                    // Use unified ExecutionPlanRunner which handles all the complexity of loops,
                    // break/continue signals, try/catch, etc. consistently.
                    // Script-level var declarations are marked with IsScriptLevel=true in the IR
                    // so they correctly update the global object.
                    var irResult = ExecutionPlanRunner.RunScript(
                        scriptPlan,
                        executionEnvironment,
                        context);
                    return irResult;
                }
                catch (NotSupportedException ex)
                {
                    // Unsupported instruction encountered - fall back to AST walking
                    context.RealmState.Logger?.LogWarning(
                        "Script IR execution fallback: {Reason}",
                        ex.Message);
                    // Continue to AST walking path below
                }
            }
            else
            {
                context.RealmState.Logger?.LogInformation(
                    "Script IR building failed: {Reason}. Using AST walking.",
                    scriptPlanCache?.FailureReason ?? "unknown");
            }

            // AST walking fallback path
            var resultJs = JsValue.Unit;
            foreach (var statement in program.Body)
            {
                context.ThrowIfCancellationRequested();
                var completionJs = statement.EvaluateStatementJsValue(executionEnvironment, context);
                var shouldStop = context.ShouldStopEvaluation;

                if (!completionJs.IsUnit)
                {
                    resultJs = completionJs;
                }
                else if (ShouldResetScriptCompletion(statement))
                {
                    // Compound statements with empty completion reset the script completion to undefined
                    resultJs = JsValue.Undefined;
                }

                // Abrupt completions should yield undefined as the script result (per spec test cases)
                if (context.IsBreak || context.IsContinue)
                {
                    resultJs = JsValue.Undefined;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return resultJs;
        }

        private static bool ShouldResetScriptCompletion(StatementNode statement)
        {
            return statement switch
            {
                IfStatement => true,
                SwitchStatement => true,
                TryStatement => true,
                ForStatement => true,
                ForEachStatement => true,
                WhileStatement => true,
                DoWhileStatement => true,
                LabeledStatement => true,
                _ => false
            };
        }
    }
}
