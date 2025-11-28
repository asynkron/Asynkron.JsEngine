using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment
{
    internal static readonly object Uninitialized = new();

    private const int MaxDepth = 1_000;
    private readonly SourceReference? _creatingSource;
    private readonly string? _description;
    private readonly JsEnvironment? _enclosing;
    private readonly bool _isFunctionScope;
    private readonly bool _isParameterEnvironment;
    private HashSet<Symbol>? _bodyLexicalNames;
    private readonly bool _isBodyEnvironment;
    private readonly JsObject? _withObject;
    private HashSet<Symbol>? _simpleCatchParameters;

    private readonly Dictionary<Symbol, Binding> _values = new();
    private Dictionary<Symbol, List<Action<object?>>>? _bindingObservers;

    public JsEnvironment(
        JsEnvironment? enclosing = null,
        bool isFunctionScope = false,
        bool isStrict = false,
        SourceReference? creatingSource = null,
        string? description = null,
        JsObject? withObject = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        _enclosing = enclosing;
        _isFunctionScope = isFunctionScope;
        _creatingSource = creatingSource;
        _description = description;
        IsStrictLocal = isStrict;
        _withObject = withObject;
        _isParameterEnvironment = isParameterEnvironment;
        _isBodyEnvironment = isBodyEnvironment;

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
        bool? globalFunctionConfigurable = null,
        EvaluationContext? context = null,
        bool blocksFunctionScopeOverride = false)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        var isGlobalScope = scope._enclosing is null;
        JsObject? globalThis = null;
        PropertyDescriptor? existingDescriptor = null;
        object? existingGlobalValue = null;
        if (isGlobalScope && scope._values.TryGetValue(Symbol.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            globalThis = globalObject;
            existingDescriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
            if (existingDescriptor is not null)
            {
                globalObject.TryGetProperty(name.Name, out existingGlobalValue);
            }
        }

        if (isGlobalScope && isFunctionDeclaration && existingDescriptor is not null)
        {
            var canDeclare = existingDescriptor.Configurable ||
                             (existingDescriptor.IsDataDescriptor &&
                              existingDescriptor.Writable &&
                              existingDescriptor.Enumerable);
            if (!canDeclare)
            {
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }
        }

        if (scope._values.TryGetValue(name, out var existing))
        {
            if (existing.IsConst || existing.IsGlobalConstant)
            {
                return;
            }

            if (blocksFunctionScopeOverride)
            {
                existing.UpgradeLexical(existing.IsLexical, true);
            }

            if (existing.BlocksFunctionScopeOverride)
            {
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

        var allowConfigurableGlobalBinding =
            context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };

        var initialValue = value;
        var shouldWriteGlobal = true;

        if (isGlobalScope && existingDescriptor is not null && !hasInitializer)
        {
            initialValue = existingGlobalValue;
            shouldWriteGlobal = false;
        }

        scope._values[name] = new Binding(initialValue, false, false, false, blocksFunctionScopeOverride);
        if (isGlobalScope && globalThis is not null && shouldWriteGlobal)
        {
            if (isFunctionDeclaration)
            {
                var configurable = globalFunctionConfigurable ?? allowConfigurableGlobalBinding;
                globalThis.DefineProperty(name.Name,
                    new PropertyDescriptor
                    {
                        Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                    });
            }
            else
            {
                if (existingDescriptor is null)
                {
                    globalThis.DefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue,
                            Writable = true,
                            Enumerable = true,
                            Configurable = allowConfigurableGlobalBinding
                        });
                }
                else
                {
                    globalThis.SetProperty(name.Name, initialValue);
                }
            }
        }
    }

    public object? Get(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current._enclosing is null &&
                    current._values.TryGetValue(Symbol.This, out var thisBinding) &&
                    thisBinding.Value is JsObject globalObject &&
                    globalObject.TryGetProperty(name.Name, out var globalValue))
                {
                    return globalValue;
                }

                return binding.Value;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out var withValue))
            {
                return withValue;
            }

            current = current._enclosing;
        }

        if (_values.TryGetValue(Symbol.This, out var rootThis) &&
            rootThis.Value is JsObject rootGlobal &&
            rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            return propertyValue;
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    internal bool IsConstBinding(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            return binding.IsConst || binding.IsGlobalConstant;
        }

        if (_withObject is not null && TryGetFromWith(_withObject, name, out _))
        {
            return false;
        }

        return _enclosing?.IsConstBinding(name) ?? false;
    }

    internal bool HasBinding(Symbol name)
    {
        if (_values.ContainsKey(name))
        {
            return true;
        }

        if (_withObject is not null && TryGetFromWith(_withObject, name, out _))
        {
            return true;
        }

        return _enclosing?.HasBinding(name) ?? false;
    }

    internal bool HasOwnBinding(Symbol name)
    {
        return _values.ContainsKey(name);
    }

    internal bool HasOwnLexicalBinding(Symbol name)
    {
        return _values.TryGetValue(name, out var binding) && binding.IsLexical;
    }

    internal bool TryAssignBlockedBinding(Symbol name, object? value)
    {
        var current = this;
        while (current is not null)
        {
            if (current._values.TryGetValue(name, out var binding) && binding.BlocksFunctionScopeOverride)
            {
                binding.Value = value;
                current.NotifyBindingObservers(name, value);
                if (current._enclosing is null &&
                    current._values.TryGetValue(Symbol.This, out var thisBinding) &&
                    thisBinding.Value is JsObject globalObject)
                {
                    globalObject.SetProperty(name.Name, value);
                }
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out _))
            {
                break;
            }

            current = current._enclosing;
        }

        return false;
    }

    internal bool HasLexicalBinding(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding) && binding.IsLexical)
        {
            return true;
        }

        return _enclosing?.HasLexicalBinding(name) ?? false;
    }

    internal bool HasBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current._isFunctionScope)
        {
            if (current._withObject is null && current._values.ContainsKey(name))
            {
                return true;
            }

            if (current._isFunctionScope && !current._isParameterEnvironment)
            {
                break;
            }

            current = current._enclosing;
        }

        return false;
    }

    internal bool HasRestrictedGlobalProperty(Symbol name)
    {
        var scope = GetFunctionScope();
        if (!scope.IsGlobalFunctionScope)
        {
            return false;
        }

        if (!scope._values.TryGetValue(Symbol.This, out var thisBinding) ||
            thisBinding.Value is not JsObject globalObject)
        {
            return false;
        }

        var descriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
        return descriptor is not null && !descriptor.Configurable;
    }

    internal bool IsObjectEnvironment => _withObject is not null;

    internal bool HasLexicalBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current._isFunctionScope)
        {
            if (current._values.TryGetValue(name, out var binding) &&
                binding.IsLexical)
            {
                return true;
            }

            current = current._enclosing;
        }

        return false;
    }

    internal bool IsParameterEnvironment => _isParameterEnvironment;
    internal bool IsBodyEnvironment => _isBodyEnvironment;
    internal bool IsFunctionScope => _isFunctionScope;

    internal void SetBodyLexicalNames(HashSet<Symbol> names)
    {
        _bodyLexicalNames = names;
    }

    internal bool HasBodyLexicalName(Symbol name)
    {
        return _bodyLexicalNames is not null && _bodyLexicalNames.Contains(name);
    }

    internal void SetSimpleCatchParameters(HashSet<Symbol> names)
    {
        _simpleCatchParameters = names;
    }

    internal bool IsSimpleCatchParameter(Symbol name)
    {
        return _simpleCatchParameters is not null && _simpleCatchParameters.Contains(name);
    }

    internal JsEnvironment? Enclosing => _enclosing;

    public bool TryGet(Symbol name, out object? value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current._enclosing is null &&
                    current._values.TryGetValue(Symbol.This, out var thisBinding) &&
                    thisBinding.Value is JsObject globalObject &&
                    globalObject.TryGetProperty(name.Name, out var globalValue))
                {
                    value = globalValue;
                    return true;
                }

                value = binding.Value;
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out value))
            {
                return true;
            }

            current = current._enclosing;
        }

        if (_values.TryGetValue(Symbol.This, out var rootThis) &&
            rootThis.Value is JsObject rootGlobal &&
            rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        value = null;
        return false;
    }

    internal bool TryFindBinding(Symbol name, out JsEnvironment environment, out object? value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                environment = current;
                value = binding.Value;
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out value))
            {
                environment = current;
                return true;
            }

            current = current._enclosing;
        }

        environment = null!;
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
        if (_enclosing is null && _values.TryGetValue(Symbol.This, out var thisBinding) &&
            thisBinding.Value is JsObject global)
        {
            globalObject = global;
        }

        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError($"Cannot reassign constant '{name.Name}'."));
            }

            if (binding.IsGlobalConstant)
            {
                if (isStrictContext)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable"));
                }

                return;
            }

            binding.Value = value;
            globalObject?.SetProperty(name.Name, value);
            NotifyBindingObservers(name, value);
            return;
        }

        if (_withObject is not null && !IsUnscopable(_withObject, name.Name) &&
            _withObject.TryGetProperty(name.Name, out _))
        {
            _withObject.SetProperty(name.Name, value);
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

    private static bool IsUnscopable(JsObject target, string name)
    {
        var unscopablesSymbol = TypedAstSymbol.For("Symbol.unscopables");
        var key = $"@@symbol:{unscopablesSymbol.GetHashCode()}";
        if (target.TryGetProperty(key, out var unscopables) && unscopables is IJsPropertyAccessor accessor &&
            JsOps.TryGetPropertyValue(accessor, name, out var blocked) && JsOps.ToBoolean(blocked))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetFromWith(JsObject target, Symbol name, out object? value)
    {
        if (IsUnscopable(target, name.Name))
        {
            value = null;
            return false;
        }

        if (target.TryGetProperty(name.Name, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        value = null;
        return false;
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

    internal bool HasFunctionScopedBinding(Symbol name)
    {
        var scope = GetFunctionScope();
        return scope._values.TryGetValue(name, out var binding) && !binding.IsLexical;
    }

    internal bool IsGlobalFunctionScope => _isFunctionScope && _enclosing is null;

    internal JsEnvironment GetFunctionScope()
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
                    current.Depth
                ));
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
