using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine;

/// <summary>
///     A special host function for eval() that has access to the calling environment
///     and can evaluate code synchronously in that context.
/// </summary>
public sealed class EvalHostFunction : IJsEnvironmentAwareCallable, IEvaluationContextAwareCallable, IJsPropertyAccessor
{
    internal static readonly Symbol FieldInitializerEvalFlag = Symbol.Intern("#classFieldInitializerEval");
    private readonly JsEngine _engine;
    private readonly JsObject _properties = new();

    public EvalHostFunction(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _properties.SetProperty("prototype", new JsObject());
    }

    internal JsEngine Engine => _engine;

    public EvaluationContext? CallingContext { get; set; }

    /// <summary>
    ///     The environment that is calling this function.
    ///     This allows eval to execute code in the caller's scope.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    internal bool InClassFieldInitializer { get; set; }

    internal bool IsDirectCall { get; set; }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        if (arguments.Count == 0 || arguments[0] is not string code)
        {
            return arguments.Count > 0 ? arguments[0] : Symbol.Undefined;
        }

        var isDirectEval = IsDirectCall;
        IsDirectCall = false;

        // Direct eval executes in the caller's scope; indirect eval always uses the realm's global scope.
        var evalRealmGlobal = _engine.GlobalExecutionScope ?? _engine.GlobalEnvironment;
        var environment = isDirectEval
            ? CallingJsEnvironment ?? throw new InvalidOperationException("eval() called without a calling environment")
            : evalRealmGlobal;

        var forceStrict = isDirectEval && (CallingContext?.CurrentScope.IsStrict ?? false);

        // Parse the code and build the typed AST so eval shares the same pipeline
        ParsedProgram program;
        try
        {
            program = _engine.ParseForExecution(code, forceStrict);
        }
        catch (ParseException parseException)
        {
            var message = parseException.Message;
            object? errorObject = message;
            if (!environment.TryGet(Symbol.SyntaxErrorIdentifier, out var ctor) ||
                ctor is not IJsCallable callable)
            {
                throw new ThrowSignal(errorObject);
            }

            try
            {
                errorObject = callable.Invoke([message], null);
            }
            catch (ThrowSignal signal)
            {
                errorObject = signal.ThrownValue;
            }

            throw new ThrowSignal(errorObject);
        }

        // Scripts evaluated via eval may not contain module syntax (export/import).
        if (program.Typed.Body.Any(statement => statement is ModuleStatement))
        {
            throw StandardLibrary.ThrowSyntaxError(
                "Cannot use module declarations within eval code.",
                CallingContext,
                environment.RealmState);
        }

        var insideClassFieldInitializer = InClassFieldInitializer ||
                                          CallingContext?.InClassFieldInitializer == true ||
                                          (CallingJsEnvironment?.HasBinding(FieldInitializerEvalFlag) ?? false);
        var containsSuperCallInInitializer = ContainsSuperCall(program.Typed.Body, includeFunctionBodies: true);
        var containsSuperReferenceInInitializer = ContainsSuperReference(program.Typed.Body, includeFunctionBodies: true);
        var containsArgumentsInInitializer = insideClassFieldInitializer &&
                                             ContainsArguments(program.Typed.Body, includeFunctionBodies: true);
        if (insideClassFieldInitializer && containsSuperCallInInitializer)
        {
            throw StandardLibrary.ThrowSyntaxError(
                "super calls are not allowed in eval inside class field initializers.",
                CallingContext,
                environment.RealmState);
        }

        if (insideClassFieldInitializer && !isDirectEval && containsSuperReferenceInInitializer)
        {
            throw StandardLibrary.ThrowSyntaxError(
                "super references are not allowed in eval inside class field initializers.",
                CallingContext,
                environment.RealmState);
        }

        if (isDirectEval && containsArgumentsInInitializer)
        {
            throw StandardLibrary.ThrowSyntaxError(
                "'arguments' is not allowed in eval inside class field initializers.",
                CallingContext,
                environment.RealmState);
        }

        if (!isDirectEval && ContainsNewTarget(program.Typed.Body, includeFunctionBodies: true))
        {
            throw StandardLibrary.ThrowSyntaxError(
                "new.target is not allowed in indirect eval code.",
                CallingContext,
                environment.RealmState);
        }

        if (!isDirectEval && ContainsSuperReference(program.Typed.Body))
        {
            throw StandardLibrary.ThrowSyntaxError(
                "super references are not allowed in indirect eval code.",
                CallingContext,
                environment.RealmState);
        }

        if (isDirectEval && ContainsNewTarget(program.Typed.Body))
        {
            var callerFunctionScope = CallingJsEnvironment?.GetFunctionScope();
            var callerHasNewTarget = callerFunctionScope?.HasOwnBinding(Symbol.NewTarget) == true;
            if (!callerHasNewTarget)
            {
                throw StandardLibrary.ThrowSyntaxError(
                    "new.target is not allowed in this direct eval context.",
                    CallingContext,
                    environment.RealmState);
            }
        }

        if (isDirectEval)
        {
            var hasSuperBinding = CallingJsEnvironment?.TryGet(Symbol.Super, out _) == true;
            var hasNewTarget = CallingJsEnvironment?.TryGet(Symbol.NewTarget, out var newTarget) == true &&
                               !ReferenceEquals(newTarget, Symbol.Undefined);

            if (!hasSuperBinding && ContainsSuperReference(program.Typed.Body))
            {
                throw StandardLibrary.ThrowSyntaxError(
                    "super references are not allowed in direct eval outside methods.",
                    CallingContext,
                    environment.RealmState);
            }

            if (!hasNewTarget && ContainsSuperCall(program.Typed.Body))
            {
                throw StandardLibrary.ThrowSyntaxError(
                    "super calls are only allowed in direct eval when evaluating constructors.",
                    CallingContext,
                    environment.RealmState);
            }
        }

        if (ContainsIllegalReturn(program.Typed.Body) || ContainsIllegalBreakOrContinue(program.Typed.Body))
        {
            throw StandardLibrary.ThrowSyntaxError(
                "Illegal control flow statement in eval code.",
                CallingContext,
                environment.RealmState);
        }

        // ES2022: AllPrivateNamesValid static semantic check for eval code.
        // For direct eval, private names from enclosing class scopes are available.
        // For indirect eval, no private names are available.
        // Any private name reference not found in available scopes is a SyntaxError.
        ImmutableArray<PrivateNameScope>? evalPrivateNameScopes = null;
        if (isDirectEval && CallingContext is not null)
        {
            var capturedScopes = CallingContext.CapturePrivateNameScopes();
            Console.Error.WriteLine($"[DEBUG EvalHostFunction] Direct eval: capturedScopes.Length = {(capturedScopes.IsDefaultOrEmpty ? 0 : capturedScopes.Length)}, CurrentPrivateNameScope = {CallingContext.CurrentPrivateNameScope?.GetType().Name ?? "null"}");
            if (!capturedScopes.IsDefaultOrEmpty)
            {
                evalPrivateNameScopes = capturedScopes;
            }
            else if (CallingContext.CurrentPrivateNameScope is not null)
            {
                evalPrivateNameScopes = ImmutableArray.Create(CallingContext.CurrentPrivateNameScope);
            }
            Console.Error.WriteLine($"[DEBUG EvalHostFunction] evalPrivateNameScopes has value: {evalPrivateNameScopes.HasValue}, length: {(evalPrivateNameScopes?.Length ?? 0)}");
        }

        var invalidPrivateName = FindInvalidPrivateName(program.Typed.Body, evalPrivateNameScopes);
        Console.Error.WriteLine($"[DEBUG EvalHostFunction] FindInvalidPrivateName result: {invalidPrivateName ?? "(none)"}");
        if (evalPrivateNameScopes.HasValue)
        {
            foreach (var scope in evalPrivateNameScopes.Value)
            {
                Console.Error.WriteLine($"[DEBUG EvalHostFunction] Scope has key 'm': {scope.TryGetKey("m", out var key)}, key = {key ?? "(null)"}");
            }
        }
        if (invalidPrivateName is not null)
        {
            throw StandardLibrary.ThrowSyntaxError(
                $"Private field '{invalidPrivateName}' must be declared in an enclosing class",
                CallingContext,
                environment.RealmState);
        }

