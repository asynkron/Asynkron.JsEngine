using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment
{
    private const int MaxDepth = 1_000;
    internal static readonly object Uninitialized = new();
    private readonly SourceReference? _creatingSource;
    private readonly string? _description;

    private readonly Dictionary<Symbol, Binding> _values = new();
    private readonly IJsObjectLike? _withObject;
    private Dictionary<Symbol, List<Action<object?>>>? _bindingObservers;
    private HashSet<Symbol>? _bodyLexicalNames;
    private HashSet<Symbol>? _simpleCatchParameters;

    public JsEnvironment(
        JsEnvironment? enclosing = null,
        bool isFunctionScope = false,
        bool isStrict = false,
        SourceReference? creatingSource = null,
        string? description = null,
        IJsObjectLike? withObject = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        _creatingSource = creatingSource;
        _description = description;
        IsStrictLocal = isStrict;
        _withObject = withObject;
        IsParameterEnvironment = isParameterEnvironment;
        IsBodyEnvironment = isBodyEnvironment;

        Depth = (Enclosing?.Depth ?? -1) + 1;
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
    public bool IsStrict => IsStrictLocal || (Enclosing?.IsStrict ?? false);

    internal bool IsObjectEnvironment => _withObject is not null;

    internal bool IsParameterEnvironment { get; }

    internal bool IsBodyEnvironment { get; }

    internal bool IsFunctionScope { get; }

    internal JsEnvironment? Enclosing { get; }

    internal bool IsGlobalFunctionScope => IsFunctionScope && Enclosing is null;

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
        bool blocksFunctionScopeOverride = false,
        bool? globalVarConfigurable = null)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        var isGlobalScope = scope.Enclosing is null;
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
                             existingDescriptor is { IsDataDescriptor: true, Writable: true, Enumerable: true };
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
        var varBindingConfigurable = globalVarConfigurable ?? allowConfigurableGlobalBinding;

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
                            Configurable = varBindingConfigurable
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

                if (current.Enclosing is null &&
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

            current = current.Enclosing;
        }

        if (_values.TryGetValue(Symbol.This, out var rootThis) &&
            rootThis.Value is JsObject rootGlobal &&
            rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            return propertyValue;
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    internal object? GetDeclarative(Symbol name)
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

                if (current.Enclosing is null &&
                    current._values.TryGetValue(Symbol.This, out var thisBinding) &&
                    thisBinding.Value is JsObject globalObject &&
                    globalObject.TryGetProperty(name.Name, out var globalValue))
                {
                    return globalValue;
                }

                return binding.Value;
            }

            current = current.Enclosing;
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
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

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            return false;
        }

        return Enclosing?.IsConstBinding(name) ?? false;
    }

    internal bool HasBinding(Symbol name)
    {
        if (_values.ContainsKey(name))
        {
            return true;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            return true;
        }

        return Enclosing?.HasBinding(name) ?? false;
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
                if (current.Enclosing is null &&
                    current._values.TryGetValue(Symbol.This, out var thisBinding) &&
                    thisBinding.Value is JsObject globalObject)
                {
                    globalObject.SetProperty(name.Name, value);
                }

                return true;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                break;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal bool TryResolveWithBinding(
        Symbol name,
        EvaluationContext context,
        out ObjectEnvironmentBinding binding)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        var isStrictReference = context.CurrentScope.IsStrict;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values.ContainsKey(name))
            {
                break;
            }

            if (current._withObject is not null &&
                TryResolveObjectBinding(
                    current._withObject,
                    name,
                    out var propertyName,
                    out var allowMissingAssignment))
            {
                binding = new ObjectEnvironmentBinding(
                    current._withObject,
                    propertyName,
                    isStrictReference,
                    allowMissingAssignment);
                return true;
            }

            current = current.Enclosing;
        }

        binding = default;
        return false;
    }

    internal bool HasLexicalBinding(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding) && binding.IsLexical)
        {
            return true;
        }

        return Enclosing?.HasLexicalBinding(name) ?? false;
    }

    internal bool HasBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current.IsFunctionScope)
        {
            if (current._withObject is null && current._values.ContainsKey(name))
            {
                return true;
            }

            if (current is { IsFunctionScope: true, IsParameterEnvironment: false })
            {
                break;
            }

            current = current.Enclosing;
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

    internal bool HasLexicalBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current.IsFunctionScope)
        {
            if (current._values.TryGetValue(name, out var binding) &&
                binding.IsLexical)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

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

                if (current.Enclosing is null &&
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

            current = current.Enclosing;
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

            current = current.Enclosing;
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
        if (Enclosing is null && _values.TryGetValue(Symbol.This, out var thisBinding) &&
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
                    throw new ThrowSignal(
                        StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable"));
                }

                return;
            }

            binding.Value = value;
            globalObject?.SetProperty(name.Name, value);
            NotifyBindingObservers(name, value);
            return;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            _withObject.SetProperty(name.Name, value);
            return;
        }

        if (Enclosing is not null)
        {
            Enclosing.AssignInternal(name, value, isStrictContext);
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

    internal DeleteBindingResult DeleteBinding(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                return current._withObject.Delete(name.Name)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                return current.TryDeleteDeclarativeBinding(name, binding)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            current = current.Enclosing;
        }

        var globalObject = GetRootGlobalObject();
        if (globalObject is not null)
        {
            var descriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
            if (descriptor is not null)
            {
                if (!descriptor.Configurable)
                {
                    return DeleteBindingResult.NotDeletable;
                }

                globalObject.Delete(name.Name);
                return DeleteBindingResult.Deleted;
            }
        }

        return DeleteBindingResult.NotFound;
    }

    private bool TryDeleteDeclarativeBinding(Symbol name, Binding binding)
    {
        if (binding.IsLexical || binding.IsConst || binding.IsGlobalConstant || binding.BlocksFunctionScopeOverride)
        {
            return false;
        }

        if (IsFunctionScope)
        {
            if (Enclosing is not null)
            {
                // Function scopes (including parameters) cannot remove declarative bindings.
                return false;
            }

            var globalObject = GetRootGlobalObject();
            if (globalObject is null)
            {
                return false;
            }

            var descriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
            if (descriptor is not null && !descriptor.Configurable)
            {
                return false;
            }

            globalObject.Delete(name.Name);
            _values.Remove(name);
            return true;
        }

        return false;
    }

    private JsObject? GetRootGlobalObject()
    {
        var current = this;
        var hops = 0;
        const int maxDepth = 10_000;
        while (current.Enclosing is not null && hops++ < maxDepth)
        {
            current = current.Enclosing;
        }

        if (current._values.TryGetValue(Symbol.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            return globalObject;
        }

        return null;
    }

    private static bool IsBlockedByUnscopables(IJsObjectLike target, string name, out bool touchedUnscopables)
    {
        touchedUnscopables = false;
        var unscopablesSymbol = TypedAstSymbol.For("Symbol.unscopables");
        var key = $"@@symbol:{unscopablesSymbol.GetHashCode()}";
        if (target.TryGetProperty(key, out var unscopables))
        {
            touchedUnscopables = true;
            if (unscopables is IJsPropertyAccessor accessor &&
                JsOps.TryGetPropertyValue(accessor, name, out var blocked) && JsOps.ToBoolean(blocked))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFromWith(IJsObjectLike target, Symbol name, out object? value)
    {
        var propertyName = name.Name;
        if (string.IsNullOrEmpty(propertyName))
        {
            value = null;
            return false;
        }

        if (!HasProperty(target, propertyName))
        {
            value = null;
            return false;
        }

        if (IsBlockedByUnscopables(target, propertyName, out _))
        {
            value = null;
            return false;
        }

        if (target.TryGetProperty(propertyName, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        return target.TryGetProperty(propertyName, target, out value);
    }

    private static bool HasVisibleWithBinding(IJsObjectLike target, Symbol name)
    {
        return TryResolveObjectBinding(target, name, out _, out _);
    }

    private static bool TryResolveObjectBinding(
        IJsObjectLike target,
        Symbol name,
        out string propertyName,
        out bool allowMissingAssignment)
    {
        propertyName = name.Name;
        allowMissingAssignment = false;
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        if (!HasProperty(target, propertyName))
        {
            return false;
        }

        JsObject? jsObject = null;
        PropertyDescriptor? originalDescriptor = null;
        if (target is JsObject candidate)
        {
            jsObject = candidate;
            originalDescriptor = candidate.GetOwnPropertyDescriptor(propertyName);
        }

        var touchedUnscopables = false;
        if (IsBlockedByUnscopables(target, propertyName, out touchedUnscopables))
        {
            return false;
        }

        if (touchedUnscopables && jsObject is not null && originalDescriptor is not null)
        {
            var currentDescriptor = jsObject.GetOwnPropertyDescriptor(propertyName);
            if (currentDescriptor is null)
            {
                allowMissingAssignment = true;
            }
        }

        return true;
    }

    private static bool HasProperty(IJsObjectLike target, string name)
    {
        if (target is JsProxy proxy)
        {
            return proxy.HasProperty(name);
        }

        if (target is JsObject jsObject && jsObject.HasProperty(name))
        {
            return true;
        }

        if (target.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        var prototype = target.Prototype;
        while (prototype is not null)
        {
            if (prototype.HasProperty(name))
            {
                return true;
            }

            prototype = prototype.Prototype;
        }

        return target.TryGetProperty(name, out _);
    }

    internal static object? GetWithBindingValue(in ObjectEnvironmentBinding binding)
    {
        var propertyName = binding.PropertyName;
        if (!HasProperty(binding.BindingObject, propertyName))
        {
            if (binding.IsStrictReference)
            {
                throw new InvalidOperationException($"ReferenceError: {propertyName} is not defined");
            }

            return Symbol.Undefined;
        }

        return JsOps.TryGetPropertyValue(binding.BindingObject, propertyName, out var value)
            ? value
            : Symbol.Undefined;
    }

    internal static bool TrySetWithBindingValue(in ObjectEnvironmentBinding binding, object? value)
    {
        var propertyName = binding.PropertyName;
        var bindingObject = binding.BindingObject;
        var stillExists = HasProperty(bindingObject, propertyName);
        if (!stillExists)
        {
            if (binding.IsStrictReference)
            {
                throw new InvalidOperationException($"ReferenceError: {propertyName} is not defined");
            }

            if (!binding.AllowMissingAssignment)
            {
                return false;
            }
        }

        JsOps.AssignPropertyValueByName(bindingObject, propertyName, value);

        var ownDescriptor = bindingObject.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor is null)
        {
            return true;
        }

        if (bindingObject is not IPropertyDefinitionHost definitionHost)
        {
            return true;
        }

        if (!ownDescriptor.IsDataDescriptor)
        {
            return true;
        }

        var descriptorClone = ownDescriptor.Clone();
        descriptorClone.Value = value;
        definitionHost.TryDefineProperty(propertyName, descriptorClone);
        return true;
    }

    internal void AddBindingObserver(Symbol symbol, Action<object?> observer)
    {
        _bindingObservers ??= new Dictionary<Symbol, List<Action<object?>>>(ReferenceEqualityComparer<Symbol>.Instance);
        if (!_bindingObservers.TryGetValue(symbol, out var list))
        {
            list = [];
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

    internal JsEnvironment GetFunctionScope()
    {
        var current = this;
        while (!current.IsFunctionScope)
        {
            current = current.Enclosing
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

            current = current.Enclosing;
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
            current = current.Enclosing;
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

internal enum DeleteBindingResult
{
    NotFound,
    Deleted,
    NotDeletable
}

internal readonly record struct ObjectEnvironmentBinding(
    IJsObjectLike BindingObject,
    string PropertyName,
    bool IsStrictReference,
    bool AllowMissingAssignment);
