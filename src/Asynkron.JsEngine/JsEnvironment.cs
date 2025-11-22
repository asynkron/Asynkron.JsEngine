using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment(
    JsEnvironment? enclosing = null,
    bool isFunctionScope = false,
    bool isStrict = false,
    SourceReference? creatingSource = null,
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
    private readonly SourceReference? _creatingSource = creatingSource;
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
        var isGlobalScope = scope._enclosing is null;
        JsObject? globalThis = null;
        if (isGlobalScope && scope._values.TryGetValue(Symbols.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            globalThis = globalObject;
        }

        if (scope._values.TryGetValue(name, out var existing))
        {
            if (hasInitializer)
            {
                existing.Value = value;
                globalThis?.SetProperty(name.Name, value);
            }

            return;
        }

        scope._values[name] = new Binding(value, false);
        globalThis?.SetProperty(name.Name, value);
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
        Define(name, value, false);
        if (_enclosing is null && _values.TryGetValue(Symbols.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            globalObject.SetProperty(name.Name, value);
        }
    }

    private JsEnvironment GetFunctionScope()
    {
        var current = this;
        while (!current._isFunctionScope)
        {
            current = current._enclosing
                      ?? throw new InvalidOperationException("Unable to locate function scope for var declaration.");
        }

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
            if (current._creatingSource is not null || current._description is not null)
            {
                var operationType = DetermineOperationTypeFromDescription(current._description);
                var description = current._description ?? operationType;
                frames.Add(new CallStackFrame(
                    operationType,
                    description,
                    current._creatingSource,
                    depth
                ));

                depth++;
            }

            // Follow the enclosing chain (lexical scope chain)
            current = current._enclosing;
        }

        return frames;
    }

    private static string DetermineOperationTypeFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "unknown";
        }

        var trimmed = description.TrimStart();
        var separators = new[] { ' ', '-', ':' };
        var separatorIndex = trimmed.IndexOfAny(separators);
        var firstToken = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;

        return string.IsNullOrEmpty(firstToken)
            ? "unknown"
            : firstToken.ToLowerInvariant();
    }
}