        var isStrictEval = program.Typed.IsStrict;
        JsEnvironment lexicalEnv;
        if (!isDirectEval)
        {
            // Indirect eval runs with a fresh lexical environment whose outer is the global
            // environment record (ES 18.2.1.1 EvalDeclarationInstantiation). In strict mode
            // the variable environment is that new declarative scope as well, so top-level
            // var/function declarations do not leak into the caller or global scope.
            var indirectLexical = new JsEnvironment(environment, isStrictEval, isStrictEval,
                description: "indirect eval", inheritStrictness: false);
            if (!isStrictEval)
            {
                indirectLexical.SetVarEnvironment(environment.GetVarEnvironment());
            }

            lexicalEnv = indirectLexical;
        }
        else if (isStrictEval)
        {
            // Strict direct eval: fresh declarative environment for both lexical and var bindings
            // (PerformEval step 9.a).
            lexicalEnv = new JsEnvironment(
                environment,
                isFunctionScope: true,
                true,
                description: "strict direct eval",
                treatAsGlobalFunctionScope: false,
                inheritStrictness: false);
        }
        else
        {
            // Sloppy direct eval: fresh lexical environment whose outer is the caller, but var
            // declarations still target the caller's var environment (EvalDeclarationInstantiation step 8).
            lexicalEnv = new JsEnvironment(
                environment,
                isFunctionScope: false,
                isStrict: false,
                description: "direct eval lexical",
                inheritStrictness: false);
        }

        var varEnv = isDirectEval
            ? isStrictEval
                ? lexicalEnv
                : environment.GetVarEnvironment()
            : isStrictEval
                ? lexicalEnv
                : lexicalEnv.GetVarEnvironment();

        // 18.2.1.1 EvalDeclarationInstantiation: non-strict direct eval must
        // reject var declarations that collide with caller lexicals (including parameters).
        var varDeclaredNames = new HashSet<Symbol>();
        CollectVarDeclaredNames(program.Typed.Body, varDeclaredNames);
        var lexicallyDeclaredNames = CollectLexicallyDeclaredNames(program.Typed.Body);
        var lexicalDeclarations = CollectLexicalDeclarations(program.Typed.Body);
        var varFunctionDeclarations = CollectVarFunctionDeclarations(program.Typed.Body);
        if (!isStrictEval)
        {
            foreach (var name in varDeclaredNames)
            {
                if (isDirectEval &&
                    varEnv.IsParameterEnvironment &&
                    varEnv.HasOwnBinding(name))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare var-scoped binding '{name.Name}' in direct eval due to existing parameter binding.",
                        CallingContext,
                        environment.RealmState);
                }

