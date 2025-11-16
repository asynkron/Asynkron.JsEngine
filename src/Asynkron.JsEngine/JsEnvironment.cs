using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment(
    JsEnvironment? enclosing = null,
    bool isFunctionScope = false,
    bool isStrict = false,
    Cons? creatingExpression = null,
    string? description = null)
{
    private sealed class Binding(object? value, bool isConst)
    {
        public object? Value { get; set; } = value;

        public bool IsConst { get; } = isConst;
    }

    private readonly Dictionary<Symbol, Binding> _values = new();
    private readonly JsEnvironment? _enclosing = enclosing;
    private readonly bool _isFunctionScope = isFunctionScope;
    private readonly Cons? _creatingExpression = creatingExpression;
    private readonly string? _description = description;

    /// <summary>
    /// Returns true if this environment or any enclosing environment is in strict mode.
    /// </summary>
    public bool IsStrict => isStrict || (_enclosing?.IsStrict ?? false);

    public void Define(Symbol name, object? value, bool isConst = false)
    {
        _values[name] = new Binding(value, isConst);
    }

    public void DefineFunctionScoped(Symbol name, object? value, bool hasInitializer)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        if (scope._values.TryGetValue(name, out var existing))
        {
            if (hasInitializer)
            {
                existing.Value = value;
            }

            return;
        }

        scope._values[name] = new Binding(value, false);
    }

    public object? Get(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            return binding.Value;
        }

        if (_enclosing is not null)
        {
            return _enclosing.Get(name);
        }

        throw new InvalidOperationException($"Undefined symbol '{name.Name}'.");
    }

    public bool TryGet(Symbol name, out object? value)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            value = binding.Value;
            return true;
        }

        if (_enclosing is not null)
        {
            return _enclosing.TryGet(name, out value);
        }

        value = null;
        return false;
    }

    public void Assign(Symbol name, object? value)
    {
        // Remember if we're in strict mode at the call site
        var isStrictContext = IsStrict;
        AssignInternal(name, value, isStrictContext);
    }

    private void AssignInternal(Symbol name, object? value, bool isStrictContext)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst)
            {
                throw new InvalidOperationException($"Cannot reassign constant '{name.Name}'.");
            }

            binding.Value = value;
            return;
        }

        if (_enclosing is not null)
        {
            _enclosing.AssignInternal(name, value, isStrictContext);
            return;
        }

        // Reached the global scope without finding the variable
        // In strict mode, assignment to undefined variable is an error
        // In non-strict mode, create the variable as a global
        if (isStrictContext)
        {
            // Use ReferenceError message format
            throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
        }

        // Non-strict mode: Create the variable in the global scope (this environment)
        Define(name, value, isConst: false);
    }

    private JsEnvironment GetFunctionScope()
    {
        var current = this;
        while (!current._isFunctionScope)
            current = current._enclosing
                      ?? throw new InvalidOperationException("Unable to locate function scope for var declaration.");

        return current;
    }

    /// <summary>
    /// Gets all variables from this environment and all enclosing environments.
    /// Used for debugging purposes.
    /// </summary>
    public Dictionary<string, object?> GetAllVariables()
    {
        var result = new Dictionary<string, object?>();

        // Traverse up the scope chain
        var current = this;
        while (current is not null)
        {
            // Add variables from current scope (only if not already present from inner scope)
            foreach (var kvp in current._values)
            {
                if (!result.ContainsKey(kvp.Key.Name))
                {
                    result[kvp.Key.Name] = kvp.Value.Value;
                }
            }

            current = current._enclosing;
        }

        return result;
    }

    /// <summary>
    /// Builds a call stack by traversing the enclosing environment chain
    /// and collecting information about the S-expressions that created each environment.
    /// </summary>
    public List<CallStackFrame> BuildCallStack()
    {
        var frames = new List<CallStackFrame>();
        var current = this;
        var depth = 0;
        var iterations = 0;
        const int maxIterations = 100; // Prevent infinite loops

        while (current is not null && iterations < maxIterations)
        {
            iterations++;

            // Always add a frame if we have any identifying information
            if (current._creatingExpression is not null || current._description is not null)
            {
                var operationType = DetermineOperationType(current._creatingExpression);
                var description = current._description ??
                                  GetExpressionDescription(current._creatingExpression, operationType);

                frames.Add(new CallStackFrame(
                    operationType,
                    description,
                    current._creatingExpression,
                    depth
                ));

                depth++;
            }

            // Follow the enclosing chain (lexical scope chain)
            current = current._enclosing;
        }

        return frames;
    }

    /// <summary>
    /// Determines the operation type from an S-expression.
    /// </summary>
    private static string DetermineOperationType(Cons? expression)
    {
        if (expression is null)
        {
            return "unknown";
        }

        if (expression.Head is not Symbol symbol)
        {
            return "expression";
        }

        if (ReferenceEquals(symbol, JsSymbols.Call))
        {
            return "call";
        }

        if (ReferenceEquals(symbol, JsSymbols.For))
        {
            return "for";
        }

        if (ReferenceEquals(symbol, JsSymbols.While))
        {
            return "while";
        }

        if (ReferenceEquals(symbol, JsSymbols.DoWhile))
        {
            return "do-while";
        }

        if (ReferenceEquals(symbol, JsSymbols.Function))
        {
            return "function";
        }

        if (ReferenceEquals(symbol, JsSymbols.Block))
        {
            return "block";
        }

        return symbol.Name;

    }

    /// <summary>
    /// Gets a human-readable description for an S-expression.
    /// </summary>
    private static string GetExpressionDescription(Cons? expression, string operationType)
    {
        if (expression is null)
        {
            return "unknown";
        }

        return operationType switch
        {
            "for" => "for loop",
            "while" => "while loop",
            "do-while" => "do-while loop",
            "function" => GetFunctionName(expression),
            "call" => GetCallDescription(expression),
            "block" => "block",
            _ => operationType
        };
    }

    /// <summary>
    /// Extracts the function name from a function S-expression.
    /// </summary>
    private static string GetFunctionName(Cons expression)
    {
        // (function name params body)
        if (expression.Rest.Head is Symbol nameSymbol)
        {
            return $"function {nameSymbol.Name}";
        }

        return "anonymous function";
    }

    /// <summary>
    /// Gets a description of a function call.
    /// </summary>
    private static string GetCallDescription(Cons expression)
    {
        // (call callee args...)
        var callee = expression.Rest.Head;

        if (callee is Symbol symbol)
        {
            return $"call to {symbol.Name}";
        }

        return "function call";
    }
}
