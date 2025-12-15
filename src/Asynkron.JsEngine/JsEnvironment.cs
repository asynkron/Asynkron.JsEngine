using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Collections;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment
{
    private const int MaxDepth = 1_000;
    internal static readonly object Uninitialized = new();
    private SourceReference? _creatingSource;
    private bool _inheritStrictness;
    private string? _description;
    private bool _treatAsGlobalFunctionScope;

    private SymbolHybridDictionary<Binding>? _values;

    /// <summary>
    /// Gets the values dictionary, creating it if necessary.
    /// Use this when you need to add bindings.
    /// </summary>
    private SymbolHybridDictionary<Binding> Values
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values ??= new();
    }

    private IJsObjectLike? _withObject;
    private Dictionary<Symbol, ResolvedIdentifierBinding>? _identifierBindingCache;
    private Dictionary<Symbol, List<Action<object?>>>? _bindingObservers;
    private HashSet<Symbol>? _bodyLexicalNames;
    private HashSet<Symbol>? _simpleCatchParameters;

    internal RealmState? RealmState { get; private set; }
    private JsEnvironment? _varEnvironmentOverride;
    internal bool IsAsyncModule { get; set; }
    internal string? ModulePath { get; set; }

    public JsEnvironment(
        JsEnvironment? enclosing = null,
        bool isFunctionScope = false,
        bool isStrict = false,
        SourceReference? creatingSource = null,
        string? description = null,
        IJsObjectLike? withObject = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false,
        bool treatAsGlobalFunctionScope = false,
        bool inheritStrictness = true)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        _creatingSource = creatingSource;
        _description = description;
        IsStrictLocal = isStrict;
        _withObject = withObject;
        IsParameterEnvironment = isParameterEnvironment;
        IsBodyEnvironment = isBodyEnvironment;
        _treatAsGlobalFunctionScope = treatAsGlobalFunctionScope;
        _inheritStrictness = inheritStrictness;
        RealmState = enclosing?.RealmState;
        ModulePath = enclosing?.ModulePath;
        IsAsyncModule = enclosing?.IsAsyncModule ?? false;

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
    public int Depth { get; private set; }

    private bool IsStrictLocal { get; set; }

    /// <summary>
    ///     Returns true if this environment or any enclosing environment is in strict mode.
    /// </summary>
    public bool IsStrict => IsStrictLocal || (_inheritStrictness && (Enclosing?.IsStrict ?? false));

    internal bool IsObjectEnvironment => _withObject is not null;

    internal bool IsParameterEnvironment { get; private set; }

    internal bool IsBodyEnvironment { get; private set; }

    internal bool IsFunctionScope { get; private set; }

    /// <summary>
    ///     When true, indicates this environment belongs to a default derived constructor
    ///     where argument spreading should bypass the iterator protocol per ES spec 15.7.14.
    ///     Walks up the enclosing chain to find the flag if not set locally.
    /// </summary>
    internal bool IsDefaultDerivedConstructor
    {
        get => _isDefaultDerivedConstructor || (Enclosing?.IsDefaultDerivedConstructor ?? false);
        set => _isDefaultDerivedConstructor = value;
    }

    private bool _isDefaultDerivedConstructor;

    internal JsEnvironment? Enclosing { get; private set; }

    internal void SetRealmState(RealmState realmState)
    {
        RealmState = realmState;
    }

    /// <summary>
    ///     Resets the environment for reuse from a pool.
    ///     Sets new enclosing and clears all bindings.
    /// </summary>
    internal void Reset(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        IsStrictLocal = isStrict;
        _creatingSource = creatingSource;
        _description = description;
        _values?.Clear();
        _identifierBindingCache?.Clear();
        _bindingObservers?.Clear();
        _bodyLexicalNames?.Clear();
        _simpleCatchParameters?.Clear();
        _isDefaultDerivedConstructor = false;
        _varEnvironmentOverride = null;
        _withObject = null;
        IsParameterEnvironment = isParameterEnvironment;
        IsBodyEnvironment = isBodyEnvironment;
        _treatAsGlobalFunctionScope = false;
        _inheritStrictness = true;
        RealmState = enclosing?.RealmState;
        ModulePath = enclosing?.ModulePath;
        IsAsyncModule = enclosing?.IsAsyncModule ?? false;
        Depth = (enclosing?.Depth ?? -1) + 1;
    }

    internal bool IsGlobalFunctionScope => _treatAsGlobalFunctionScope || (IsFunctionScope && Enclosing is null);

    /// <summary>
    /// Defines a binding with an object value. Consider using DefineJsValue instead to avoid boxing primitives.
    /// </summary>
    [Obsolete("Use DefineJsValue to avoid boxing primitives")]
    public void Define(
        Symbol name,
        object? value,
        bool isConst = false,
        bool isGlobalConstant = false,
        bool isLexical = true,
        bool blocksFunctionScopeOverride = false,
        bool canDelete = false,
        bool isImmutableBinding = false)
    {
        if (_values is not null && _values.TryGetValue(name, out var existing) && existing.IsGlobalConstant)
        {
            return;
        }

        ref var binding = ref Values.GetValueRefOrNullRef(name);
        if (!Unsafe.IsNullRef(ref binding))
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
                    binding = new Binding(value, isConst, isGlobalConstant, isLexical,
                        blocksFunctionScopeOverride, canDelete, isImmutableBinding);
                }

                return;
            }

            binding.JsValue = JsValue.FromObject(value);
            binding.UpgradeLexical(isLexical, blocksFunctionScopeOverride);
            NotifyBindingObservers(name, value);
            return;
        }

        Values[name] = new Binding(value, isConst, isGlobalConstant, isLexical, blocksFunctionScopeOverride,
            canDelete, isImmutableBinding);
        NotifyBindingObservers(name, value);
    }

    /// <summary>
    /// Defines a binding with a JsValue directly, avoiding boxing for primitives.
    /// </summary>
    public void DefineJsValue(
        Symbol name,
        JsValue value,
        bool isConst = false,
        bool isGlobalConstant = false,
        bool isLexical = true,
        bool blocksFunctionScopeOverride = false,
        bool canDelete = false,
        bool isImmutableBinding = false)
    {
        if (_values is not null && _values.TryGetValue(name, out var existing) && existing.IsGlobalConstant)
        {
            return;
        }

        ref var binding = ref Values.GetValueRefOrNullRef(name);
        if (!Unsafe.IsNullRef(ref binding))
        {
            if (binding.IsConst || binding.IsGlobalConstant)
            {
                if (isLexical && blocksFunctionScopeOverride)
                {
                    binding = new Binding(value, isConst, isGlobalConstant, isLexical,
                        blocksFunctionScopeOverride, canDelete, isImmutableBinding);
                }

                return;
            }

            binding.JsValue = value;
            binding.UpgradeLexical(isLexical, blocksFunctionScopeOverride);
            // Only notify if there are observers (avoid ToObject boxing in hot path)
            if (_bindingObservers is not null)
            {
                NotifyBindingObservers(name, value.ToObject());
            }
            return;
        }

        Values[name] = new Binding(value, isConst, isGlobalConstant, isLexical, blocksFunctionScopeOverride,
            canDelete, isImmutableBinding);
        // Only notify if there are observers (avoid ToObject boxing in hot path)
        if (_bindingObservers is not null)
        {
            NotifyBindingObservers(name, value.ToObject());
        }
    }

    internal void DefineExportPromiseBinding(Symbol name, JsPromise promise, bool isLexical, bool isConst)
    {
        if (_values is not null && _values.ContainsKey(name))
        {
            return;
        }

        Values[name] = Binding.CreateAsyncExport(promise, isConst, isLexical);
    }

    /// <summary>
    /// Defines an import binding that indirectly references a binding in another module's environment.
    /// Import bindings are immutable - they always read from the source module.
    /// </summary>
    internal void DefineImportBinding(Symbol localName, JsEnvironment sourceEnvironment, Symbol bindingName)
    {
        Values[localName] = Binding.CreateImport(sourceEnvironment, bindingName);
    }

    public void DefineFunctionScoped(
        Symbol name,
        object? value,
        bool hasInitializer,
        bool isFunctionDeclaration = false,
        bool? globalFunctionConfigurable = null,
        EvaluationContext? context = null,
        bool blocksFunctionScopeOverride = false,
        bool? globalVarConfigurable = null,
        bool allowExistingGlobalFunctionRedeclaration = false,
        bool canDelete = false)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        var isGlobalScope = scope.IsGlobalFunctionScope;
        JsObject? globalThis = null;
        PropertyDescriptor? existingDescriptor = null;
        object? existingGlobalValue = null;
        var hasLooseGlobalValue = false;
        var allowDelete = canDelete || context is { ExecutionKind: ExecutionKind.Eval } && !isGlobalScope;
        if (isGlobalScope)
        {
            globalThis = scope.GetRootGlobalObject();
            if (globalThis is not null)
            {
                existingDescriptor = globalThis.GetOwnPropertyDescriptor(name.Name);
                if (existingDescriptor is not null)
                {
                    if (globalThis.TryGetProperty(name.Name, out var jsValue))
                    {
                        existingGlobalValue = jsValue.ToObject();
                    }
                }
                else if (globalThis.TryGetValue(name.Name, out var looseValue))
                {
                    // Use TryGetValue instead of TryGetProperty to avoid invoking
                    // inherited accessors like Object.prototype.__proto__
                    existingGlobalValue = looseValue;
                    hasLooseGlobalValue = true;
                }
            }
        }

        var canDeclareFunction = true;
        var isRestrictedGlobal = isGlobalScope &&
                                 existingDescriptor is { Configurable: false } descriptor &&
                                 (!descriptor.IsDataDescriptor || !descriptor.Writable);

        if (isGlobalScope && isFunctionDeclaration)
        {
            if (isRestrictedGlobal)
            {
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }

            canDeclareFunction = existingDescriptor switch
            {
                null => globalThis?.IsExtensible != false,
                { Configurable: true } => true,
                _ => !existingDescriptor.IsAccessorDescriptor &&
                     existingDescriptor.Writable &&
                     existingDescriptor.Enumerable
            };

            if (!canDeclareFunction)
            {
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }
        }

        if (isGlobalScope &&
            !isFunctionDeclaration &&
            existingDescriptor is null &&
            globalThis is not null &&
            !globalThis.IsExtensible)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot declare global variable '{name.Name}' on a non-extensible global object.",
                context,
                context?.RealmState);
        }

        // Per ES spec GlobalDeclarationInstantiation step 6:
        // If envRec.HasLexicalDeclaration(name) is true, throw a SyntaxError.
        // This check must happen before checking _values because lexical bindings
        // may be tracked separately in bodyLexicalNames across script evaluations.
        // We need to check both the current scope AND enclosing scopes because
        // strict scripts create wrapper environments around the global scope.
        if (isGlobalScope && HasGlobalLexicalName(scope, name))
        {
            throw StandardLibrary.ThrowSyntaxError(
                $"Identifier '{name.Name}' has already been declared",
                context,
                context?.RealmState);
        }

        ref var existing = ref scope.Values.GetValueRefOrNullRef(name);
        if (!Unsafe.IsNullRef(ref existing))
        {
            // Also check existing lexical bindings in the local scope
            if (isGlobalScope && existing.IsLexical)
            {
                throw StandardLibrary.ThrowSyntaxError(
                    $"Identifier '{name.Name}' has already been declared",
                    context,
                    context?.RealmState);
            }

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
                    existing.JsValue = JsValue.FromObject(value);
                    if (isGlobalScope && globalThis is not null)
                    {
                        globalThis.SetProperty(name.Name, JsValue.FromObject(value));
                    }
                }

                return;
            }

            if (hasInitializer)
            {
                existing.JsValue = JsValue.FromObject(value);
                if (isGlobalScope && globalThis is not null)
                {
                    globalThis.SetProperty(name.Name, JsValue.FromObject(value));
                }
            }

            return;
        }

        var allowConfigurableGlobalBinding =
            context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
        var varBindingConfigurable = globalVarConfigurable ?? allowConfigurableGlobalBinding;

        var initialValue = value;
        var shouldWriteGlobal = true;

        if (isGlobalScope && !hasInitializer && (existingDescriptor is not null || hasLooseGlobalValue))
        {
            initialValue = existingGlobalValue;
            shouldWriteGlobal = false;
        }

        scope.Values[name] = new Binding(initialValue, false, false, false, blocksFunctionScopeOverride, allowDelete);
        if (isGlobalScope && globalThis is not null && shouldWriteGlobal)
        {
            if (isFunctionDeclaration)
            {
                var configurable = globalFunctionConfigurable ?? allowConfigurableGlobalBinding;
                if (existingDescriptor is null)
                {
                    if (!globalThis.TryDefineProperty(
                            name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot declare global function '{name.Name}'.",
                            context,
                            context?.RealmState);
                    }
                }
                else if (existingDescriptor.Configurable)
                {
                    if (!globalThis.TryDefineProperty(
                            name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot redeclare global function '{name.Name}'.",
                            context,
                            context?.RealmState);
                    }
                }
                else
                {
                    // Existing non-configurable property: update value only (CreateGlobalFunctionBinding step 6).
                    if (!globalThis.TryDefineProperty(
                            name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue
                        }))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot update global function binding for '{name.Name}'.",
                            context,
                            context?.RealmState);
                    }
                }
            }
            else
            {
                if (existingDescriptor is null)
                {
                    if (!globalThis.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue,
                            Writable = true,
                            Enumerable = true,
                            Configurable = varBindingConfigurable
                        }))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot declare global variable '{name.Name}'.",
                            context,
                            context?.RealmState);
                    }
                }
                else
                {
                    globalThis.SetProperty(name.Name, JsValue.FromObject(initialValue));
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
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                if (binding.IsUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope &&
                    !binding.IsLexical)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        return globalValue;
                    }
                }

                return binding.JsValue.ToObject();
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                return current._varEnvironmentOverride.Get(name);
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out var withValue))
            {
                return withValue;
            }

            current = current.Enclosing;
        }

        if (IsGlobalFunctionScope)
        {
            var rootGlobal = GetRootGlobalObject();
            if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
            {
                return propertyValue;
            }
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    /// <summary>
    /// Gets a binding value as JsValue, avoiding boxing for primitives.
    /// </summary>
    public JsValue GetJsValue(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                // Check IsUninitialized before reading
                if (binding.IsUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope &&
                    !binding.IsLexical)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        return JsValue.FromObject(globalValue);
                    }
                }

                return binding.JsValue;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                return current._varEnvironmentOverride.GetJsValue(name);
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out var withValue))
            {
                return JsValue.FromObject(withValue);
            }

            current = current.Enclosing;
        }

        if (IsGlobalFunctionScope)
        {
            var rootGlobal = GetRootGlobalObject();
            if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
            {
                return JsValue.FromObject(propertyValue);
            }
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
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                // Check IsUninitialized before reading
                if (binding.IsUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope &&
                    !binding.IsLexical)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        return globalValue;
                    }
                }

                return binding.JsValue.ToObject();
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                return current._varEnvironmentOverride.GetDeclarative(name);
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
        if (_values is not null && _values.TryGetValue(name, out var binding))
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
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values is not null && current._values.ContainsKey(name))
            {
                return true;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                return true;
            }

            var next = current.Enclosing;
            if (ReferenceEquals(next, current))
            {
                break;
            }

            current = next;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasOwnBinding(Symbol name)
    {
        return _values is not null && _values.ContainsKey(name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasOwnLexicalBinding(Symbol name)
    {
        return _values is not null && _values.TryGetValue(name, out var binding) && binding.IsLexical;
    }

    internal bool TryAssignBlockedBinding(Symbol name, object? value)
    {
        var current = this;
        var passedFunctionBoundary = false;
        while (current is not null)
        {
            if (current.IsFunctionScope)
            {
                if (passedFunctionBoundary)
                {
                    break;
                }

                passedFunctionBoundary = true;
            }

            if (current._values is not null)
            {
                ref var binding = ref current._values.GetValueRefOrNullRef(name);
                if (!Unsafe.IsNullRef(ref binding) && binding.BlocksFunctionScopeOverride)
                {
                    binding.JsValue = JsValue.FromObject(value);
                    current.NotifyBindingObservers(name, value);
                    if (current.IsGlobalFunctionScope)
                    {
                        var globalObject = current.GetRootGlobalObject();
                        globalObject?.SetProperty(name.Name, JsValue.FromObject(value));
                    }

                    return true;
                }
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
        var isStrictReference = IsStrict || context.CurrentScope.IsStrict || context.IsStrictSource;

        while (current is not null && hops++ < maxLookupDepth)
        {
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

            if (current._values is not null && current._values.ContainsKey(name))
            {
                break;
            }

            current = current.Enclosing;
        }

        binding = default;
        return false;
    }

    internal AssignmentReference ResolveIdentifierAssignmentReference(Symbol name, EvaluationContext context)
    {
        var strictContext = context.CurrentScope.IsStrict;

        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return AssignmentReference.ForDeclarativeBinding(cached, name, context, strictContext);
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return AssignmentReference.ForDeclarativeBinding(cachedBinding, name, context, strictContext);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return AssignmentReference.ForGlobalBinding(globalBinding, context);
        }

        return AssignmentReference.ForUnresolvable(name, context, strictContext, this);
    }

    /// <summary>
    ///     Direct identifier resolution for read accesses without allocating AssignmentReference delegates.
    ///     Mirrors the semantics of <see cref="ResolveIdentifierAssignmentReference" /> for GetValue.
    /// </summary>
    [Obsolete("Use GetIdentifierJsValue to avoid boxing primitives")]
    internal object? GetIdentifierValue(Symbol name, EvaluationContext context)
    {
        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return cached.Read(name, context);
        }

        // Fast path: skip TryResolveWithBinding when AllowIdentifierCache is true (no with/eval in scope)
        if (!context.AllowIdentifierCache && TryResolveWithBinding(name, context, out var withBinding))
        {
            return GetWithBindingValue(withBinding);
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return cachedBinding.Read(name, context);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return GetWithBindingValue(globalBinding);
        }

        return ReadUnresolvable(name);
    }

    /// <summary>
    /// Direct identifier resolution that returns JsValue, avoiding boxing for primitives.
    /// </summary>
    internal JsValue GetIdentifierJsValue(Symbol name, EvaluationContext context)
    {
        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return cached.ReadJsValue(name, context);
        }

        // Fast path: when AllowIdentifierCache is true, we know there's no with/eval in scope,
        // so skip the expensive TryResolveWithBinding check entirely
        if (!context.AllowIdentifierCache && TryResolveWithBinding(name, context, out var withBinding))
        {
            return JsValue.FromObject(GetWithBindingValue(withBinding));
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return cachedBinding.ReadJsValue(name, context);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return JsValue.FromObject(GetWithBindingValue(globalBinding));
        }

        return JsValue.FromObject(ReadUnresolvable(name));
    }

    /// <summary>
    /// Tries to resolve an identifier and return its value as JsValue.
    /// Returns false if the identifier is not found (instead of throwing).
    /// This is the fast path for identifier evaluation in hot loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetIdentifierJsValue(Symbol name, EvaluationContext context, out JsValue value)
    {
        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            value = cached.ReadJsValue(name, context);
            return true;
        }

        // Fast path: skip TryResolveWithBinding when AllowIdentifierCache is true (no with/eval in scope)
        if (!context.AllowIdentifierCache && TryResolveWithBinding(name, context, out var withBinding))
        {
            value = JsValue.FromObject(GetWithBindingValue(withBinding));
            return true;
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            value = cachedBinding.ReadJsValue(name, context);
            return true;
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            value = JsValue.FromObject(GetWithBindingValue(globalBinding));
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Direct identifier assignment that avoids creating AssignmentReference structs.
    /// This is the fast path for simple identifier assignments in loops.
    /// </summary>
    internal void SetIdentifierJsValue(Symbol name, JsValue value, EvaluationContext context)
    {
        var isStrictContext = context.CurrentScope.IsStrict;

        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            cached.WriteJsValue(name, value, isStrictContext);
            return;
        }

        // Fast path: skip TryResolveWithBinding when AllowIdentifierCache is true (no with/eval in scope)
        if (!context.AllowIdentifierCache && TryResolveWithBinding(name, context, out var withBinding))
        {
            if (isStrictContext && IsStrictRestrictedName(name))
            {
                throw new ThrowSignal(JsValue.FromObject(StdLib.StandardLibrary.CreateSyntaxError(
                    "Assignment to eval or arguments is not allowed in strict mode.", context,
                    context.RealmState)));
            }

            var objValue = value.ToObject();
            if (!TrySetWithBindingValue(withBinding, objValue, context.RealmState))
            {
                Assign(name, objValue);
            }
            return;
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            cachedBinding.WriteJsValue(name, value, isStrictContext);
            return;
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            TrySetWithBindingValue(globalBinding, value.ToObject(), context.RealmState);
            return;
        }

        AssignUnresolvable(name, value.ToObject(), isStrictContext, context, this);
    }

    private static bool IsStrictRestrictedName(Symbol name)
    {
        return string.Equals(name.Name, "eval", StringComparison.Ordinal) ||
               string.Equals(name.Name, "arguments", StringComparison.Ordinal);
    }

    private bool TryGetCachedDeclarativeBinding(
        Symbol name,
        EvaluationContext context,
        out ResolvedIdentifierBinding binding)
    {
        if (!context.AllowIdentifierCache || _identifierBindingCache is null)
        {
            binding = default;
            return false;
        }

        return _identifierBindingCache.TryGetValue(name, out binding);
    }

    private void CacheDeclarativeBinding(
        Symbol name,
        ResolvedIdentifierBinding binding,
        EvaluationContext context)
    {
        if (!context.AllowIdentifierCache)
        {
            return;
        }

        _identifierBindingCache ??=
            new Dictionary<Symbol, ResolvedIdentifierBinding>(ReferenceEqualityComparer<Symbol>.Instance);
        _identifierBindingCache[name] = binding;
    }

    internal readonly struct ResolvedIdentifierBinding
    {
        private readonly JsEnvironment _environment;
        private readonly Symbol _name;

        internal ResolvedIdentifierBinding(JsEnvironment environment, Symbol name)
        {
            _environment = environment;
            _name = name;
        }

        [Obsolete("Use ReadJsValue to avoid boxing primitives")]
        internal object? Read(Symbol name, EvaluationContext context)
        {
            if (_environment._values is null)
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            ref var binding = ref _environment._values.GetValueRefOrNullRef(_name);
            if (Unsafe.IsNullRef(ref binding))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            return ReadResolvedBindingValue(_environment, ref binding, _name);
        }

        /// <summary>
        /// Reads the binding value as JsValue, avoiding boxing for primitives.
        /// </summary>
        internal JsValue ReadJsValue(Symbol name, EvaluationContext context)
        {
            if (_environment._values is null)
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            ref var binding = ref _environment._values.GetValueRefOrNullRef(_name);
            if (Unsafe.IsNullRef(ref binding))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            return ReadResolvedBindingJsValue(_environment, ref binding, _name);
        }

        internal void Write(Symbol name, object? value, bool isStrictContext, EvaluationContext context)
        {
            Write(name, value, isStrictContext);
        }

        /// <summary>
        /// Writes the binding value without requiring an EvaluationContext.
        /// This is safe for async contexts where the original context may be stale.
        /// </summary>
        internal void Write(Symbol name, object? value, bool isStrictContext)
        {
            if (_environment._values is null)
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            ref var binding = ref _environment._values.GetValueRefOrNullRef(_name);
            if (Unsafe.IsNullRef(ref binding))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            _environment.WriteResolvedBindingValue(_environment, ref binding, _name, value, isStrictContext);
        }

        /// <summary>
        /// Writes the binding value as JsValue, avoiding boxing for primitives.
        /// This is safe for async contexts where the original context may be stale.
        /// </summary>
        internal void WriteJsValue(Symbol name, JsValue value, bool isStrictContext)
        {
            if (_environment._values is null)
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            ref var binding = ref _environment._values.GetValueRefOrNullRef(_name);
            if (Unsafe.IsNullRef(ref binding))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            _environment.WriteResolvedBindingJsValue(_environment, ref binding, _name, value, isStrictContext);
        }
    }

    private static object? ReadResolvedBindingValue(JsEnvironment bindingEnvironment, ref Binding binding, Symbol name)
    {
        // Check IsUninitialized before reading
        if (binding.IsUninitialized)
        {
            throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
        }

        // Check for live export bindings
        if (binding.LiveExportBindingOrNull is { } liveBinding)
        {
            return liveBinding.GetValue();
        }

        // Use binding.JsValue for logging to avoid boxing, then convert at the end
        bindingEnvironment.RealmState?.Logger?.LogInformation(
            "Read binding '{Name}' (envDepth={Depth}, lexical={Lexical}, bindingHash={Hash}) -> {Value}",
            name.Name,
            bindingEnvironment.Depth,
            binding.IsLexical,
            binding.GetHashCode(),
            binding.JsValue);

        if (bindingEnvironment.IsGlobalFunctionScope && !binding.IsLexical)
        {
            var globalObject = bindingEnvironment.GetRootGlobalObject();
            if (globalObject is not null && globalObject.TryGetProperty(name.Name, out var globalValue))
            {
                return globalValue.ToObject();
            }
        }

        // Only call ToObject() once at the very end
        return binding.JsValue.ToObject();
    }

    /// <summary>
    /// Reads a resolved binding value as JsValue, avoiding boxing for primitives.
    /// </summary>
    private static JsValue ReadResolvedBindingJsValue(JsEnvironment bindingEnvironment, ref Binding binding, Symbol name)
    {
        // Check IsUninitialized before reading
        if (binding.IsUninitialized)
        {
            throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
        }

        // Check for live export bindings
        if (binding.LiveExportBindingOrNull is { } liveBinding)
        {
            return JsValue.FromObject(liveBinding.GetValue());
        }

        // Use binding.JsValue for logging to avoid boxing
        bindingEnvironment.RealmState?.Logger?.LogInformation(
            "Read binding '{Name}' (envDepth={Depth}, lexical={Lexical}, bindingHash={Hash}) -> {Value}",
            name.Name,
            bindingEnvironment.Depth,
            binding.IsLexical,
            binding.GetHashCode(),
            binding.JsValue);

        if (bindingEnvironment.IsGlobalFunctionScope && !binding.IsLexical)
        {
            var globalObject = bindingEnvironment.GetRootGlobalObject();
            if (globalObject is not null && globalObject.TryGetProperty(name.Name, out var globalValue))
            {
                return JsValue.FromObject(globalValue);
            }
        }

        return binding.JsValue;
    }

    private void WriteResolvedBindingValue(
        JsEnvironment bindingEnvironment,
        ref Binding binding,
        Symbol name,
        object? value,
        bool isStrictContext)
    {
        RealmState?.Logger?.LogInformation(
            "Write binding '{Name}' (envDepth={Depth}, lexical={Lexical}, const={Const}, strictCtx={StrictCtx}, bindingHash={Hash}) = {Value}",
            name.Name,
            bindingEnvironment.Depth,
            binding.IsLexical,
            binding.IsConst,
            isStrictContext,
            binding.GetHashCode(),
            value);
        var realm = bindingEnvironment.RealmState ?? bindingEnvironment.Enclosing?.RealmState;

        // Check IsUninitialized before reading
        if (binding.IsUninitialized &&
            binding.IsLexical &&
            !Equals(name, Symbol.This))
        {
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (binding.IsConst)
        {
            // Per ES spec, assignment to const always throws TypeError regardless of strict mode
            throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError(
                $"Cannot reassign constant '{name.Name}'.", realm: realm)));
        }

        if (binding.IsImmutableBinding)
        {
            // Immutable bindings (named function expression names) throw in strict mode,
            // but silently fail in non-strict mode
            var bindingIsStrict = bindingEnvironment.IsStrict || bindingEnvironment.GetFunctionScope().IsStrict;
            if (bindingIsStrict || isStrictContext)
            {
                throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError(
                    $"Cannot reassign constant '{name.Name}'.", realm: realm)));
            }

            return;
        }

        if (binding.IsGlobalConstant)
        {
            if (isStrictContext)
            {
                throw new ThrowSignal(JsValue.FromObject(
                    StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable", realm: realm)));
            }

            return;
        }

        binding.JsValue = JsValue.FromObject(value);
        if (!binding.IsLexical && bindingEnvironment.IsGlobalFunctionScope)
        {
            bindingEnvironment.GetRootGlobalObject()?.SetProperty(name.Name, JsValue.FromObject(value));
        }

        bindingEnvironment.NotifyBindingObservers(name, value);
    }

    private void WriteResolvedBindingJsValue(
        JsEnvironment bindingEnvironment,
        ref Binding binding,
        Symbol name,
        JsValue value,
        bool isStrictContext)
    {
        RealmState?.Logger?.LogInformation(
            "Write binding '{Name}' (envDepth={Depth}, lexical={Lexical}, const={Const}, strictCtx={StrictCtx}, bindingHash={Hash}) = {Value}",
            name.Name,
            bindingEnvironment.Depth,
            binding.IsLexical,
            binding.IsConst,
            isStrictContext,
            binding.GetHashCode(),
            value);
        var realm = bindingEnvironment.RealmState ?? bindingEnvironment.Enclosing?.RealmState;

        // Check IsUninitialized before reading
        if (binding.IsUninitialized &&
            binding.IsLexical &&
            !Equals(name, Symbol.This))
        {
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (binding.IsConst)
        {
            // Per ES spec, assignment to const always throws TypeError regardless of strict mode
            throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError(
                $"Cannot reassign constant '{name.Name}'.", realm: realm)));
        }

        if (binding.IsImmutableBinding)
        {
            // Immutable bindings (named function expression names) throw in strict mode,
            // but silently fail in non-strict mode
            var bindingIsStrict = bindingEnvironment.IsStrict || bindingEnvironment.GetFunctionScope().IsStrict;
            if (bindingIsStrict || isStrictContext)
            {
                throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError(
                    $"Cannot reassign constant '{name.Name}'.", realm: realm)));
            }

            return;
        }

        if (binding.IsGlobalConstant)
        {
            if (isStrictContext)
            {
                throw new ThrowSignal(JsValue.FromObject(
                    StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable", realm: realm)));
            }

            return;
        }

        // Use JsValue directly to avoid boxing
        binding.JsValue = value;
        if (!binding.IsLexical && bindingEnvironment.IsGlobalFunctionScope)
        {
            bindingEnvironment.GetRootGlobalObject()?.SetProperty(name.Name, value);
        }

        // Only notify if there are observers (avoid ToObject boxing in hot path)
        if (bindingEnvironment._bindingObservers is not null)
        {
            bindingEnvironment.NotifyBindingObservers(name, value.ToObject());
        }
    }

    internal static object ReadUnresolvable(Symbol name)
    {
        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    internal static void AssignUnresolvable(Symbol name, object? value, bool isStrictContext, EvaluationContext context, JsEnvironment? environment = null)
    {
        var realm = environment?.RealmState ?? environment?.Enclosing?.RealmState ?? context.RealmState;
        if (isStrictContext)
        {
            context.RealmState?.Logger?.LogInformation(
                "AssignUnresolvable strict throw name={Name} scopeStrict={ScopeStrict} functionScopeStrict={FnStrict} env={Env}",
                name.Name,
                context.CurrentScope.IsStrict,
                environment?.GetFunctionScope().IsStrict ?? false,
                environment?.GetHashCode() ?? 0);
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (environment is null)
        {
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        var globalScope = environment;
        while (globalScope.Enclosing is not null)
        {
            globalScope = globalScope.Enclosing;
        }

        var globalObject = environment.GetRootGlobalObject();
        if (globalObject is null)
        {
            globalScope.DefineJsValue(name, JsValue.FromObject(value), isLexical: false, canDelete: true);
            return;
        }

        // Sloppy assignment to an unresolvable reference creates a new
        // configurable property on the global object rather than a declarative
        // binding so that `delete` can remove it (ES2024 9.1.1.3.4 SetMutableBinding).
        globalObject.SetProperty(name.Name, JsValue.FromObject(value));
        context.RealmState?.Logger?.LogInformation(
            "Sloppy assignment created unresolvable binding name={Name} valueType={ValueType}",
            name.Name,
            value?.GetType().Name ?? "null");
    }

    private bool TryLocateBinding(
        Symbol name,
        out JsEnvironment bindingEnvironment,
        out Binding binding)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values is not null && current._values.TryGetValue(name, out binding))
            {
                bindingEnvironment = current;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryLocateBinding(name, out bindingEnvironment, out binding))
            {
                return true;
            }

            current = current.Enclosing;
        }

        bindingEnvironment = null!;
        binding = default;
        return false;
    }

    private bool TryResolveGlobalObjectBinding(
        Symbol name,
        EvaluationContext context,
        out ObjectEnvironmentBinding binding)
    {
        binding = default;
        var globalObject = GetRootGlobalObject();
        if (globalObject is null)
        {
            return false;
        }

        if (!HasProperty(globalObject, name.Name))
        {
            return false;
        }

        var isStrictReference = IsStrict || context.CurrentScope.IsStrict || context.IsStrictSource;
        binding = new ObjectEnvironmentBinding(globalObject, name.Name, isStrictReference, AllowMissingAssignment: false);
        return true;
    }

    internal bool HasLexicalBinding(Symbol name)
    {
        if (_values is not null && _values.TryGetValue(name, out var binding) && binding.IsLexical)
        {
            return true;
        }

        return Enclosing?.HasLexicalBinding(name) ?? false;
    }

    internal bool HasBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null)
        {
            if (current._withObject is null && current._values is not null && current._values.ContainsKey(name))
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

        var descriptor = scope.GetGlobalOwnPropertyDescriptor(name, out var globalObject);
        if (globalObject is null)
        {
            return false;
        }

        return descriptor is not null && !descriptor.Configurable;
    }

    internal PropertyDescriptor? GetGlobalOwnPropertyDescriptor(Symbol name, out JsObject? globalObject)
    {
        globalObject = GetRootGlobalObject();
        return globalObject?.GetOwnPropertyDescriptor(name.Name);
    }

    internal bool HasLexicalBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current.IsFunctionScope)
        {
            if (current._values is not null && current._values.TryGetValue(name, out var binding) &&
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

    /// <summary>
    /// Merges the given lexical names into the existing set of body lexical names.
    /// This is used during global script evaluation to preserve lexical bindings
    /// from previous evalScript calls.
    /// </summary>
    internal void MergeBodyLexicalNames(HashSet<Symbol> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        if (_bodyLexicalNames is null)
        {
            _bodyLexicalNames = new HashSet<Symbol>(names, ReferenceEqualityComparer<Symbol>.Instance);
        }
        else
        {
            _bodyLexicalNames.UnionWith(names);
        }
    }

    internal bool HasBodyLexicalName(Symbol name)
    {
        return _bodyLexicalNames is not null && _bodyLexicalNames.Contains(name);
    }

    /// <summary>
    /// Checks if the given name has a var declaration in this environment or the global object.
    /// Used by GlobalDeclarationInstantiation step 5.a to detect lexical declarations that
    /// conflict with existing var declarations.
    /// </summary>
    public bool HasVarDeclaration(Symbol name)
    {
        // Check if there's a non-lexical binding in _values
        if (_values is not null && _values.TryGetValue(name, out var binding) && !binding.IsLexical)
        {
            if (binding.CanDelete && IsGlobalFunctionScope)
            {
                // Non-strict direct eval creates deletable global var bindings (configurable properties)
                // which should not block future lexical declarations in GlobalDeclarationInstantiation.
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the given name is a lexical declaration in the global environment.
    /// This checks the current scope's body lexical names and traverses enclosing scopes.
    /// Used by GlobalDeclarationInstantiation to detect var/function declarations that
    /// conflict with existing let/const/class bindings.
    /// </summary>
    public bool HasGlobalLexicalDeclaration(Symbol name)
    {
        // Check the current scope and all enclosing scopes for the lexical name
        var current = this;
        while (current is not null)
        {
            if (current.HasBodyLexicalName(name))
            {
                return true;
            }

            // Also check if there's a lexical binding in _values
            if (current._values is not null && current._values.TryGetValue(name, out var binding) && binding.IsLexical)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    /// <summary>
    /// Checks if the given name is a lexical binding in the global environment chain.
    /// This traverses the entire chain from the given scope up to the root global scope
    /// to check for lexical bindings that might have been created in strict script wrappers.
    /// </summary>
    private static bool HasGlobalLexicalName(JsEnvironment scope, Symbol name)
    {
        var current = scope;
        while (current is not null)
        {
            if (current.HasBodyLexicalName(name))
            {
                return true;
            }

            // Also check if there's a lexical binding in _values
            if (current._values is not null && current._values.TryGetValue(name, out var binding) && binding.IsLexical)
            {
                return true;
            }

            // Continue to enclosing scopes (important for strict script wrappers)
            current = current.Enclosing;
        }

        return false;
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
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                // Check IsUninitialized before reading
                if (binding.IsUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        value = globalValue;
                        return true;
                    }
                }

                value = binding.JsValue.ToObject();
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryGet(name, out value))
            {
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get a binding value as JsValue, avoiding boxing for primitives.
    /// </summary>
    public bool TryGetJsValue(Symbol name, out JsValue value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                // Check IsUninitialized before reading
                if (binding.IsUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        value = JsValue.FromObject(globalValue);
                        return true;
                    }
                }

                value = binding.JsValue;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryGetJsValue(name, out value))
            {
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out var withValue))
            {
                value = JsValue.FromObject(withValue);
                return true;
            }

            current = current.Enclosing;
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            value = JsValue.FromObject(propertyValue);
            return true;
        }

        value = default;
        return false;
    }

    internal bool TryFindBinding(Symbol name, out JsEnvironment environment, out object? value)
    {
        return TryFindBinding(name, false, out environment, out value);
    }

    internal bool TryFindBinding(Symbol name, bool allowUninitialized, out JsEnvironment environment,
        out object? value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                // Check IsUninitialized before reading
                if (binding.IsUninitialized && !allowUninitialized)
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                environment = current;
                value = binding.JsValue.ToObject();
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryFindBinding(name, allowUninitialized, out environment, out value))
            {
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
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            JsObject? globalObject = null;
            if (current.IsGlobalFunctionScope)
            {
                globalObject = current.GetRootGlobalObject();
            }
            var realm = current.RealmState ?? current.Enclosing?.RealmState;

            if (current._values is not null)
            {
                ref var binding = ref current._values.GetValueRefOrNullRef(name);
                if (!Unsafe.IsNullRef(ref binding))
                {
                    // Check IsUninitialized before reading
                    if (binding.IsUninitialized &&
                        binding.IsLexical &&
                        !Equals(name, Symbol.This))
                    {
                        throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
                    }

                    if (binding.IsConst)
                    {
                        throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError($"Cannot reassign constant '{name.Name}'.",
                            realm: realm)));
                    }

                    if (binding.IsImmutableBinding)
                    {
                        // Immutable bindings (named function expression names) throw in strict mode,
                        // but silently fail in non-strict mode
                        if (isStrictContext)
                        {
                            throw new ThrowSignal(JsValue.FromObject(StandardLibrary.CreateTypeError($"Cannot reassign constant '{name.Name}'.",
                                realm: realm)));
                        }

                        return;
                    }

                    if (binding.IsGlobalConstant)
                    {
                        if (isStrictContext)
                        {
                            throw new ThrowSignal(JsValue.FromObject(
                                StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable",
                                    realm: realm)));
                        }

                        return;
                    }

                    binding.JsValue = JsValue.FromObject(value);
                    if (!binding.IsLexical)
                    {
                        globalObject?.SetProperty(name.Name, JsValue.FromObject(value));
                    }
                    current.NotifyBindingObservers(name, value);
                    return;
                }
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                current._varEnvironmentOverride.AssignInternal(name, value, isStrictContext);
                return;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                if (current._withObject is JsObject withObject)
                {
                    AssignmentReferenceResolver.AssignObjectProperty(withObject, name.Name, value, isStrictContext, null,
                        realm);
                }
                else
                {
                    current._withObject.SetProperty(name.Name, JsValue.FromObject(value));
                }

                return;
            }

            if (current.Enclosing is null)
            {
                // Reached the global scope without finding the variable
                if (globalObject is not null && globalObject.GetOwnPropertyDescriptor(name.Name) is not null)
                {
                    AssignmentReferenceResolver.AssignObjectProperty(globalObject, name.Name, value, isStrictContext, null,
                        realm);
                    return;
                }

                // In strict mode, assignment to undefined variable is an error
                // In non-strict mode, create the variable as a global
                var functionScope = current.GetFunctionScope();
                if (functionScope.HasBodyLexicalName(name))
                {
                    RealmState?.Logger?.LogInformation(
                        "AssignUnresolvable blocked by body lexical name={Name} strict={Strict} env={Env}",
                        name.Name, isStrictContext, GetHashCode());
                    throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
                }

                if (isStrictContext)
                {
                    // Use ReferenceError message format
                    throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
                }

                // Non-strict mode: Create the variable in the global scope (this environment)
                current.DefineJsValue(name, JsValue.FromObject(value));
                globalObject?.SetProperty(name.Name, JsValue.FromObject(value));
                return;
            }

            current = current.Enclosing;
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

            if (current._values is not null && current._values.TryGetValue(name, out var binding))
            {
                return current.TryDeleteDeclarativeBinding(name, binding)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            current = current.Enclosing;
        }

        var globalObject = GetRootGlobalObject();
        var descriptor = globalObject?.GetOwnPropertyDescriptor(name.Name);
        if (descriptor != null)
        {
            if (!descriptor.Configurable)
            {
                return DeleteBindingResult.NotDeletable;
            }

            globalObject.Delete(name.Name);
            return DeleteBindingResult.Deleted;
        }

        return DeleteBindingResult.NotFound;
    }

    private bool TryDeleteDeclarativeBinding(Symbol name, Binding binding)
    {
        if (binding.IsLexical || binding.IsConst || binding.IsGlobalConstant || binding.BlocksFunctionScopeOverride)
        {
            return false;
        }

        if (binding.CanDelete)
        {
            _values?.Remove(name);
            return true;
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
            _values?.Remove(name);
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

        if (current._values is not null &&
            current._values.TryGetValue(Symbol.This, out var thisBinding) &&
            thisBinding.JsValue.ToObject() is JsObject globalObject)
        {
            return globalObject;
        }

        return null;
    }


    private static bool IsBlockedByUnscopables(IJsObjectLike target, string name, out bool touchedUnscopables)
    {
        touchedUnscopables = false;
        var key = SymbolKeys.Unscopables;
        if (target.TryGetProperty(key, out var unscopables))
        {
            touchedUnscopables = true;
            if (unscopables.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
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
            value = propertyValue.ToObject();
            return true;
        }

        if (target.TryGetProperty(propertyName, JsValue.FromObject(target), out var receiverValue))
        {
            value = receiverValue.ToObject();
            return true;
        }

        value = null;
        return false;
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

        const int maxDepth = 100;
        var depth = 0;
        IJsPropertyAccessor? prototypeAccessor =
            (target as IPrototypeAccessorProvider)?.PrototypeAccessor ?? target.Prototype;

        while (prototypeAccessor is not null && depth++ < maxDepth)
        {
            if (prototypeAccessor is JsObject protoObj)
            {
                if (protoObj.HasProperty(name))
                {
                    return true;
                }

                prototypeAccessor = protoObj.PrototypeAccessor ?? protoObj.Prototype;
                continue;
            }

            if (prototypeAccessor is IJsObjectLike objectLike)
            {
                if (objectLike.GetOwnPropertyDescriptor(name) is not null)
                {
                    return true;
                }

                prototypeAccessor =
                    (objectLike as IPrototypeAccessorProvider)?.PrototypeAccessor ?? objectLike.Prototype;
                continue;
            }

            if (prototypeAccessor.TryGetProperty(name, out _))
            {
                return true;
            }

            if (prototypeAccessor is IPrototypeAccessorProvider provider)
            {
                prototypeAccessor = provider.PrototypeAccessor;
                continue;
            }

            break;
        }

        return false;
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

    internal static bool TrySetWithBindingValue(in ObjectEnvironmentBinding binding, object? value,
        RealmState? realm = null)
    {
        var propertyName = binding.PropertyName;
        var bindingObject = binding.BindingObject;
        var stillExists = HasProperty(bindingObject, propertyName);
        if (!stillExists && !binding.AllowMissingAssignment)
        {
            if (binding.IsStrictReference)
            {
                realm ??= (bindingObject as JsObject)?.RealmState;
                throw StandardLibrary.ThrowReferenceError($"ReferenceError: {propertyName} is not defined",
                    realm: realm);
            }
        }

        if (bindingObject is JsObject jsObject)
        {
            AssignmentReferenceResolver.AssignObjectProperty(
                jsObject,
                propertyName,
                value,
                binding.IsStrictReference,
                null,
                realm ?? jsObject.RealmState,
                bindingObject);
            return true;
        }

        JsOps.AssignPropertyValueByName(bindingObject, propertyName, value);
        if (bindingObject is IJsObjectLike withObject &&
            bindingObject is IPropertyDefinitionHost definitionHost)
        {
            var ownDescriptor = withObject.GetOwnPropertyDescriptor(propertyName);
            if (ownDescriptor is not null && ownDescriptor.IsDataDescriptor)
            {
                var descriptorClone = ownDescriptor.Clone();
                descriptorClone.Value = value;
                definitionHost.TryDefineProperty(propertyName, descriptorClone);
            }
        }

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
        return scope._values is not null && scope._values.TryGetValue(name, out var binding) && !binding.IsLexical;
    }

    internal JsEnvironment GetFunctionScope()
    {
        if (_varEnvironmentOverride is not null)
        {
            return _varEnvironmentOverride.GetFunctionScope();
        }

        var current = this;
        while (!current.IsFunctionScope)
        {
            current = current.Enclosing
                      ?? throw new InvalidOperationException("Unable to locate function scope for var declaration.");
        }

        return current;
    }

    internal JsEnvironment GetVarEnvironment()
    {
        return _varEnvironmentOverride ?? GetFunctionScope();
    }

    internal void SetVarEnvironment(JsEnvironment environment)
    {
        _varEnvironmentOverride = environment;
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
            if (current._values is null)
            {
                current = current.Enclosing;
                continue;
            }

            foreach (var kvp in current._values)
            {
                if (!result.ContainsKey(kvp.Key.Name))
                {
                    result[kvp.Key.Name] = kvp.Value.JsValue.ToObject();
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

    [Flags]
    private enum BindingFlags : byte
    {
        None = 0,
        IsConst = 1,
        IsGlobalConstant = 2,
        IsLexical = 4,
        BlocksFunctionScopeOverride = 8,
        CanDelete = 16,
        IsImmutableBinding = 32,
        HasSpecialBinding = 64 // When set, _value holds ISpecialBinding
    }

    /// <summary>
    /// A binding in the environment. This is a struct to avoid per-binding heap allocations.
    /// Regular values are stored in _jsValue to avoid boxing primitives.
    /// For special bindings (async exports, imports), the HasSpecialBinding flag is set
    /// and _specialBinding holds an ISpecialBinding instance.
    /// </summary>
    private struct Binding
    {
        private JsValue _jsValue;
        private object? _specialBinding; // Only used when HasSpecialBinding flag is set
        private BindingFlags _flags;

        public Binding(
            object? value,
            bool isConst,
            bool isGlobalConstant,
            bool isLexical,
            bool blocksFunctionScopeOverride,
            bool canDelete,
            bool isImmutableBinding = false)
        {
            _jsValue = JsValue.FromObject(value);
            _specialBinding = null;
            _flags = BindingFlags.None;
            if (isConst) _flags |= BindingFlags.IsConst;
            if (isGlobalConstant) _flags |= BindingFlags.IsGlobalConstant;
            if (isLexical) _flags |= BindingFlags.IsLexical;
            if (blocksFunctionScopeOverride) _flags |= BindingFlags.BlocksFunctionScopeOverride;
            if (canDelete) _flags |= BindingFlags.CanDelete;
            if (isImmutableBinding) _flags |= BindingFlags.IsImmutableBinding;
        }

        /// <summary>
        /// Constructor that takes JsValue directly, avoiding boxing for primitives.
        /// </summary>
        public Binding(
            JsValue value,
            bool isConst,
            bool isGlobalConstant,
            bool isLexical,
            bool blocksFunctionScopeOverride,
            bool canDelete,
            bool isImmutableBinding = false)
        {
            _jsValue = value;
            _specialBinding = null;
            _flags = BindingFlags.None;
            if (isConst) _flags |= BindingFlags.IsConst;
            if (isGlobalConstant) _flags |= BindingFlags.IsGlobalConstant;
            if (isLexical) _flags |= BindingFlags.IsLexical;
            if (blocksFunctionScopeOverride) _flags |= BindingFlags.BlocksFunctionScopeOverride;
            if (canDelete) _flags |= BindingFlags.CanDelete;
            if (isImmutableBinding) _flags |= BindingFlags.IsImmutableBinding;
        }

        private Binding(ISpecialBinding special, BindingFlags flags)
        {
            _jsValue = default;
            _specialBinding = special;
            _flags = flags | BindingFlags.HasSpecialBinding;
        }

        public static Binding CreateAsyncExport(JsPromise promise, bool isConst, bool isLexical)
        {
            var flags = BindingFlags.None;
            if (isConst) flags |= BindingFlags.IsConst;
            if (isLexical) flags |= BindingFlags.IsLexical;
            return new Binding(new AsyncExportBinding(promise, isConst), flags);
        }

        public static Binding CreateImport(JsEnvironment sourceEnvironment, Symbol bindingName)
        {
            return new Binding(
                new ImportBindingWrapper(sourceEnvironment, bindingName),
                BindingFlags.IsConst | BindingFlags.IsLexical);
        }

        /// <summary>
        /// Gets or sets the value as JsValue directly, avoiding boxing for primitives.
        /// For special bindings, this still converts through object?.
        /// </summary>
        public JsValue JsValue
        {
            readonly get => (_flags & BindingFlags.HasSpecialBinding) != 0
                ? JsValue.FromObject(((ISpecialBinding)_specialBinding!).GetValue())
                : _jsValue;
            set
            {
                if ((_flags & BindingFlags.HasSpecialBinding) != 0)
                {
                    ((ISpecialBinding)_specialBinding!).SetValue(value.ToObject());
                }
                else
                {
                    _jsValue = value;
                }
            }
        }

        public readonly bool IsConst => (_flags & BindingFlags.HasSpecialBinding) != 0
            ? ((ISpecialBinding)_specialBinding!).IsConst
            : (_flags & BindingFlags.IsConst) != 0;

        public readonly bool IsGlobalConstant => (_flags & BindingFlags.IsGlobalConstant) != 0;

        public readonly bool IsLexical => (_flags & BindingFlags.IsLexical) != 0;

        public readonly bool BlocksFunctionScopeOverride => (_flags & BindingFlags.BlocksFunctionScopeOverride) != 0;

        public readonly bool CanDelete => (_flags & BindingFlags.CanDelete) != 0;

        public readonly bool IsImmutableBinding => (_flags & BindingFlags.IsImmutableBinding) != 0;

        public readonly bool IsImportBinding => (_flags & BindingFlags.HasSpecialBinding) != 0
            && _specialBinding is ImportBindingWrapper;

        /// <summary>
        /// Checks if this binding holds the Uninitialized sentinel without triggering ToObject().
        /// </summary>
        public readonly bool IsUninitialized =>
            (_flags & BindingFlags.HasSpecialBinding) == 0 &&
            ReferenceEquals(_jsValue.ObjectValue, Uninitialized);

        /// <summary>
        /// Gets the LiveExportBinding if this is a live export, otherwise null.
        /// Does not trigger ToObject() boxing.
        /// </summary>
        public readonly LiveExportBinding? LiveExportBindingOrNull =>
            (_flags & BindingFlags.HasSpecialBinding) == 0
                ? _jsValue.ObjectValue as LiveExportBinding
                : null;

        public void UpgradeLexical(bool isLexical, bool blocksFunctionScopeOverride)
        {
            if (isLexical)
            {
                _flags |= BindingFlags.IsLexical;
            }

            if (blocksFunctionScopeOverride)
            {
                _flags |= BindingFlags.BlocksFunctionScopeOverride;
            }
        }
    }

    /// <summary>
    /// Interface for special bindings that need custom get/set behavior.
    /// </summary>
    private interface ISpecialBinding
    {
        object? GetValue();
        void SetValue(object? value);
        bool IsConst { get; }
    }

    private sealed class AsyncExportBinding : ISpecialBinding
    {
        private readonly JsPromise _promise;
        private bool _resolved;
        private object? _resolvedValue;

        public AsyncExportBinding(JsPromise promise, bool isConst)
        {
            _promise = promise;
            IsConst = isConst;
        }

        public object? GetValue() => _resolved ? _resolvedValue : _promise.JsObject;

        public void SetValue(object? value)
        {
            if (ReferenceEquals(value, Uninitialized) || _resolved)
            {
                return;
            }

            _resolved = true;
            _resolvedValue = value;
            _promise.Resolve(JsValue.FromObject(value));
        }

        public bool IsConst { get; }
    }

    /// <summary>
    /// An import binding that proxies reads to the source module's environment.
    /// Import bindings are immutable (assignment throws TypeError).
    /// </summary>
    private sealed class ImportBindingWrapper(JsEnvironment sourceEnvironment, Symbol bindingName) : ISpecialBinding
    {
        public JsEnvironment SourceEnvironment { get; } = sourceEnvironment;
        public Symbol BindingName { get; } = bindingName;

        public object? GetValue() => SourceEnvironment.Get(BindingName);

        public void SetValue(object? value) =>
            throw new InvalidOperationException("TypeError: Cannot assign to import binding");

        public bool IsConst => true;
    }
}