                // In sloppy direct eval, a parameter named `arguments` (or any matching
                // parameter binding) blocks new var/function declarations of the same name
                // (EvalDeclarationInstantiation step 5.d). Only enforce when the parameter
                // environment actually has that binding; otherwise allow the eval hoist.
                if (isDirectEval &&
                    varEnv.IsParameterEnvironment &&
                    ReferenceEquals(name, Symbol.Arguments) &&
                    varEnv.HasOwnBinding(Symbol.Arguments))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare var-scoped binding '{name.Name}' in direct eval due to existing parameter binding.",
                        CallingContext,
                        environment.RealmState);
                }

                var hasGlobalLexical = varEnv.IsGlobalFunctionScope &&
                                       (varEnv.HasOwnLexicalBinding(name) || varEnv.HasBodyLexicalName(name));
                // EvalDeclarationInstantiation (18.2.1.3, step 5.d) rejects var names
                // that collide with existing lexical bindings on the path to the var
                // environment, except for simple catch parameters (Annex B.3.3.3).
                if (HasDeclarativeBindingBetween(lexicalEnv, varEnv, name) || hasGlobalLexical)
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare var-scoped binding '{name.Name}' in direct eval due to existing lexical declaration.",
                        CallingContext,
                        environment.RealmState);
                }
            }
        }

        // Annex B / EvalDeclarationInstantiation: in non-strict direct eval, declaring
        // `arguments` via var/function inside parameter initializers of functions with
        // default parameters is a SyntaxError when an `arguments` binding already exists
        // in the caller's variable environment (ES 18.2.1.1, steps 5.d+).
        // EvalDeclarationInstantiation step 7+8: lexical declarations must not
        // conflict with existing var/lexical bindings in the variable environment.
        if (isDirectEval)
        {
            foreach (var name in lexicallyDeclaredNames)
            {
                var hasVarBinding = varEnv.HasFunctionScopedBinding(name);
                if (hasVarBinding)
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare lexical binding '{name.Name}' in direct eval because a var binding already exists.",
                        CallingContext,
                        environment.RealmState);
                }

                if (varEnv.IsGlobalFunctionScope && varEnv.HasRestrictedGlobalProperty(name))
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare lexical binding '{name.Name}' in direct eval due to non-configurable global.",
                        CallingContext,
                        environment.RealmState);
                }
            }
        }

        if (!isStrictEval && varEnv.IsGlobalFunctionScope)
        {
            var declaredFunctionNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            for (var i = varFunctionDeclarations.Count - 1; i >= 0; i--)
            {
                var declaration = varFunctionDeclarations[i];
                if (declaration.Function.Name is null || !declaredFunctionNames.Add(declaration.Function.Name))
                {
                    continue;
                }

                if (!CanDeclareGlobalFunction(varEnv, declaration.Function.Name))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot redeclare non-configurable global function",
                        CallingContext,
                        environment.RealmState);
                }
            }
        }

        var evalEnvironment = isStrictEval
            ? new JsEnvironment(
                lexicalEnv,
                false,
                true,
                description: "eval",
                treatAsGlobalFunctionScope: false,
                inheritStrictness: !isDirectEval)
            : lexicalEnv;

        InstantiateLexicalDeclarations(evalEnvironment, lexicalDeclarations);

        var preexistingVarBindings = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var name in varDeclaredNames)
        {
            if (varEnv.HasBinding(name))
            {
                preexistingVarBindings.Add(name);
            }
        }

        try
        {
            // Evaluate directly in the constructed eval environment (direct eval is synchronous).
            // Note: evalPrivateNameScopes was captured earlier for AllPrivateNamesValid validation.
            if (evalPrivateNameScopes.HasValue && !evalPrivateNameScopes.Value.IsDefaultOrEmpty && CallingContext is not null)
            {
                CallingContext.RealmState.Logger?.LogInformation(
                    "Eval direct: captured {PrivateScopeCount} private scopes (class initializer={InInitializer})",
                    evalPrivateNameScopes.Value.Length,
                    insideClassFieldInitializer);
            }
            var result = program.Typed.EvaluateProgram(evalEnvironment, _engine.RealmState, CancellationToken.None,
                ExecutionKind.Eval, createStrictEnvironment: false, inheritedPrivateNameScopes: evalPrivateNameScopes);

            return result;
        }
        catch (ThrowSignal)
        {
            RollbackEvalBindings(varEnv, varDeclaredNames, preexistingVarBindings);
            throw;
        }
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return _properties.TryGetProperty(name, receiver ?? this, out value);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        _properties.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    private static void CollectVarDeclaredNames(ImmutableArray<StatementNode> statements, HashSet<Symbol> names)
    {
        foreach (var statement in statements)
        {
            CollectVarDeclaredNamesFromStatement(statement, names);
        }
    }

    private static void CollectVarDeclaredNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        CollectBindingNames(declarator.Target, names);
                    }

                    break;
                case FunctionDeclaration { Function.Name: not null } funcDecl:
                    names.Add(funcDecl.Function.Name);
                    break;
                case BlockStatement block:
                    CollectVarDeclaredNames(block.Statements, names);
                    break;
                case ForStatement { Initializer: VariableDeclaration { Kind: VariableKind.Var } initDecl } forStatement:
                {
                    foreach (var declarator in initDecl.Declarators)
                    {
                        CollectBindingNames(declarator.Target, names);
                    }

                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    break;
                }
                case ForEachStatement { DeclarationKind: VariableKind.Var } forEach:
                    CollectBindingNames(forEach.Target, names);
                    statement = forEach.Body;
                    continue;
            }

            break;
        }
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

    private static HashSet<Symbol> CollectLexicallyDeclaredNames(ImmutableArray<StatementNode> statements)
    {
        var names = new HashSet<Symbol>();
        foreach (var statement in statements)
        {
            CollectLexicallyDeclaredNamesFromStatement(statement, names);
        }

        return names;
    }

    private static List<FunctionDeclaration> CollectVarFunctionDeclarations(ImmutableArray<StatementNode> statements)
    {
        var functions = new List<FunctionDeclaration>();
        foreach (var statement in statements)
        {
            CollectVarFunctionsFromStatement(statement, functions);
        }

        return functions;
    }

    private static void CollectLexicallyDeclaredNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectLexicallyDeclaredNamesFromStatement(inner, names);
                    }

                    break;
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } decl:
                    foreach (var declarator in decl.Declarators)
                    {
                        CollectBindingNames(declarator.Target, names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
                case FunctionDeclaration:
                    // Function declarations are handled as var-scoped in eval code.
                    break;
                case IfStatement ifStatement:
                    CollectLexicallyDeclaredNamesFromStatement(ifStatement.Then, names);
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
                        } initDecl)
                    {
                        foreach (var declarator in initDecl.Declarators)
                        {
                            CollectBindingNames(declarator.Target, names);
                        }
                    }

                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    break;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const)
                    {
                        CollectBindingNames(forEachStatement.Target, names);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectLexicallyDeclaredNamesFromStatement(switchCase.Body, names);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectLexicallyDeclaredNamesFromStatement(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is not null)
                        {
                            CollectBindingNames(catchClause.Binding, names);
                        }
                        CollectLexicallyDeclaredNamesFromStatement(catchClause.Body, names);
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

    private static void CollectVarFunctionsFromStatement(StatementNode statement, List<FunctionDeclaration> functions)
    {
        while (true)
        {
            switch (statement)
            {
                case FunctionDeclaration functionDeclaration:
                    functions.Add(functionDeclaration);
                    break;
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectVarFunctionsFromStatement(inner, functions);
                    }

                    break;
                case IfStatement ifStatement:
                    CollectVarFunctionsFromStatement(ifStatement.Then, functions);
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
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initDecl)
                    {
                        CollectVarFunctionsFromStatement(initDecl, functions);
                    }

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
                        CollectVarFunctionsFromStatement(switchCase.Body, functions);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectVarFunctionsFromStatement(tryStatement.TryBlock, functions);
                    if (tryStatement.Catch is { Body: not null } catchClause)
                    {
                        CollectVarFunctionsFromStatement(catchClause.Body, functions);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
            }

            break;
        }
    }

    private static bool HasDeclarativeBindingBetween(JsEnvironment lexicalEnv, JsEnvironment varEnv, Symbol name)
    {
        var current = lexicalEnv;
        while (current is not null && !ReferenceEquals(current, varEnv))
        {
            if (current.IsObjectEnvironment)
            {
                current = current.Enclosing;
                continue;
            }

            if (current.IsSimpleCatchParameter(name))
            {
                current = current.Enclosing;
                continue;
            }

            if (current.HasOwnBinding(name))
            {
                return true;
            }

            if (current.HasOwnLexicalBinding(name))
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    private static bool ContainsIllegalBreakOrContinue(ImmutableArray<StatementNode> statements)
    {
        var labelStack = new Stack<(Symbol Label, LabelTargetKind Kind)>();
        foreach (var statement in statements)
        {
            if (ContainsIllegalBreakOrContinue(statement, labelStack, 0, 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIllegalBreakOrContinue(
        StatementNode statement,
        Stack<(Symbol Label, LabelTargetKind Kind)> labels,
        int iterationDepth,
        int switchDepth)
    {
        while (true)
        {
            switch (statement)
            {
                case BreakStatement breakStatement:
                    if (breakStatement.Label is null)
                    {
                        return iterationDepth == 0 && switchDepth == 0;
                    }

                    return !TryResolveLabel(labels, breakStatement.Label, requireIteration: false);
                case ContinueStatement continueStatement:
                    if (continueStatement.Label is null)
                    {
                        return iterationDepth == 0;
                    }

                    return !TryResolveLabel(labels, continueStatement.Label, requireIteration: true);
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (ContainsIllegalBreakOrContinue(inner, labels, iterationDepth, switchDepth))
                        {
                            return true;
                        }
                    }

                    return false;
                case IfStatement ifStatement:
                    return ContainsIllegalBreakOrContinue(ifStatement.Then, labels, iterationDepth, switchDepth) ||
                           (ifStatement.Else is not null &&
                            ContainsIllegalBreakOrContinue(ifStatement.Else, labels, iterationDepth, switchDepth));
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    iterationDepth++;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    iterationDepth++;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Body is null)
                    {
                        return false;
                    }

                    statement = forStatement.Body;
                    iterationDepth++;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    iterationDepth++;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var @case in switchStatement.Cases)
                    {
                        if (ContainsIllegalBreakOrContinue(@case.Body, labels, iterationDepth, switchDepth + 1))
                        {
                            return true;
                        }
                    }

                    return false;
                case TryStatement tryStatement:
                    if (ContainsIllegalBreakOrContinue(tryStatement.TryBlock, labels, iterationDepth, switchDepth))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        ContainsIllegalBreakOrContinue(catchClause.Body, labels, iterationDepth, switchDepth))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null &&
                        ContainsIllegalBreakOrContinue(tryStatement.Finally, labels, iterationDepth, switchDepth))
                    {
                        return true;
                    }

                    return false;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case LabeledStatement labeledStatement:
                    var targetKind = GetLabelTargetKind(labeledStatement.Statement);
                    labels.Push((labeledStatement.Label, targetKind));
                    var result = ContainsIllegalBreakOrContinue(
                        labeledStatement.Statement,
                        labels,
                        iterationDepth,
                        switchDepth);
                    labels.Pop();
                    return result;
                case FunctionDeclaration:
                case ClassDeclaration:
                    // Function/class bodies handle their own control flow rules.
                    return false;
                default:
                    return false;
            }
        }
    }

    private static LabelTargetKind GetLabelTargetKind(StatementNode statement)
    {
        return statement switch
        {
            WhileStatement => LabelTargetKind.Iteration,
            DoWhileStatement => LabelTargetKind.Iteration,
            ForStatement => LabelTargetKind.Iteration,
            ForEachStatement => LabelTargetKind.Iteration,
            SwitchStatement => LabelTargetKind.Switch,
            _ => LabelTargetKind.Other
        };
    }

    private static bool TryResolveLabel(
        Stack<(Symbol Label, LabelTargetKind Kind)> labels,
        Symbol target,
        bool requireIteration)
    {
        foreach (var (label, kind) in labels)
        {
            if (!ReferenceEquals(label, target))
            {
                continue;
            }

            if (!requireIteration)
            {
                return true;
            }

            return kind == LabelTargetKind.Iteration;
        }

        return false;
    }

    private enum LabelTargetKind
    {
        Other,
        Iteration,
        Switch
    }

    private static bool ContainsIllegalReturn(ImmutableArray<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            if (IsIllegalReturn(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanDeclareGlobalFunction(JsEnvironment varEnv, Symbol name)
    {
        var descriptor = varEnv.GetGlobalOwnPropertyDescriptor(name, out var globalObject);
        if (globalObject is null)
        {
            return true;
        }

        return descriptor switch
        {
            null => globalObject.IsExtensible,
            { Configurable: true } => true,
            _ => !descriptor.IsAccessorDescriptor &&
                 descriptor.Writable &&
                 descriptor.Enumerable
        };
    }

    private static bool IsIllegalReturn(StatementNode statement)
    {
        while (true)
        {
            switch (statement)
            {
                case ReturnStatement:
                    return true;
                case BlockStatement block:
                    return block.Statements.Any(IsIllegalReturn);
                case IfStatement ifStatement:
                    return IsIllegalReturn(ifStatement.Then) || (ifStatement.Else is not null && IsIllegalReturn(ifStatement.Else));
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    continue;
                case ForStatement forStatement:
                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    return switchStatement.Cases.Any(c => IsIllegalReturn(c.Body));
                case TryStatement tryStatement:
                    return IsIllegalReturn(tryStatement.TryBlock) || (tryStatement.Catch is { Body: not null } catchClause && IsIllegalReturn(catchClause.Body)) || (tryStatement.Finally is not null && IsIllegalReturn(tryStatement.Finally));
                // Function/class declarations create their own return-valid scope.
                default:
                    return false;
            }

            break;
        }
    }

    private static bool ContainsNewTarget(
        ImmutableArray<StatementNode> statements,
        bool includeFunctionBodies = false)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsNewTarget(statement, includeFunctionBodies))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSuperReference(
        ImmutableArray<StatementNode> statements,
        bool includeFunctionBodies = false)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsSuper(statement, includeFunctionBodies))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSuperCall(
        ImmutableArray<StatementNode> statements,
        bool includeFunctionBodies = false)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsSuperCall(statement, includeFunctionBodies))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsArguments(
        ImmutableArray<StatementNode> statements,
        bool includeFunctionBodies = false)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsArguments(statement, includeFunctionBodies))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementContainsNewTarget(StatementNode statement, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return ExpressionContainsNewTarget(expressionStatement.Expression, includeFunctionBodies);
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    return ExpressionContainsNewTarget(returnStatement.Expression, includeFunctionBodies);
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ExpressionContainsNewTarget(declarator.Initializer, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (StatementContainsNewTarget(inner, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case IfStatement ifStatement:
                    return StatementContainsNewTarget(ifStatement.Then, includeFunctionBodies) ||
                           (ifStatement.Else is not null && StatementContainsNewTarget(ifStatement.Else, includeFunctionBodies));
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
                    if (forStatement.Initializer is ExpressionStatement initExpression &&
                        ExpressionContainsNewTarget(initExpression.Expression, includeFunctionBodies))
                    {
                        return true;
                    }
                    if (forStatement.Initializer is VariableDeclaration initVarDecl &&
                        StatementContainsNewTarget(initVarDecl, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Condition is not null &&
                        ExpressionContainsNewTarget(forStatement.Condition, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ExpressionContainsNewTarget(forStatement.Increment, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (ExpressionContainsNewTarget(forEachStatement.Iterable, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (StatementContainsNewTarget(switchCase.Body, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TryStatement tryStatement:
                    if (StatementContainsNewTarget(tryStatement.TryBlock, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        StatementContainsNewTarget(catchClause.Body, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    return false;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case FunctionDeclaration functionDeclaration when includeFunctionBodies:
                    return ContainsNewTarget(functionDeclaration.Function.Body.Statements, true);
                case FunctionDeclaration:
                case ClassDeclaration:
                    // new.target is allowed inside function/class bodies; skip nested scopes.
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool StatementContainsSuperCall(StatementNode statement, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return ExpressionContainsSuperCall(expressionStatement.Expression, includeFunctionBodies);
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (StatementContainsSuperCall(inner, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case IfStatement ifStatement:
                    return StatementContainsSuperCall(ifStatement.Then, includeFunctionBodies) ||
                           (ifStatement.Else is not null &&
                            StatementContainsSuperCall(ifStatement.Else, includeFunctionBodies));
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
                    if (forStatement.Initializer is ExpressionStatement initExpression &&
                        ExpressionContainsSuperCall(initExpression.Expression, includeFunctionBodies))
                    {
                        return true;
                    }
                    if (forStatement.Initializer is VariableDeclaration initVarDecl &&
                        StatementContainsSuperCall(initVarDecl, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Condition is not null &&
                        ExpressionContainsSuperCall(forStatement.Condition, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ExpressionContainsSuperCall(forStatement.Increment, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (ExpressionContainsSuperCall(forEachStatement.Iterable, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (StatementContainsSuperCall(switchCase.Body, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TryStatement tryStatement:
                    if (StatementContainsSuperCall(tryStatement.TryBlock, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        StatementContainsSuperCall(catchClause.Body, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    return false;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    return ExpressionContainsSuperCall(returnStatement.Expression, includeFunctionBodies);
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ExpressionContainsSuperCall(declarator.Initializer, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionDeclaration functionDeclaration when includeFunctionBodies:
                    return ContainsSuperCall(functionDeclaration.Function.Body.Statements, true);
                case FunctionDeclaration:
                case ClassDeclaration:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool StatementContainsSuper(StatementNode statement, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return ExpressionContainsSuper(expressionStatement.Expression, includeFunctionBodies);
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (StatementContainsSuper(inner, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case IfStatement ifStatement:
                    return StatementContainsSuper(ifStatement.Then, includeFunctionBodies) ||
                           (ifStatement.Else is not null &&
                            StatementContainsSuper(ifStatement.Else, includeFunctionBodies));
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
                    if (forStatement.Initializer is ExpressionStatement initExpression &&
                        ExpressionContainsSuper(initExpression.Expression, includeFunctionBodies))
                    {
                        return true;
                    }
                    if (forStatement.Initializer is VariableDeclaration initVarDecl &&
                        StatementContainsSuper(initVarDecl, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Condition is not null &&
                        ExpressionContainsSuper(forStatement.Condition, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ExpressionContainsSuper(forStatement.Increment, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (ExpressionContainsSuper(forEachStatement.Iterable, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (StatementContainsSuper(switchCase.Body, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TryStatement tryStatement:
                    if (StatementContainsSuper(tryStatement.TryBlock, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        StatementContainsSuper(catchClause.Body, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    return false;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    return ExpressionContainsSuper(returnStatement.Expression, includeFunctionBodies);
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ExpressionContainsSuper(declarator.Initializer, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionDeclaration functionDeclaration when includeFunctionBodies:
                    return ContainsSuperReference(functionDeclaration.Function.Body.Statements, true);
                case FunctionDeclaration:
                case ClassDeclaration:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool StatementContainsArguments(StatementNode statement, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return ExpressionContainsArguments(expressionStatement.Expression, includeFunctionBodies);
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        if (StatementContainsArguments(inner, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case IfStatement ifStatement:
                    return StatementContainsArguments(ifStatement.Then, includeFunctionBodies) ||
                           (ifStatement.Else is not null &&
                            StatementContainsArguments(ifStatement.Else, includeFunctionBodies));
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
                    if (forStatement.Initializer is ExpressionStatement initExpression &&
                        ExpressionContainsArguments(initExpression.Expression, includeFunctionBodies))
                    {
                        return true;
                    }
                    if (forStatement.Initializer is VariableDeclaration initVarDecl &&
                        StatementContainsArguments(initVarDecl, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Condition is not null &&
                        ExpressionContainsArguments(forStatement.Condition, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (forStatement.Increment is not null &&
                        ExpressionContainsArguments(forStatement.Increment, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (ExpressionContainsArguments(forEachStatement.Iterable, includeFunctionBodies))
                    {
                        return true;
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (StatementContainsArguments(switchCase.Body, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TryStatement tryStatement:
                    if (StatementContainsArguments(tryStatement.TryBlock, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause &&
                        StatementContainsArguments(catchClause.Body, includeFunctionBodies))
                    {
                        return true;
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    return false;
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    return ExpressionContainsArguments(returnStatement.Expression, includeFunctionBodies);
                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null &&
                            ExpressionContainsArguments(declarator.Initializer, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionDeclaration functionDeclaration when includeFunctionBodies:
                    return ContainsArguments(functionDeclaration.Function.Body.Statements, true);
                case FunctionDeclaration:
                case ClassDeclaration:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool ExpressionContainsNewTarget(ExpressionNode expression, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (expression)
            {
                case NewTargetExpression:
                    return true;
                case BinaryExpression binary:
                    return ExpressionContainsNewTarget(binary.Left, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(binary.Right, includeFunctionBodies);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ConditionalExpression conditional:
                    return ExpressionContainsNewTarget(conditional.Test, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(conditional.Consequent, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(conditional.Alternate, includeFunctionBodies);
                case CallExpression call:
                    if (ExpressionContainsNewTarget(call.Callee, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ExpressionContainsNewTarget(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression newExpression:
                    if (ExpressionContainsNewTarget(newExpression.Constructor, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        if (ExpressionContainsNewTarget(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    return ExpressionContainsNewTarget(member.Target, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(member.Property, includeFunctionBodies);
                case AssignmentExpression assignment:
                    return ExpressionContainsNewTarget(assignment.Value, includeFunctionBodies);
                case PropertyAssignmentExpression propertyAssignment:
                    return ExpressionContainsNewTarget(propertyAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(propertyAssignment.Property, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(propertyAssignment.Value, includeFunctionBodies);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsNewTarget(indexAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(indexAssignment.Index, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(indexAssignment.Value, includeFunctionBodies);
                case SequenceExpression sequence:
                    return ExpressionContainsNewTarget(sequence.Left, includeFunctionBodies) ||
                           ExpressionContainsNewTarget(sequence.Right, includeFunctionBodies);
                case DestructuringAssignmentExpression destructuringAssignment:
                    return ExpressionContainsNewTarget(destructuringAssignment.Value, includeFunctionBodies);
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null &&
                            ExpressionContainsNewTarget(element.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey &&
                            ExpressionContainsNewTarget(computedKey, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Kind == ObjectMemberKind.Spread && member.Value is not null &&
                            ExpressionContainsNewTarget(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Function is not null || member.Kind is ObjectMemberKind.Method
                            or ObjectMemberKind.Getter or ObjectMemberKind.Setter)
                        {
                            if (member.IsComputed && member.Value is not null &&
                                ExpressionContainsNewTarget(member.Value, includeFunctionBodies))
                            {
                                return true;
                            }

                            if (includeFunctionBodies && member.Function is not null)
                            {
                                foreach (var parameter in member.Function.Parameters)
                                {
                                    if (parameter.DefaultValue is not null &&
                                        ExpressionContainsNewTarget(parameter.DefaultValue, true))
                                    {
                                        return true;
                                    }
                                }

                                if (ContainsNewTarget(member.Function.Body.Statements, true))
                                {
                                    return true;
                                }
                            }

                            continue;
                        }

                        if (member.Value is not null &&
                            ExpressionContainsNewTarget(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null &&
                            ExpressionContainsNewTarget(part.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression taggedTemplate:
                    if (ExpressionContainsNewTarget(taggedTemplate.Tag, includeFunctionBodies) ||
                        ExpressionContainsNewTarget(taggedTemplate.StringsArray, includeFunctionBodies) ||
                        ExpressionContainsNewTarget(taggedTemplate.RawStringsArray, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        if (ExpressionContainsNewTarget(expr, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case YieldExpression yieldExpression when yieldExpression.Expression is not null:
                    expression = yieldExpression.Expression;
                    continue;
                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case FunctionExpression functionExpression when includeFunctionBodies:
                    foreach (var parameter in functionExpression.Parameters)
                    {
                        if (parameter.DefaultValue is not null &&
                            ExpressionContainsNewTarget(parameter.DefaultValue, true))
                        {
                            return true;
                        }
                    }

                    return ContainsNewTarget(functionExpression.Body.Statements, true);
                case ClassExpression:
                case FunctionExpression:
                    // new.target is permitted inside function/class bodies.
                    return false;
                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool ExpressionContainsArguments(ExpressionNode expression, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierExpression identifier when identifier.Name.Name == "arguments":
                    return true;
                case BinaryExpression binary:
                    return ExpressionContainsArguments(binary.Left, includeFunctionBodies) ||
                           ExpressionContainsArguments(binary.Right, includeFunctionBodies);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ConditionalExpression conditional:
                    return ExpressionContainsArguments(conditional.Test, includeFunctionBodies) ||
                           ExpressionContainsArguments(conditional.Consequent, includeFunctionBodies) ||
                           ExpressionContainsArguments(conditional.Alternate, includeFunctionBodies);
                case CallExpression call:
                    if (ExpressionContainsArguments(call.Callee, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ExpressionContainsArguments(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression newExpression:
                    if (ExpressionContainsArguments(newExpression.Constructor, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        if (ExpressionContainsArguments(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    return ExpressionContainsArguments(member.Target, includeFunctionBodies) ||
                           ExpressionContainsArguments(member.Property, includeFunctionBodies);
                case AssignmentExpression assignment:
                    return ExpressionContainsArguments(assignment.Value, includeFunctionBodies);
                case PropertyAssignmentExpression propertyAssignment:
                    return ExpressionContainsArguments(propertyAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsArguments(propertyAssignment.Property, includeFunctionBodies) ||
                           ExpressionContainsArguments(propertyAssignment.Value, includeFunctionBodies);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsArguments(indexAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsArguments(indexAssignment.Index, includeFunctionBodies) ||
                           ExpressionContainsArguments(indexAssignment.Value, includeFunctionBodies);
                case SequenceExpression sequence:
                    return ExpressionContainsArguments(sequence.Left, includeFunctionBodies) ||
                           ExpressionContainsArguments(sequence.Right, includeFunctionBodies);
                case DestructuringAssignmentExpression destructuringAssignment:
                    return ExpressionContainsArguments(destructuringAssignment.Value, includeFunctionBodies);
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null &&
                            ExpressionContainsArguments(element.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey &&
                            ExpressionContainsArguments(computedKey, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Kind == ObjectMemberKind.Spread && member.Value is not null &&
                            ExpressionContainsArguments(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Function is not null || member.Kind is ObjectMemberKind.Method
                            or ObjectMemberKind.Getter or ObjectMemberKind.Setter)
                        {
                            if (member.IsComputed && member.Value is not null &&
                                ExpressionContainsArguments(member.Value, includeFunctionBodies))
                            {
                                return true;
                            }

                            if (includeFunctionBodies && member.Function is not null)
                            {
                                foreach (var parameter in member.Function.Parameters)
                                {
                                    if (parameter.DefaultValue is not null &&
                                        ExpressionContainsArguments(parameter.DefaultValue, true))
                                    {
                                        return true;
                                    }
                                }

                                if (ContainsArguments(member.Function.Body.Statements, true))
                                {
                                    return true;
                                }
                            }

                            continue;
                        }

                        if (member.Value is not null &&
                            ExpressionContainsArguments(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null &&
                            ExpressionContainsArguments(part.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression taggedTemplate:
                    if (ExpressionContainsArguments(taggedTemplate.Tag, includeFunctionBodies) ||
                        ExpressionContainsArguments(taggedTemplate.StringsArray, includeFunctionBodies) ||
                        ExpressionContainsArguments(taggedTemplate.RawStringsArray, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        if (ExpressionContainsArguments(expr, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case YieldExpression { Expression: not null } yieldExpression:
                    expression = yieldExpression.Expression;
                    continue;
                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case FunctionExpression functionExpression when includeFunctionBodies:
                    foreach (var parameter in functionExpression.Parameters)
                    {
                        if (parameter.DefaultValue is not null &&
                            ExpressionContainsArguments(parameter.DefaultValue, true))
                        {
                            return true;
                        }
                    }

                    return ContainsArguments(functionExpression.Body.Statements, true);
                case FunctionExpression:
                case ClassExpression:
                    return false;
                case LiteralExpression:
                case ThisExpression:
                case SuperExpression:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool ExpressionContainsSuperCall(ExpressionNode expression, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (expression)
            {
                case CallExpression call when IsSuperCallee(call.Callee):
                    return true;
                case CallExpression call:
                    if (ExpressionContainsSuperCall(call.Callee, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ExpressionContainsSuperCall(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression newExpression:
                    if (ExpressionContainsSuperCall(newExpression.Constructor, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        if (ExpressionContainsSuperCall(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    expression = member.Target;
                    continue;
                case BinaryExpression binary:
                    return ExpressionContainsSuperCall(binary.Left, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(binary.Right, includeFunctionBodies);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ConditionalExpression conditional:
                    return ExpressionContainsSuperCall(conditional.Test, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(conditional.Consequent, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(conditional.Alternate, includeFunctionBodies);
                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;
                case PropertyAssignmentExpression propertyAssignment:
                    return ExpressionContainsSuperCall(propertyAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(propertyAssignment.Property, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(propertyAssignment.Value, includeFunctionBodies);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsSuperCall(indexAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(indexAssignment.Index, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(indexAssignment.Value, includeFunctionBodies);
                case SequenceExpression sequence:
                    return ExpressionContainsSuperCall(sequence.Left, includeFunctionBodies) ||
                           ExpressionContainsSuperCall(sequence.Right, includeFunctionBodies);
                case DestructuringAssignmentExpression destructuringAssignment:
                    expression = destructuringAssignment.Value;
                    continue;
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null &&
                            ExpressionContainsSuperCall(element.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey &&
                            ExpressionContainsSuperCall(computedKey, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Kind == ObjectMemberKind.Spread && member.Value is not null &&
                            ExpressionContainsSuperCall(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Function is not null || member.Kind is ObjectMemberKind.Method
                            or ObjectMemberKind.Getter or ObjectMemberKind.Setter)
                        {
                            if (member.IsComputed && member.Value is not null &&
                                ExpressionContainsSuperCall(member.Value, includeFunctionBodies))
                            {
                                return true;
                            }

                            if (includeFunctionBodies && member.Function is not null)
                            {
                                foreach (var parameter in member.Function.Parameters)
                                {
                                    if (parameter.DefaultValue is not null &&
                                        ExpressionContainsSuperCall(parameter.DefaultValue, true))
                                    {
                                        return true;
                                    }
                                }

                                if (ContainsSuperCall(member.Function.Body.Statements, true))
                                {
                                    return true;
                                }
                            }

                            continue;
                        }

                        if (member.Value is not null &&
                            ExpressionContainsSuperCall(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null &&
                            ExpressionContainsSuperCall(part.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression taggedTemplate:
                    if (ExpressionContainsSuperCall(taggedTemplate.Tag, includeFunctionBodies) ||
                        ExpressionContainsSuperCall(taggedTemplate.StringsArray, includeFunctionBodies) ||
                        ExpressionContainsSuperCall(taggedTemplate.RawStringsArray, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        if (ExpressionContainsSuperCall(expr, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case YieldExpression { Expression: not null } yieldExpression:
                    expression = yieldExpression.Expression;
                    continue;
                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case FunctionExpression functionExpression when includeFunctionBodies:
                    foreach (var parameter in functionExpression.Parameters)
                    {
                        if (parameter.DefaultValue is not null &&
                            ExpressionContainsSuperCall(parameter.DefaultValue, true))
                        {
                            return true;
                        }
                    }

                    return ContainsSuperCall(functionExpression.Body.Statements, true);
                case ClassExpression:
                case FunctionExpression:
                    return false;
                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static bool IsSuperCallee(ExpressionNode callee)
    {
        return callee is SuperExpression || callee is MemberExpression { Target: SuperExpression };
    }

    private static bool ExpressionContainsSuper(ExpressionNode expression, bool includeFunctionBodies)
    {
        while (true)
        {
            switch (expression)
            {
                case SuperExpression:
                    return true;
                case MemberExpression { Target: SuperExpression }:
                    return true;
                case MemberExpression member:
                    return ExpressionContainsSuper(member.Target, includeFunctionBodies) ||
                           ExpressionContainsSuper(member.Property, includeFunctionBodies);
                case CallExpression call:
                    if (call.Callee is SuperExpression)
                    {
                        return true;
                    }

                    if (ExpressionContainsSuper(call.Callee, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ExpressionContainsSuper(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression newExpression:
                    if (ExpressionContainsSuper(newExpression.Constructor, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        if (ExpressionContainsSuper(argument.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case BinaryExpression binary:
                    return ExpressionContainsSuper(binary.Left, includeFunctionBodies) ||
                           ExpressionContainsSuper(binary.Right, includeFunctionBodies);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ConditionalExpression conditional:
                    return ExpressionContainsSuper(conditional.Test, includeFunctionBodies) ||
                           ExpressionContainsSuper(conditional.Consequent, includeFunctionBodies) ||
                           ExpressionContainsSuper(conditional.Alternate, includeFunctionBodies);
                case PropertyAssignmentExpression propertyAssignment:
                    return ExpressionContainsSuper(propertyAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsSuper(propertyAssignment.Property, includeFunctionBodies) ||
                           ExpressionContainsSuper(propertyAssignment.Value, includeFunctionBodies);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsSuper(indexAssignment.Target, includeFunctionBodies) ||
                           ExpressionContainsSuper(indexAssignment.Index, includeFunctionBodies) ||
                           ExpressionContainsSuper(indexAssignment.Value, includeFunctionBodies);
                case SequenceExpression sequence:
                    return ExpressionContainsSuper(sequence.Left, includeFunctionBodies) ||
                           ExpressionContainsSuper(sequence.Right, includeFunctionBodies);
                case DestructuringAssignmentExpression destructuringAssignment:
                    return ExpressionContainsSuper(destructuringAssignment.Value, includeFunctionBodies);
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null &&
                            ExpressionContainsSuper(element.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey &&
                            ExpressionContainsSuper(computedKey, includeFunctionBodies))
                        {
                            return true;
                        }

                        if (member.Function is not null ||
                            member.Kind is ObjectMemberKind.Method or ObjectMemberKind.Getter or ObjectMemberKind.Setter)
                        {
                            if (member.IsComputed && member.Value is not null &&
                                ExpressionContainsSuper(member.Value, includeFunctionBodies))
                            {
                                return true;
                            }

                            if (includeFunctionBodies && member.Function is not null)
                            {
                                foreach (var parameter in member.Function.Parameters)
                                {
                                    if (parameter.DefaultValue is not null &&
                                        ExpressionContainsSuper(parameter.DefaultValue, true))
                                    {
                                        return true;
                                    }
                                }

                                if (ContainsSuperReference(member.Function.Body.Statements, true))
                                {
                                    return true;
                                }
                            }

                            continue;
                        }

                        if (member.Value is not null &&
                            ExpressionContainsSuper(member.Value, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null &&
                            ExpressionContainsSuper(part.Expression, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression tagged:
                    if (ExpressionContainsSuper(tagged.Tag, includeFunctionBodies) ||
                        ExpressionContainsSuper(tagged.StringsArray, includeFunctionBodies) ||
                        ExpressionContainsSuper(tagged.RawStringsArray, includeFunctionBodies))
                    {
                        return true;
                    }

                    foreach (var expr in tagged.Expressions)
                    {
                        if (ExpressionContainsSuper(expr, includeFunctionBodies))
                        {
                            return true;
                        }
                    }

                    return false;
                case YieldExpression { Expression: not null } yieldExpression:
                    expression = yieldExpression.Expression;
                    continue;
                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                case FunctionExpression functionExpression when includeFunctionBodies:
                    foreach (var parameter in functionExpression.Parameters)
                    {
                        if (parameter.DefaultValue is not null &&
                            ExpressionContainsSuper(parameter.DefaultValue, true))
                        {
                            return true;
                        }
                    }

                    return ContainsSuperReference(functionExpression.Body.Statements, true);
                case ClassExpression:
                case FunctionExpression:
                    return false;
                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                    return false;
                default:
                    return false;
            }
        }
    }

    private static Dictionary<Symbol, bool> CollectLexicalDeclarations(ImmutableArray<StatementNode> statements)
    {
        var declarations = new Dictionary<Symbol, bool>(ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var statement in statements)
        {
            CollectLexicalDeclarationsFromStatement(statement, declarations);
        }

        return declarations;
    }

    private static void CollectLexicalDeclarationsFromStatement(
        StatementNode statement,
        Dictionary<Symbol, bool> declarations)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectLexicalDeclarationsFromStatement(inner, declarations);
                    }

                    break;
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } lexicalDeclaration:
                {
                    var isConst = lexicalDeclaration.Kind == VariableKind.Const;
                    foreach (var declarator in lexicalDeclaration.Declarators)
                    {
                        CollectLexicalDeclarationNames(declarator.Target, isConst, declarations);
                    }

                    break;
                }
                case ClassDeclaration classDeclaration:
                    declarations[classDeclaration.Name] = true;
                    break;
                case IfStatement ifStatement:
                    CollectLexicalDeclarationsFromStatement(ifStatement.Then, declarations);
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
                        } initDecl)
                    {
                        var isConst = initDecl.Kind == VariableKind.Const;
                        foreach (var declarator in initDecl.Declarators)
                        {
                            CollectLexicalDeclarationNames(declarator.Target, isConst, declarations);
                        }
                    }

                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    break;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const)
                    {
                        var isConst = forEachStatement.DeclarationKind == VariableKind.Const;
                        CollectLexicalDeclarationNames(forEachStatement.Target, isConst, declarations);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectLexicalDeclarationsFromStatement(switchCase.Body, declarations);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectLexicalDeclarationsFromStatement(tryStatement.TryBlock, declarations);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is not null)
                        {
                            CollectLexicalDeclarationNames(catchClause.Binding, false, declarations);
                        }
                        CollectLexicalDeclarationsFromStatement(catchClause.Body, declarations);
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

    private static void CollectLexicalDeclarationNames(
        BindingTarget target,
        bool isConst,
        Dictionary<Symbol, bool> declarations)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    declarations[identifier.Name] =
                        declarations.TryGetValue(identifier.Name, out var existing)
                            ? existing || isConst
                            : isConst;
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            CollectLexicalDeclarationNames(element.Target, isConst, declarations);
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
                        CollectLexicalDeclarationNames(property.Target, isConst, declarations);
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

    private static void InstantiateLexicalDeclarations(
        JsEnvironment lexicalEnvironment,
        Dictionary<Symbol, bool> declarations)
    {
        foreach (var (name, isConst) in declarations)
        {
            if (lexicalEnvironment.HasOwnBinding(name))
            {
                continue;
            }

            lexicalEnvironment.Define(name, JsEnvironment.Uninitialized, isConst, isLexical: true,
                blocksFunctionScopeOverride: true);
        }
    }

    private static void RollbackEvalBindings(
        JsEnvironment varEnvironment,
        HashSet<Symbol> declaredNames,
        HashSet<Symbol> preexistingBindings)
    {
        foreach (var name in declaredNames)
        {
            if (preexistingBindings.Contains(name))
            {
                continue;
            }

            varEnvironment.DeleteBinding(name);
        }
    }

    /// <summary>
    ///     ES2022 AllPrivateNamesValid: Walks the AST and finds the first private name reference
    ///     that is not declared in any of the available private name scopes.
    ///     Returns the invalid private name (e.g., "#x") or null if all are valid.
    /// </summary>
    private static string? FindInvalidPrivateName(
        ImmutableArray<StatementNode> statements,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        foreach (var statement in statements)
        {
            var invalid = FindInvalidPrivateNameInStatement(statement, availableScopes);
            if (invalid is not null)
            {
                return invalid;
            }
        }

        return null;
    }

    private static string? FindInvalidPrivateNameInStatement(
        StatementNode statement,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        while (true)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    return FindInvalidPrivateNameInExpression(expressionStatement.Expression, availableScopes);

                case BlockStatement block:
                    return FindInvalidPrivateName(block.Statements, availableScopes);

                case IfStatement ifStatement:
                    var thenResult = FindInvalidPrivateNameInStatement(ifStatement.Then, availableScopes);
                    if (thenResult is not null)
                    {
                        return thenResult;
                    }

                    if (ifStatement.Else is not null)
                    {
                        statement = ifStatement.Else;
                        continue;
                    }

                    return null;

                case WhileStatement whileStatement:
                    var whileCondition = FindInvalidPrivateNameInExpression(whileStatement.Condition, availableScopes);
                    if (whileCondition is not null)
                    {
                        return whileCondition;
                    }

                    statement = whileStatement.Body;
                    continue;

                case DoWhileStatement doWhileStatement:
                    var doBody = FindInvalidPrivateNameInStatement(doWhileStatement.Body, availableScopes);
                    if (doBody is not null)
                    {
                        return doBody;
                    }

                    return FindInvalidPrivateNameInExpression(doWhileStatement.Condition, availableScopes);

                case ForStatement forStatement:
                    if (forStatement.Initializer is ExpressionStatement initExpr)
                    {
                        var initResult = FindInvalidPrivateNameInExpression(initExpr.Expression, availableScopes);
                        if (initResult is not null)
                        {
                            return initResult;
                        }
                    }
                    else if (forStatement.Initializer is VariableDeclaration initVar)
                    {
                        var initVarResult = FindInvalidPrivateNameInStatement(initVar, availableScopes);
                        if (initVarResult is not null)
                        {
                            return initVarResult;
                        }
                    }

                    if (forStatement.Condition is not null)
                    {
                        var condResult = FindInvalidPrivateNameInExpression(forStatement.Condition, availableScopes);
                        if (condResult is not null)
                        {
                            return condResult;
                        }
                    }

                    if (forStatement.Increment is not null)
                    {
                        var incResult = FindInvalidPrivateNameInExpression(forStatement.Increment, availableScopes);
                        if (incResult is not null)
                        {
                            return incResult;
                        }
                    }

                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    return null;

                case ForEachStatement forEachStatement:
                    var iterableResult = FindInvalidPrivateNameInExpression(forEachStatement.Iterable, availableScopes);
                    if (iterableResult is not null)
                    {
                        return iterableResult;
                    }

                    statement = forEachStatement.Body;
                    continue;

                case SwitchStatement switchStatement:
                    var switchExpr = FindInvalidPrivateNameInExpression(switchStatement.Discriminant, availableScopes);
                    if (switchExpr is not null)
                    {
                        return switchExpr;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null)
                        {
                            var testResult = FindInvalidPrivateNameInExpression(switchCase.Test, availableScopes);
                            if (testResult is not null)
                            {
                                return testResult;
                            }
                        }

                        var caseBody = FindInvalidPrivateNameInStatement(switchCase.Body, availableScopes);
                        if (caseBody is not null)
                        {
                            return caseBody;
                        }
                    }

                    return null;

                case TryStatement tryStatement:
                    var tryBody = FindInvalidPrivateNameInStatement(tryStatement.TryBlock, availableScopes);
                    if (tryBody is not null)
                    {
                        return tryBody;
                    }

                    if (tryStatement.Catch is { Body: not null } catchClause)
                    {
                        var catchBody = FindInvalidPrivateNameInStatement(catchClause.Body, availableScopes);
                        if (catchBody is not null)
                        {
                            return catchBody;
                        }
                    }

                    if (tryStatement.Finally is not null)
                    {
                        statement = tryStatement.Finally;
                        continue;
                    }

                    return null;

                case WithStatement withStatement:
                    var withObj = FindInvalidPrivateNameInExpression(withStatement.Object, availableScopes);
                    if (withObj is not null)
                    {
                        return withObj;
                    }

                    statement = withStatement.Body;
                    continue;

                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;

                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    return FindInvalidPrivateNameInExpression(returnStatement.Expression, availableScopes);

                case ThrowStatement throwStatement:
                    return FindInvalidPrivateNameInExpression(throwStatement.Expression, availableScopes);

                case VariableDeclaration varDecl:
                    foreach (var declarator in varDecl.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            var initResult = FindInvalidPrivateNameInExpression(declarator.Initializer, availableScopes);
                            if (initResult is not null)
                            {
                                return initResult;
                            }
                        }
                    }

                    return null;

                case FunctionDeclaration functionDeclaration:
                    // Function bodies create their own scope, but they can still reference
                    // private names from enclosing classes. We need to check them.
                    return FindInvalidPrivateNameInFunction(functionDeclaration.Function, availableScopes);

                case ClassDeclaration classDeclaration:
                    // Class declarations introduce their own private name scope.
                    // The class body's private names are valid within that scope.
                    // We don't recurse into class bodies since they define their own scope.
                    // However, computed property names and heritage clause are evaluated
                    // in the outer scope and may reference outer private names.
                    if (classDeclaration.Definition.Extends is not null)
                    {
                        var heritage = FindInvalidPrivateNameInExpression(classDeclaration.Definition.Extends, availableScopes);
                        if (heritage is not null)
                        {
                            return heritage;
                        }
                    }

                    foreach (var member in classDeclaration.Definition.Members)
                    {
                        if (member.IsComputed && member.ComputedName is { } computedKey)
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }
                    }

                    return null;

                default:
                    return null;
            }
        }
    }

    private static string? FindInvalidPrivateNameInExpression(
        ExpressionNode expression,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        while (true)
        {
            switch (expression)
            {
                case MemberExpression memberExpression:
                    // Check the target first
                    var targetResult = FindInvalidPrivateNameInExpression(memberExpression.Target, availableScopes);
                    if (targetResult is not null)
                    {
                        return targetResult;
                    }

                    // For non-computed member expressions with private names (e.g., this.#x),
                    // the Property is a LiteralExpression containing the private name string.
                    if (!memberExpression.IsComputed &&
                        memberExpression.Property is LiteralExpression { Value: string propName } &&
                        propName.IsPrivateName())
                    {
                        // Check if this private name is valid in available scopes
                        if (!IsPrivateNameValid(propName, availableScopes))
                        {
                            return propName;
                        }

                        return null;
                    }

                    // For computed expressions, check the property expression
                    expression = memberExpression.Property;
                    continue;

                case PrivateIdentifierExpression privateId:
                    // Private identifier in 'in' expression (e.g., #field in obj)
                    var privateName = "#" + privateId.Name;
                    if (!IsPrivateNameValid(privateName, availableScopes))
                    {
                        return privateName;
                    }

                    return null;

                case BinaryExpression binary:
                    var leftResult = FindInvalidPrivateNameInExpression(binary.Left, availableScopes);
                    if (leftResult is not null)
                    {
                        return leftResult;
                    }

                    expression = binary.Right;
                    continue;

                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;

                case ConditionalExpression conditional:
                    var testResult = FindInvalidPrivateNameInExpression(conditional.Test, availableScopes);
                    if (testResult is not null)
                    {
                        return testResult;
                    }

                    var consequent = FindInvalidPrivateNameInExpression(conditional.Consequent, availableScopes);
                    if (consequent is not null)
                    {
                        return consequent;
                    }

                    expression = conditional.Alternate;
                    continue;

                case CallExpression call:
                    var calleeResult = FindInvalidPrivateNameInExpression(call.Callee, availableScopes);
                    if (calleeResult is not null)
                    {
                        return calleeResult;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        var argResult = FindInvalidPrivateNameInExpression(argument.Expression, availableScopes);
                        if (argResult is not null)
                        {
                            return argResult;
                        }
                    }

                    return null;

                case NewExpression newExpression:
                    var ctorResult = FindInvalidPrivateNameInExpression(newExpression.Constructor, availableScopes);
                    if (ctorResult is not null)
                    {
                        return ctorResult;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        var argResult = FindInvalidPrivateNameInExpression(argument.Expression, availableScopes);
                        if (argResult is not null)
                        {
                            return argResult;
                        }
                    }

                    return null;

                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;

                case PropertyAssignmentExpression propertyAssignment:
                    var paTarget = FindInvalidPrivateNameInExpression(propertyAssignment.Target, availableScopes);
                    if (paTarget is not null)
                    {
                        return paTarget;
                    }

                    var paProp = FindInvalidPrivateNameInExpression(propertyAssignment.Property, availableScopes);
                    if (paProp is not null)
                    {
                        return paProp;
                    }

                    expression = propertyAssignment.Value;
                    continue;

                case IndexAssignmentExpression indexAssignment:
                    var iaTarget = FindInvalidPrivateNameInExpression(indexAssignment.Target, availableScopes);
                    if (iaTarget is not null)
                    {
                        return iaTarget;
                    }

                    var iaIndex = FindInvalidPrivateNameInExpression(indexAssignment.Index, availableScopes);
                    if (iaIndex is not null)
                    {
                        return iaIndex;
                    }

                    expression = indexAssignment.Value;
                    continue;

                case SequenceExpression sequence:
                    var seqLeft = FindInvalidPrivateNameInExpression(sequence.Left, availableScopes);
                    if (seqLeft is not null)
                    {
                        return seqLeft;
                    }

                    expression = sequence.Right;
                    continue;

                case DestructuringAssignmentExpression destructuring:
                    expression = destructuring.Value;
                    continue;

                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is not null)
                        {
                            var elemResult = FindInvalidPrivateNameInExpression(element.Expression, availableScopes);
                            if (elemResult is not null)
                            {
                                return elemResult;
                            }
                        }
                    }

                    return null;

                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey)
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }

                        if (member.Value is not null)
                        {
                            var valueResult = FindInvalidPrivateNameInExpression(member.Value, availableScopes);
                            if (valueResult is not null)
                            {
                                return valueResult;
                            }
                        }

                        if (member.Function is not null)
                        {
                            var funcResult = FindInvalidPrivateNameInFunction(member.Function, availableScopes);
                            if (funcResult is not null)
                            {
                                return funcResult;
                            }
                        }
                    }

                    return null;

                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null)
                        {
                            var partResult = FindInvalidPrivateNameInExpression(part.Expression, availableScopes);
                            if (partResult is not null)
                            {
                                return partResult;
                            }
                        }
                    }

                    return null;

                case TaggedTemplateExpression tagged:
                    var tagResult = FindInvalidPrivateNameInExpression(tagged.Tag, availableScopes);
                    if (tagResult is not null)
                    {
                        return tagResult;
                    }

                    foreach (var expr in tagged.Expressions)
                    {
                        var exprResult = FindInvalidPrivateNameInExpression(expr, availableScopes);
                        if (exprResult is not null)
                        {
                            return exprResult;
                        }
                    }

                    return null;

                case YieldExpression yieldExpression when yieldExpression.Expression is not null:
                    expression = yieldExpression.Expression;
                    continue;

                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;

                case FunctionExpression functionExpression:
                    return FindInvalidPrivateNameInFunction(functionExpression, availableScopes);

                case ClassExpression classExpression:
                    // Similar to ClassDeclaration - check heritage and computed keys
                    if (classExpression.Definition.Extends is not null)
                    {
                        var heritage = FindInvalidPrivateNameInExpression(classExpression.Definition.Extends, availableScopes);
                        if (heritage is not null)
                        {
                            return heritage;
                        }
                    }

                    foreach (var member in classExpression.Definition.Members)
                    {
                        if (member.IsComputed && member.ComputedName is { } computedKey)
                        {
                            var keyResult = FindInvalidPrivateNameInExpression(computedKey, availableScopes);
                            if (keyResult is not null)
                            {
                                return keyResult;
                            }
                        }
                    }

                    return null;

                case LiteralExpression:
                case IdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                case NewTargetExpression:
                    return null;

                default:
                    return null;
            }
        }
    }

    private static string? FindInvalidPrivateNameInFunction(
        FunctionExpression function,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        // Check parameter default values
        foreach (var param in function.Parameters)
        {
            if (param.DefaultValue is not null)
            {
                var defaultResult = FindInvalidPrivateNameInExpression(param.DefaultValue, availableScopes);
                if (defaultResult is not null)
                {
                    return defaultResult;
                }
            }
        }

        // Function bodies can reference outer private names
        return FindInvalidPrivateName(function.Body.Statements, availableScopes);
    }

    private static bool IsPrivateNameValid(
        string privateName,
        ImmutableArray<PrivateNameScope>? availableScopes)
    {
        if (!availableScopes.HasValue || availableScopes.Value.IsDefaultOrEmpty)
        {
            // No private name scopes available, so any private name is invalid
            return false;
        }

        // Check if the private name (without #) exists in any scope
        var lexeme = privateName.StartsWith('#') ? privateName[1..] : privateName;
        foreach (var scope in availableScopes.Value)
        {
            if (scope.TryGetKey(lexeme, out _))
            {
                return true;
            }
        }

        return false;
    }
}
