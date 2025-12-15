using System.Collections.Immutable;
using System.Diagnostics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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
        var context = realmState.CreateContext(
            ScopeKind.Program,
            program.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
            cancellationToken,
            executionKind);
        context.AllowIdentifierCache = AllowsIdentifierCaching(program);
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
        using var programActivity =
            Activity.Current?.StartEvaluatorActivity("Program", context, program.Source);
        programActivity?.SetTag("js.program.strict", program.IsStrict);
            var executionEnvironment = program.IsStrict && createStrictEnvironment
                ? new JsEnvironment(environment, true, true,
                    treatAsGlobalFunctionScope: environment.IsGlobalFunctionScope)
                : environment;
            if (program.IsStrict && !executionEnvironment.IsStrict)
            {
                executionEnvironment = new JsEnvironment(executionEnvironment, true, true,
                    treatAsGlobalFunctionScope: executionEnvironment.IsGlobalFunctionScope);
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
            var lexicalNames = CollectLexicalNames(programBlock);
            var topLevelLexicalNames = CollectTopLevelLexicalNames(program.Body);
            var catchParameterNames = CollectCatchParameterNames(programBlock);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(programBlock);
            // For bodyLexicalNames used in global/var conflict checks, we only use TOP-LEVEL names.
            // Per ES spec GlobalDeclarationInstantiation, var declarations only conflict with
            // top-level lexical declarations, not with block-scoped let/const in nested blocks.
            var bodyLexicalNames = topLevelLexicalNames.Count == 0
                ? topLevelLexicalNames
                : new HashSet<Symbol>(topLevelLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            var functionScope = executionEnvironment.GetFunctionScope();
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
            foreach (var stmt in program.Body)
            {
                switch (stmt)
                {
                    case VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } lexDecl:
                        var isConst = lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                        foreach (var declarator in lexDecl.Declarators)
                        {
                            HoistLexicalBindingTargetForGlobalTdz(declarator.Target, executionEnvironment, isConst);
                        }
                        break;
                    case ClassDeclaration classDecl:
                        // Class declarations are also lexically scoped and need TDZ
                        if (!executionEnvironment.HasBinding(classDecl.Name))
                        {
                            executionEnvironment.Define(classDecl.Name, JsEnvironment.Uninitialized, isLexical: true,
                                blocksFunctionScopeOverride: true, isConst: true);
                        }
                        break;
                }
            }

            HoistVarDeclarations(
                programBlock,
                executionEnvironment,
                context,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            var resultJs = JsValue.Undefined;
            var hasResult = false;
            foreach (var statement in program.Body)
            {
                context.ThrowIfCancellationRequested();
                var completionJs = EvaluateStatementJsValue(statement, executionEnvironment, context);
                var shouldStop = context.ShouldStopEvaluation;
                var shouldCapture =
                    !shouldStop ||
                    context.IsReturn ||
                    context.IsThrow ||
                    context.IsYield ||
                    context.IsBreak ||
                    context.IsContinue;

                if (shouldCapture)
                {
                    resultJs = completionJs;
                    hasResult = true;
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

            return hasResult ? resultJs.ToObject() : Symbol.Undefined;
        }
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
        CollectVarNamesFromStatements(statements, names, isStrict, inBlockScope: false);
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
                    CollectVarNamesFromStatement(ifStatement.Then, names, isStrict, inBlockScope: true);
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

    private static void HoistLexicalBindingTargetForGlobalTdz(BindingTarget target, JsEnvironment environment, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!environment.HasBinding(id.Name))
                    {
                        environment.Define(id.Name, JsEnvironment.Uninitialized, isLexical: true, blocksFunctionScopeOverride: true, isConst: isConst);
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
}
