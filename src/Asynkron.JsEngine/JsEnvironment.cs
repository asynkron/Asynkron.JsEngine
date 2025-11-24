using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment
{
    private const int MaxDepth = 1_000;
    private readonly SourceReference? _creatingSource;
    private readonly string? _description;
    private readonly JsEnvironment? _enclosing;
    private readonly bool _isFunctionScope;

    private readonly Dictionary<Symbol, Binding> _values = new();
    private Dictionary<Symbol, List<Action<object?>>>? _bindingObservers;

    public JsEnvironment(
        JsEnvironment? enclosing = null,
        bool isFunctionScope = false,
        bool isStrict = false,
        SourceReference? creatingSource = null,
        string? description = null)
    {
        _enclosing = enclosing;
        _isFunctionScope = isFunctionScope;
        _creatingSource = creatingSource;
        _description = description;
        IsStrictLocal = isStrict;

        Depth = (_enclosing?.Depth ?? -1) + 1;
        if (Depth > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Exceeded maximum environment depth of {MaxDepth}. Possible unbounded recursion detected.");
        }
    }

    /// <summary>
    ///     Depth of the environment chain (0 for the root/global).
    /// </summary>
    public int Depth { get; }

    private bool IsStrictLocal { get; }

    /// <summary>
    ///     Returns true if this environment or any enclosing environment is in strict mode.
    /// </summary>
    public bool IsStrict => IsStrictLocal || (_enclosing?.IsStrict ?? false);

    public void Define(
        Symbol name,
        object? value,
        bool isConst = false,
        bool isGlobalConstant = false,
        bool isLexical = true,
        bool blocksFunctionScopeOverride = false)
    {
        if (_values.TryGetValue(name, out var existing) && existing.IsGlobalConstant)
        {
            return;
        }

        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst || binding.IsGlobalConstant)
            {
                // Generators can execute flattened blocks without recreating the
                // lexical environment per iteration, which would normally allow
                // a fresh const/let binding each time. If we see a lexical
                // redeclaration request, replace the binding so loop iterations
                // can observe the new value instead of sticking with the first.
                if (isLexical && blocksFunctionScopeOverride)
                {
                    _values[name] = new Binding(value, isConst, isGlobalConstant, isLexical,
                        blocksFunctionScopeOverride);
                }

                return;
            }

            binding.Value = value;
            binding.UpgradeLexical(isLexical, blocksFunctionScopeOverride);
            NotifyBindingObservers(name, value);
            return;
        }

        _values[name] = new Binding(value, isConst, isGlobalConstant, isLexical, blocksFunctionScopeOverride);
        NotifyBindingObservers(name, value);
    }

    public void DefineFunctionScoped(
        Symbol name,
        object? value,
        bool hasInitializer,
        bool isFunctionDeclaration = false,
        bool? globalFunctionConfigurable = null)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        var isGlobalScope = scope._enclosing is null;
        JsObject? globalThis = null;
        PropertyDescriptor? existingDescriptor = null;
        object? existingGlobalValue = null;
        if (isGlobalScope && scope._values.TryGetValue(Symbols.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            globalThis = globalObject;
            existingDescriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
            if (existingDescriptor is not null)
            {
                globalObject.TryGetProperty(name.Name, out existingGlobalValue);
            }
        }

        if (scope._values.TryGetValue(name, out var existing))
        {
            if (existing.IsConst || existing.IsGlobalConstant || existing.BlocksFunctionScopeOverride)
            {
                return;
            }

            if (hasInitializer)
            {
                existing.Value = value;
                if (isGlobalScope && globalThis is not null)
                {
                    globalThis.SetProperty(name.Name, value);
                }
            }

            return;
        }

        var initialValue = value;
        var shouldWriteGlobal = true;

        if (isGlobalScope && existingDescriptor is not null && !hasInitializer)
        {
            initialValue = existingGlobalValue;
            shouldWriteGlobal = false;
        }

        scope._values[name] = new Binding(initialValue, false, false, false, false);
        if (isGlobalScope && globalThis is not null && shouldWriteGlobal)
        {
            if (isFunctionDeclaration)
            {
                var configurable = globalFunctionConfigurable ?? false;
                globalThis.DefineProperty(name.Name,
                    new PropertyDescriptor
                    {
                        Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                    });
            }
            else
            {
                globalThis.SetProperty(name.Name, initialValue);
            }
        }
    }

    public object? Get(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            if (_enclosing is null &&
                _values.TryGetValue(Symbols.This, out var thisBinding) &&
                thisBinding.Value is JsObject globalObject &&
                globalObject.TryGetProperty(name.Name, out var globalValue))
            {
                return globalValue;
            }

            return binding.Value;
        }

        if (_enclosing is not null)
        {
            return _enclosing.Get(name);
        }

        if (_values.TryGetValue(Symbols.This, out var rootThis) &&
            rootThis.Value is JsObject rootGlobal &&
            rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            return propertyValue;
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    public bool TryGet(Symbol name, out object? value)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            if (_enclosing is null &&
                _values.TryGetValue(Symbols.This, out var thisBinding) &&
                thisBinding.Value is JsObject globalObject &&
                globalObject.TryGetProperty(name.Name, out var globalValue))
            {
                value = globalValue;
                return true;
            }

            value = binding.Value;
            return true;
        }

        if (_enclosing is not null)
        {
            return _enclosing.TryGet(name, out value);
        }

        if (_values.TryGetValue(Symbols.This, out var rootThis) &&
            rootThis.Value is JsObject rootGlobal &&
            rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            value = propertyValue;
            return true;
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
        JsObject? globalObject = null;
        if (_enclosing is null && _values.TryGetValue(Symbols.This, out var thisBinding) &&
            thisBinding.Value is JsObject global)
        {
            globalObject = global;
        }

        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst)
            {
                throw new InvalidOperationException($"Cannot reassign constant '{name.Name}'.");
            }

            if (binding.IsGlobalConstant)
            {
                if (isStrictContext)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not writable");
                }

                return;
            }

            binding.Value = value;
            globalObject?.SetProperty(name.Name, value);
            NotifyBindingObservers(name, value);
            return;
        }

        if (_enclosing is not null)
        {
            _enclosing.AssignInternal(name, value, isStrictContext);
            return;
        }

        if (globalObject is not null && globalObject.GetOwnPropertyDescriptor(name.Name) is not null)
        {
            globalObject.SetProperty(name.Name, value);
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
        Define(name, value);
        if (globalObject is not null)
        {
            globalObject.SetProperty(name.Name, value);
        }
    }

    internal void AddBindingObserver(Symbol symbol, Action<object?> observer)
    {
        _bindingObservers ??= new Dictionary<Symbol, List<Action<object?>>>(ReferenceEqualityComparer<Symbol>.Instance);
        if (!_bindingObservers.TryGetValue(symbol, out var list))
        {
            list = new List<Action<object?>>();
            _bindingObservers[symbol] = list;
        }

        list.Add(observer);
    }

    private void NotifyBindingObservers(Symbol symbol, object? value)
    {
        if (_bindingObservers is null || !_bindingObservers.TryGetValue(symbol, out var observers))
        {
            return;
        }

        foreach (var observer in observers)
        {
            observer(value);
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
    ///     Gets all variables from this environment and all enclosing environments.
    ///     Used for debugging purposes.
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
    ///     Builds a call stack by traversing the enclosing environment chain
    ///     and collecting information about the S-expressions that created each environment.
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

    private sealed class Binding(
        object? value,
        bool isConst,
        bool isGlobalConstant,
        bool isLexical,
        bool blocksFunctionScopeOverride)
    {
        public object? Value { get; set; } = value;

        public bool IsConst { get; } = isConst;

        public bool IsGlobalConstant { get; } = isGlobalConstant;

        public bool IsLexical { get; private set; } = isLexical;

        public bool BlocksFunctionScopeOverride { get; private set; } = blocksFunctionScopeOverride;

        public void UpgradeLexical(bool isLexical, bool blocksFunctionScopeOverride)
        {
            if (isLexical)
            {
                IsLexical = true;
            }

            if (blocksFunctionScopeOverride)
            {
                BlocksFunctionScopeOverride = true;
            }
        }
    }
}
