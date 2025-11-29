using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

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

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        if (arguments.Count == 0 || arguments[0] is not string code)
        {
            return arguments.Count > 0 ? arguments[0] : Symbol.Undefined;
        }

        // Use the calling environment if available, otherwise use global
        var environment = CallingJsEnvironment ?? throw new InvalidOperationException(
            "eval() called without a calling environment");

        var forceStrict = CallingContext?.CurrentScope.IsStrict ?? false;

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

        // Evaluate directly in the calling environment without going through the event queue
        // This is safe because eval() is synchronous in JavaScript
        var result = _engine.ExecuteProgram(program, environment, CancellationToken.None, ExecutionKind.Eval);

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
}
