using System.Collections.Generic;
using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine;

/// <summary>
///     A special host function for eval() that has access to the calling environment
///     and can evaluate code synchronously in that context.
/// </summary>
public sealed class EvalHostFunction : IJsEnvironmentAwareCallable, IEvaluationContextAwareCallable, IJsPropertyAccessor
{
    private readonly JsEngine _engine;
    private readonly JsObject _properties = new();

    public EvalHostFunction(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _properties.SetProperty("prototype", new JsObject());
    }

    public EvaluationContext? CallingContext { get; set; }

    /// <summary>
    ///     The environment that is calling this function.
    ///     This allows eval to execute code in the caller's scope.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

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
        var environment = isDirectEval
            ? CallingJsEnvironment ?? throw new InvalidOperationException("eval() called without a calling environment")
            : _engine.GlobalEnvironment;

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

        var lexicalEnv = environment;
        var varEnv = lexicalEnv.GetFunctionScope();
        var isStrictEval = program.Typed.IsStrict;

        // 18.2.1.1 EvalDeclarationInstantiation: non-strict direct eval must
        // reject var declarations that collide with caller lexicals (including parameters).
        var varDeclaredNames = new HashSet<Symbol>();
        CollectVarDeclaredNames(program.Typed.Body, varDeclaredNames);
        var lexicallyDeclaredNames = CollectLexicallyDeclaredNames(program.Typed.Body);

        if (!isStrictEval)
        {
            foreach (var name in varDeclaredNames)
            {
                var hasGlobalLexical = varEnv.IsGlobalFunctionScope &&
                                       (varEnv.HasOwnLexicalBinding(name) || varEnv.HasBodyLexicalName(name));
                if (HasDeclarativeBindingBetween(lexicalEnv, varEnv, name) ||
                    hasGlobalLexical)
                {
                    throw StandardLibrary.ThrowSyntaxError(
                        $"Cannot declare var-scoped binding '{name.Name}' in direct eval due to existing lexical declaration.",
                        CallingContext,
                        environment.RealmState);
                }
            }
        }

        // EvalDeclarationInstantiation step 7+8: lexical declarations must not
        // conflict with existing var/lexical bindings in the variable environment.
        foreach (var name in lexicallyDeclaredNames)
        {
            var hasVarBinding = varEnv.HasFunctionScopedBinding(name);
            var hasLexicalInVarEnv = varEnv.IsGlobalFunctionScope && varEnv.HasOwnLexicalBinding(name);
            if (hasVarBinding || hasLexicalInVarEnv)
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

        var evalEnvironment = isStrictEval
            ? new JsEnvironment(lexicalEnv, true, true, description: "eval", treatAsGlobalFunctionScope: false)
            : lexicalEnv;

        // Evaluate directly in the constructed eval environment (direct eval is synchronous).
        var result = program.Typed.EvaluateProgram(evalEnvironment, _engine.RealmState, CancellationToken.None,
            ExecutionKind.Eval, createStrictEnvironment: false);

        return result;
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
                        CollectBindingNames(catchClause.Binding, names);
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

            if (current.HasOwnBinding(name))
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }
}
